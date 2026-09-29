using System.Text;
using System.Threading.Channels;

namespace DevTerm.Logging;

/// <summary>
/// Appends a session log to a stream while it's being captured: the header on construction, then
/// one line per <see cref="Write"/>. The actual (potentially slow) disk write happens on a
/// dedicated background task, not the caller's thread — <see cref="Write"/> only hands the record
/// off to a queue, so it never blocks a <see cref="DevTerm.Core.Sessions.Session"/> read loop on
/// disk I/O (a full disk, an AV scanner, or just a slow drive). Because the format is one
/// self-contained JSON object per line, a process that dies mid-capture leaves a file that's valid
/// up to its last complete line (<see cref="SessionLog.Load"/> tolerates a torn final line).
/// Thread-safe: records reach the file in the order <see cref="Write"/> is entered.
/// </summary>
public sealed class SessionLogWriter : IDisposable
{
    private static readonly byte[] _newline = "\n"u8.ToArray();

    private readonly Stream _stream;
    private readonly Lock _gate = new();
    private readonly bool _leaveOpen;
    private readonly Channel<Func<long, SessionLogRecord>> _channel = Channel.CreateUnbounded<Func<long, SessionLogRecord>>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _drainTask;
    private long _sequence;
    private bool _disposed;
    private bool _faulted;

    /// <param name="leaveOpen">Leave <paramref name="stream"/> open when this writer is disposed.</param>
    public SessionLogWriter(Stream stream, SessionLogHeader header, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(header);
        _stream = stream;
        _leaveOpen = leaveOpen;
        WriteLine(SessionLogFormat.WriteHeader(header));
        _drainTask = Task.Run(DrainAsync);
    }

    /// <summary>The file this writes to, when created by <see cref="Create"/>.</summary>
    public string? Path { get; private init; }

    /// <summary>How many records have actually reached the file so far (not merely handed to <see cref="Write"/>).</summary>
    public long RecordCount
    {
        get
        {
            lock (_gate)
            {
                return _sequence;
            }
        }
    }

    /// <summary>
    /// Whether a previous write failed (e.g. the disk filled). Once true, every further
    /// <see cref="Write(SessionLogRecord)"/> is a no-op instead of attempting another write — a
    /// second, successful write after a torn one would leave a malformed line that isn't the file's
    /// last line, which <see cref="SessionLog.Load"/> only tolerates at end-of-file. Because the
    /// actual write happens on a background task, a fault isn't necessarily visible the instant the
    /// record that caused it was handed to <see cref="Write"/> — only once that task gets to it.
    /// </summary>
    public bool IsFaulted
    {
        get
        {
            lock (_gate)
            {
                return _faulted;
            }
        }
    }

    /// <summary>
    /// Creates (or replaces) <paramref name="path"/>, creating its directory if needed. The file is
    /// opened shareable for reading, so a log can be opened for playback while it's still being
    /// written.
    /// </summary>
    public static SessionLogWriter Create(string path, SessionLogHeader header)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = System.IO.Path.GetFullPath(path);
        if (System.IO.Path.GetDirectoryName(fullPath) is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
        }

        var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        try
        {
            return new SessionLogWriter(stream, header) { Path = fullPath };
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>Writes <paramref name="record"/> as-is, keeping its own <see cref="SessionLogRecord.Sequence"/> (used to rewrite an already-numbered log — see <see cref="SessionLog.Write"/>).</summary>
    public void Write(SessionLogRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        Enqueue(_ => record);
    }

    /// <summary>Builds a new record with the next sequence number, assigned once it's actually this record's turn to be written — so sequence order always matches file order, even with the read loop and a sender writing concurrently.</summary>
    internal void Write(Func<long, SessionLogRecord> build) => Enqueue(build);

    private void Enqueue(Func<long, SessionLogRecord> build)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            // Once faulted, don't even queue the record - a caller like SessionLogger takes its
            // next sequence number as a side effect of it actually being written, and that number
            // should stop advancing along with the file once nothing more is actually being written.
            if (_faulted)
            {
                return;
            }

            _channel.Writer.TryWrite(build);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _channel.Writer.Complete();
        }

        // Block until the background task has written (or given up on) everything already
        // enqueued, so a file this writer is done with is fully flushed before this call returns.
        _drainTask.GetAwaiter().GetResult();

        if (!_leaveOpen)
        {
            _stream.Dispose();
        }
    }

    private async Task DrainAsync()
    {
        await foreach (var build in _channel.Reader.ReadAllAsync())
        {
            lock (_gate)
            {
                if (_faulted)
                {
                    continue;
                }

                try
                {
                    var record = build(++_sequence);
                    WriteLine(SessionLogFormat.WriteRecord(record));
                }
                catch
                {
                    // _faulted is set inside WriteLine; the file already has whatever partial
                    // write got through before the failure, which is fine - it's the torn last line.
                }
            }
        }
    }

    private void WriteLine(string line)
    {
        try
        {
            _stream.Write(Encoding.UTF8.GetBytes(line));
            _stream.Write(_newline);
            _stream.Flush();
        }
        catch
        {
            _faulted = true;
            throw;
        }
    }
}
