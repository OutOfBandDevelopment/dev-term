using System.Buffers;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;

namespace DevTerm.Logging;

/// <summary>
/// Logger mode: records every byte a <see cref="Session"/> sends and receives, plus its
/// connect/disconnect events, to a session log — each record with a sequence number and a
/// timestamp. It's a passive <see cref="ISessionObserver"/>, so it never changes what the
/// presenters render. One logger follows the user across a live profile switch (<see cref="Attach"/>
/// the new session; a <c>session</c> record marks the switch), so a log is per logging run, not per
/// connection. See docs/design/session-logging.md.
/// </summary>
public sealed class SessionLogger : ISessionObserver, IDisposable
{
    private readonly SessionLogWriter _writer;
    private readonly TimeProvider _clock;
    private readonly DateTimeOffset _start;
    private readonly long _startTimestamp;
    private readonly Lock _gate = new();
    private long _sequence;
    private IDisposable? _registration;
    private bool _disposed;

    public SessionLogger(SessionLogWriter writer, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _writer = writer;
        _clock = clock ?? TimeProvider.System;
        _start = _clock.GetUtcNow();
        _startTimestamp = _clock.GetTimestamp();
    }

    /// <summary>
    /// Starts a new log at <paramref name="path"/> (replacing any file there). The header's
    /// <see cref="SessionLogHeader.Created"/> is taken from <paramref name="clock"/>, not from
    /// <paramref name="header"/>.
    /// </summary>
    public static SessionLogger Start(string path, SessionLogHeader header, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(header);
        clock ??= TimeProvider.System;
        var writer = SessionLogWriter.Create(path, header with { Created = clock.GetUtcNow() });
        return new SessionLogger(writer, clock);
    }

    /// <summary>The file being written, when started by <see cref="Start"/>.</summary>
    public string? Path => _writer.Path;

    /// <summary>How many captured records have been written so far.</summary>
    public long RecordCount => Interlocked.Read(ref _sequence);

    public bool IsActive => !_disposed;

    /// <summary>
    /// Starts following <paramref name="session"/> (detaching from whichever one it followed
    /// before) and writes a <c>session</c> record naming the connection and whether it was already
    /// open — so a log started mid-connection, or continued across a profile switch, still says
    /// what it's looking at.
    /// </summary>
    public void Attach(Session session, string? connection, string? profile = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _registration?.Dispose();
            var state = session.State == ConnectionState.Open ? "open" : "closed";
            Record(SessionLogRecordKind.Session, default, connection: connection, profile: profile, state: state);
            _registration = session.AddObserver(this);
        }
    }

    /// <summary>Stops following the current session without ending the log.</summary>
    public void Detach()
    {
        lock (_gate)
        {
            _registration?.Dispose();
            _registration = null;
        }
    }

    void ISessionObserver.OnOpened() => Record(SessionLogRecordKind.Open, default);

    void ISessionObserver.OnReceived(ReadOnlySequence<byte> data) => Record(SessionLogRecordKind.Rx, data.ToArray());

    void ISessionObserver.OnSent(ReadOnlyMemory<byte> data) => Record(SessionLogRecordKind.Tx, data.ToArray());

    void ISessionObserver.OnClosed(bool requested, Exception? error)
    {
        if (requested)
        {
            Record(SessionLogRecordKind.Close, default);
        }
        else
        {
            Record(SessionLogRecordKind.Disconnect, default, text: error?.Message);
        }
    }

    /// <summary>Stops logging: detaches from the session and closes the file.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _registration?.Dispose();
            _registration = null;
            _disposed = true;
            _writer.Dispose();
        }
    }

    private void Record(SessionLogRecordKind kind, ReadOnlyMemory<byte> data, string? text = null, string? connection = null, string? profile = null, string? state = null)
    {
        lock (_gate)
        {
            // An observer callback can still be in flight on the read loop while Dispose runs on
            // another thread; once disposed, it's simply not recorded.
            if (_disposed)
            {
                return;
            }

            // Sequence number and timestamp are taken under the same lock the line is written
            // under, so file order, sequence order, and timestamp order always agree.
            _writer.Write(() => new SessionLogRecord
            {
                Kind = kind,
                Sequence = Interlocked.Increment(ref _sequence),
                Timestamp = _start + _clock.GetElapsedTime(_startTimestamp),
                Data = data,
                Text = text,
                Connection = connection,
                Profile = profile,
                State = state,
            });
        }
    }
}
