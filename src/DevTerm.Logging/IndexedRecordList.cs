using System.Collections;
using System.Runtime.InteropServices;
using System.Text;

namespace DevTerm.Logging;

/// <summary>
/// The records of a log file that stays on disk: one scan up front keeps each record's byte offset and
/// timestamp (about 17 bytes a record), and a record is parsed only when asked for, a page at a time, so a
/// multi-hour capture opens quickly and costs flat memory. The file is opened per page read and never held, so
/// the log can still be replaced (a note saved in place) or appended to (still being captured).
/// </summary>
internal sealed class IndexedRecordList : IReadOnlyList<SessionLogRecord>
{
    private const int _pageSize = 256;
    private const int _cachedPages = 8;

    private readonly string _path;
    private readonly long[] _offsets;
    private readonly long[] _ticks;
    private readonly bool[] _noStart;
    private readonly Dictionary<int, SessionLogRecord[]> _pages = [];
    private readonly Queue<int> _pageOrder = new();
    private readonly Lock _gate = new();

    private IndexedRecordList(string path, long[] offsets, long[] ticks, bool[] noStart)
    {
        _path = path;
        _offsets = offsets;
        _ticks = ticks;
        _noStart = noStart;
    }

    public int Count => _offsets.Length;

    public SessionLogRecord this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
            return Page(index / _pageSize)[index % _pageSize];
        }
    }

    public DateTimeOffset TimestampAt(int index) => new(_ticks[index], TimeSpan.Zero);

    /// <summary>The first record that can anchor playback time (see <see cref="SessionLog.Start"/>), or -1.</summary>
    public int FirstStartIndex() => Array.FindIndex(_noStart, skip => !skip);

    /// <summary>Scans <paramref name="path"/>: the header, and every record's offset and timestamp. Same torn-last-line tolerance as <see cref="SessionLog.Read"/>.</summary>
    public static (SessionLogHeader Header, IndexedRecordList Records, List<string> Warnings) Scan(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536);
        SessionLogHeader? header = null;
        var offsets = new List<long>();
        var ticks = new List<long>();
        var noStart = new List<bool>();
        var warnings = new List<string>();
        (int Number, SessionLogFormatException Error)? torn = null;
        var number = 0;
        foreach (var line in ReadLines(stream))
        {
            number++;
            if (line.Blank)
            {
                continue;
            }

            if (torn is { } failed)
            {
                // A malformed line is only tolerated when it is the last one in the file.
                throw new SessionLogFormatException($"Line {failed.Number}: {failed.Error.Message}", failed.Error);
            }

            if (header is null)
            {
                header = SessionLogFormat.ReadHeader(line.Text);
                continue;
            }

            try
            {
                var record = SessionLogFormat.ReadRecord(line.Text);
                offsets.Add(line.Offset);
                ticks.Add(record.Timestamp.UtcTicks);
                noStart.Add(record.Kind == SessionLogRecordKind.Unknown && record.Timestamp == DateTimeOffset.MinValue);
            }
            catch (SessionLogFormatException ex)
            {
                torn = (number, ex);
            }
        }

        if (header is null)
        {
            throw new SessionLogFormatException("Not a dev-term session log: the file is empty.");
        }

        if (torn is { } last)
        {
            // A capture cut short leaves at most one torn line, always the last; everything before it is still good.
            warnings.Add($"Line {last.Number} was incomplete and has been skipped ({last.Error.Message}).");
        }

        return (header, new IndexedRecordList(path, [.. offsets], [.. ticks], [.. noStart]), warnings);
    }

    public IEnumerator<SessionLogRecord> GetEnumerator()
    {
        for (var i = 0; i < Count; i++)
        {
            yield return this[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private SessionLogRecord[] Page(int page)
    {
        lock (_gate)
        {
            if (_pages.TryGetValue(page, out var cached))
            {
                return cached;
            }

            var first = page * _pageSize;
            var count = Math.Min(_pageSize, Count - first);
            var records = new SessionLogRecord[count];
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536);
            stream.Seek(_offsets[first], SeekOrigin.Begin);
            using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false);
            for (var i = 0; i < count; i++)
            {
                // Blank lines between records were skipped by the scan; do the same here.
                string? line;
                do
                {
                    line = reader.ReadLine();
                }
                while (line is not null && line.Trim().Length == 0);

                records[i] = line is null
                    ? throw new SessionLogFormatException("The log changed on disk after it was opened.")
                    : SessionLogFormat.ReadRecord(line);
            }

            _pages[page] = records;
            _pageOrder.Enqueue(page);
            while (_pageOrder.Count > _cachedPages)
            {
                _pages.Remove(_pageOrder.Dequeue());
            }

            return records;
        }
    }

    private readonly record struct Line(long Offset, string Text)
    {
        public bool Blank => Text.AsSpan().Trim().Length == 0;
    }

    /// <summary>Splits on LF over the raw bytes (so offsets are exact), dropping a trailing CR and a leading BOM.</summary>
    private static IEnumerable<Line> ReadLines(Stream stream)
    {
        var buffer = new byte[65536];
        var pending = new List<byte>();
        long position = 0;
        long lineStart = 0;
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                if (buffer[i] == (byte)'\n')
                {
                    yield return MakeLine(lineStart, pending);
                    pending.Clear();
                    lineStart = position + i + 1;
                }
                else
                {
                    pending.Add(buffer[i]);
                }
            }

            position += read;
        }

        if (pending.Count > 0)
        {
            yield return MakeLine(lineStart, pending);
        }
    }

    private static Line MakeLine(long offset, List<byte> bytes)
    {
        var span = CollectionsMarshal.AsSpan(bytes);
        if (offset == 0 && span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF)
        {
            span = span[3..];
            offset = 3;
        }

        if (span.Length > 0 && span[^1] == (byte)'\r')
        {
            span = span[..^1];
        }

        return new Line(offset, Encoding.UTF8.GetString(span));
    }
}
