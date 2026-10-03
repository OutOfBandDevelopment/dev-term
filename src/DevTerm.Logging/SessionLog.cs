using System.Text;

namespace DevTerm.Logging;

/// <summary>
/// A whole session log in memory: its header and every record, in file order. What playback reads,
/// and what trim/markup edit and save back. See docs/design/session-logging.md.
/// </summary>
public sealed class SessionLog
{
    // A list (editable) for a log held in memory, or an index over the file (read-only until a mutation materialises it).
    private IReadOnlyList<SessionLogRecord> _records;

    public SessionLog(SessionLogHeader header, IEnumerable<SessionLogRecord> records)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(records);
        Header = header;
        _records = [.. records];
    }

    private SessionLog(SessionLogHeader header, IndexedRecordList records)
    {
        Header = header;
        _records = records;
    }

    /// <summary>True while the records are read from disk on demand rather than held in memory (see <see cref="OpenIndexed"/>).</summary>
    public bool IsIndexed => _records is IndexedRecordList;

    public SessionLogHeader Header { get; }

    public IReadOnlyList<SessionLogRecord> Records => _records;

    /// <summary>Problems tolerated while loading (a torn final line from a capture that was cut short), for a front end to mention.</summary>
    public IReadOnlyList<string> Warnings { get; private init; } = [];

    /// <summary>
    /// The time every playback offset is measured from: the first record with a real timestamp, or
    /// the header's <see cref="SessionLogHeader.Created"/> if the log is empty or every record is an
    /// <see cref="SessionLogRecordKind.Unknown"/> one with no <c>"t"</c> field (which parses as
    /// <see cref="DateTimeOffset.MinValue"/> - using that as <see cref="Start"/> would make every
    /// later record's offset come out as billions of seconds).
    /// </summary>
    public DateTimeOffset Start
    {
        get
        {
            if (_records is IndexedRecordList indexed)
            {
                var first = indexed.FirstStartIndex();
                return first < 0 ? Header.Created : indexed.TimestampAt(first);
            }

            foreach (var record in _records)
            {
                if (record.Kind != SessionLogRecordKind.Unknown || record.Timestamp != DateTimeOffset.MinValue)
                {
                    return record.Timestamp;
                }
            }

            return Header.Created;
        }
    }

    /// <summary>How far into the log <paramref name="index"/>'s record is — never negative, even if a hand-edited file's timestamps go backwards.</summary>
    public TimeSpan OffsetOf(int index)
    {
        var timestamp = _records is IndexedRecordList indexed ? indexed.TimestampAt(index) : _records[index].Timestamp;
        var offset = timestamp - Start;
        return offset < TimeSpan.Zero ? TimeSpan.Zero : offset;
    }

    public TimeSpan Duration => _records.Count == 0 ? TimeSpan.Zero : OffsetOf(_records.Count - 1);

    /// <exception cref="SessionLogFormatException">The file isn't a session log, is a newer incompatible version, or has a malformed line before its last.</exception>
    public static SessionLog Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // FileShare.ReadWrite: a log still being captured (by this process or another) is open for
        // writing, and should still be viewable.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Read(stream);
    }

    /// <summary>
    /// Opens <paramref name="path"/> without loading it: one pass indexes every record's position and time, and
    /// records are then parsed on demand, so a very large capture opens fast and uses flat memory, and a seek
    /// reads only the page it lands on. Behaves like <see cref="Load"/> otherwise; an edit (<see cref="InsertNote"/>)
    /// brings the records into memory first.
    /// </summary>
    /// <exception cref="SessionLogFormatException">As <see cref="Load"/>.</exception>
    public static SessionLog OpenIndexed(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var (header, records, warnings) = IndexedRecordList.Scan(path);
        return new SessionLog(header, records) { Warnings = warnings };
    }

    /// <inheritdoc cref="Load"/>
    public static SessionLog Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);

        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        var firstLine = lines.FindIndex(l => l.Length > 0);
        if (firstLine < 0)
        {
            throw new SessionLogFormatException("Not a dev-term session log: the file is empty.");
        }

        var header = SessionLogFormat.ReadHeader(lines[firstLine]);
        var records = new List<SessionLogRecord>();
        var warnings = new List<string>();
        var lastLine = lines.FindLastIndex(l => l.Trim().Length > 0);
        for (var i = firstLine + 1; i < lines.Count; i++)
        {
            if (lines[i].Trim().Length == 0)
            {
                continue;
            }

            try
            {
                records.Add(SessionLogFormat.ReadRecord(lines[i]));
            }
            catch (SessionLogFormatException ex) when (i == lastLine)
            {
                // A capture cut short (the app killed mid-write) leaves at most one torn line, and
                // it's always the last - everything before it is still good.
                warnings.Add($"Line {i + 1} was incomplete and has been skipped ({ex.Message}).");
            }
            catch (SessionLogFormatException ex)
            {
                throw new SessionLogFormatException($"Line {i + 1}: {ex.Message}", ex);
            }
        }

        return new SessionLog(header, records) { Warnings = warnings };
    }

    /// <summary>Writes the whole log to <paramref name="path"/>, replacing it atomically (a temporary file renamed over it), so a failed save never leaves a half-written log behind.</summary>
    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (Path.GetDirectoryName(fullPath) is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = fullPath + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            Write(stream);
        }

        File.Move(temporary, fullPath, overwrite: true);
    }

    public void Write(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var writer = new SessionLogWriter(stream, Header, leaveOpen: true);
        foreach (var record in _records)
        {
            writer.Write(record);
        }
    }

    /// <summary>
    /// A new log holding only the records at positions <paramref name="start"/> (inclusive) to
    /// <paramref name="end"/> (exclusive) — the header is kept (with <see cref="SessionLogHeader.TrimmedFrom"/>
    /// set), and so are the records' original sequence numbers and timestamps, so a trimmed log's
    /// provenance stays traceable.
    /// </summary>
    public SessionLog Trim(int start, int end, string? trimmedFrom = null)
    {
        if (start < 0 || end > _records.Count || end <= start)
        {
            throw new ArgumentOutOfRangeException(nameof(start), $"The range [{start}, {end}) must select at least one of the {_records.Count} records.");
        }

        return new SessionLog(Header with { TrimmedFrom = trimmedFrom ?? Header.TrimmedFrom }, _records.Skip(start).Take(end - start));
    }

    /// <summary>
    /// Inserts a note at position <paramref name="index"/> (before the record currently there). Its
    /// timestamp is the record before it, so it plays back immediately after that record with no
    /// delay of its own.
    /// </summary>
    public SessionLogRecord InsertNote(int index, string text)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, _records.Count);
        ArgumentNullException.ThrowIfNull(text);

        var timestamp = index > 0 ? _records[index - 1].Timestamp : Start;
        var note = SessionLogRecord.Note(timestamp, text);
        var list = _records as List<SessionLogRecord> ?? [.. _records];
        list.Insert(index, note);
        _records = list;
        return note;
    }
}
