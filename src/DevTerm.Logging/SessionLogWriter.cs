using System.Text;

namespace DevTerm.Logging;

/// <summary>
/// Appends a session log to a stream while it's being captured: the header on construction, then
/// one line per <see cref="Write"/>, each flushed as it's written. Because the format is one
/// self-contained JSON object per line, a process that dies mid-capture leaves a file that's valid
/// up to its last complete line (<see cref="SessionLog.Load"/> tolerates a torn final line).
/// Thread-safe: records are written in the order <see cref="Write"/> is entered.
/// </summary>
public sealed class SessionLogWriter : IDisposable
{
    private static readonly byte[] _newline = "\n"u8.ToArray();

    private readonly Stream _stream;
    private readonly Lock _gate = new();
    private readonly bool _leaveOpen;
    private bool _disposed;

    /// <param name="leaveOpen">Leave <paramref name="stream"/> open when this writer is disposed.</param>
    public SessionLogWriter(Stream stream, SessionLogHeader header, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(header);
        _stream = stream;
        _leaveOpen = leaveOpen;
        WriteLine(SessionLogFormat.WriteHeader(header));
    }

    /// <summary>The file this writes to, when created by <see cref="Create"/>.</summary>
    public string? Path { get; private init; }

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

    public void Write(SessionLogRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var line = SessionLogFormat.WriteRecord(record);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            WriteLine(line);
        }
    }

    /// <summary>Assigns the next sequence number and writes the record built from it, atomically — so sequence order always matches file order, even with the read loop and a sender writing concurrently.</summary>
    internal void Write(Func<SessionLogRecord> build)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            WriteLine(SessionLogFormat.WriteRecord(build()));
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
            if (!_leaveOpen)
            {
                _stream.Dispose();
            }
        }
    }

    private void WriteLine(string line)
    {
        _stream.Write(Encoding.UTF8.GetBytes(line));
        _stream.Write(_newline);
        _stream.Flush();
    }
}
