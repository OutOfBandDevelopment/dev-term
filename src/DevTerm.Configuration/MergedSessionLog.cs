using System.Buffers;
using System.Globalization;
using System.Text;
using DevTerm.Core.Sessions;

namespace DevTerm.Configuration;

/// <summary>One line of the merged view: when it happened, on which device, and what moved which way.</summary>
public sealed record MergedLogEntry(DateTimeOffset Time, string Device, bool Sent, string Text)
{
    /// <summary><c>12:00:01.250 [scope] &lt; *IDN?</c> - <c>&gt;</c> for received, <c>&lt;</c> for sent.</summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Time.LocalDateTime:HH:mm:ss.fff} [{Device}] {(Sent ? "<" : ">")} {Text}");
}

/// <summary>
/// One time-ordered log of the raw traffic of every open tab (docs/design/multi-session-ui.md): each tracked
/// session's received and sent chunks, tagged with its device name and the time they arrived, interleaved in the
/// order they happened. A bounded ring (<see cref="MaxEntries"/>), independent of per-tab file logging. Thread-safe;
/// <see cref="EntryAdded"/> is raised on whichever thread the session raised the chunk on.
/// </summary>
public sealed class MergedSessionLog
{
    public const int MaxEntries = 5000;

    private readonly Lock _gate = new();
    private readonly Dictionary<object, IDisposable> _subscriptions = [];
    private readonly Queue<MergedLogEntry> _entries = new();
    private readonly TimeProvider _clock;

    public MergedSessionLog(TimeProvider? clock = null) => _clock = clock ?? TimeProvider.System;

    public event EventHandler<MergedLogEntry>? EntryAdded;

    public IReadOnlyList<MergedLogEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries];
            }
        }
    }

    /// <summary>Starts recording <paramref name="session"/> under <paramref name="key"/> (a tab); re-tracking the key re-points it.</summary>
    public void Track(object key, Session session, string deviceName)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(session);
        lock (_gate)
        {
            _subscriptions.Remove(key, out var previous);
            previous?.Dispose();
            _subscriptions[key] = session.AddObserver(new Observer(this, deviceName));
        }
    }

    public void Untrack(object key)
    {
        lock (_gate)
        {
            if (_subscriptions.Remove(key, out var subscription))
            {
                subscription.Dispose();
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    /// <summary>Control bytes shown as <c>\r</c>, <c>\n</c>, <c>\t</c> or <c>\xNN</c>; everything else as Latin-1.</summary>
    public static string Escape(ReadOnlySpan<byte> data)
    {
        var builder = new StringBuilder(data.Length);
        foreach (var b in data)
        {
            switch (b)
            {
                case (byte)'\r': builder.Append("\\r"); break;
                case (byte)'\n': builder.Append("\\n"); break;
                case (byte)'\t': builder.Append("\\t"); break;
                case < 0x20 or 0x7F: builder.Append(CultureInfo.InvariantCulture, $"\\x{b:X2}"); break;
                default: builder.Append((char)b); break;
            }
        }

        return builder.ToString();
    }

    private void Add(string device, bool sent, ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return;
        }

        var entry = new MergedLogEntry(_clock.GetLocalNow(), device, sent, Escape(data));
        lock (_gate)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > MaxEntries)
            {
                _entries.Dequeue();
            }
        }

        EntryAdded?.Invoke(this, entry);
    }

    private sealed class Observer(MergedSessionLog owner, string device) : ISessionObserver
    {
        public void OnOpened() => owner.AddNote(device, "connected");

        public void OnReceived(ReadOnlySequence<byte> data) => owner.Add(device, sent: false, data.IsSingleSegment ? data.FirstSpan : data.ToArray());

        public void OnSent(ReadOnlyMemory<byte> data) => owner.Add(device, sent: true, data.Span);

        public void OnClosed(bool requested, Exception? error) =>
            owner.AddNote(device, error is null ? "disconnected" : $"disconnected: {error.Message}");
    }

    private void AddNote(string device, string text)
    {
        var entry = new MergedLogEntry(_clock.GetLocalNow(), device, Sent: false, $"-- {text} --");
        lock (_gate)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > MaxEntries)
            {
                _entries.Dequeue();
            }
        }

        EntryAdded?.Invoke(this, entry);
    }
}
