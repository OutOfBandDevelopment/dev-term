namespace DevTerm.Logging;

/// <summary>What a <see cref="SessionLogRecord"/> describes — its <c>"type"</c> in the file. See docs/design/session-logging.md.</summary>
public enum SessionLogRecordKind
{
    /// <summary><c>"session"</c> — the logger attached to a session (logging started, or followed a profile switch): which connection, and whether it was already open.</summary>
    Session,

    /// <summary><c>"open"</c> — the connection opened.</summary>
    Open,

    /// <summary><c>"close"</c> — the connection was closed on request (File &gt; Disconnect, quitting, a profile switch).</summary>
    Close,

    /// <summary><c>"disconnect"</c> — the session closed itself: a read/send failure, or the device hanging up.</summary>
    Disconnect,

    /// <summary><c>"tx"</c> — bytes handed to the transport.</summary>
    Tx,

    /// <summary><c>"rx"</c> — one chunk received from the device, exactly as the presenters were handed it.</summary>
    Rx,

    /// <summary><c>"note"</c> — a text annotation added during playback (markup). Not a captured event, so it has no sequence number.</summary>
    Note,

    /// <summary>A record type this version doesn't know (written by a newer dev-term) — kept verbatim so trimming/annotating a log never drops it.</summary>
    Unknown,
}

/// <summary>
/// One line of a session log after the header. Captured records (<see cref="SessionLogRecordKind.Session"/>
/// through <see cref="SessionLogRecordKind.Rx"/>) carry a <see cref="Sequence"/> number, assigned in
/// capture order; notes don't. See docs/design/session-logging.md for the on-disk shape.
/// </summary>
public sealed record SessionLogRecord
{
    public required SessionLogRecordKind Kind { get; init; }

    /// <summary>Capture order, starting at 1 and increasing by 1 per captured record; <see langword="null"/> for notes. A trimmed log keeps the original numbers, so gaps are expected.</summary>
    public long? Sequence { get; init; }

    /// <summary>When it happened (UTC). Captured timestamps come from a monotonic clock anchored at the log's start, so they never go backwards.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>The raw bytes of a <see cref="SessionLogRecordKind.Tx"/>/<see cref="SessionLogRecordKind.Rx"/> record; empty otherwise.</summary>
    public ReadOnlyMemory<byte> Data { get; init; }

    /// <summary>A note's text, or a <see cref="SessionLogRecordKind.Disconnect"/>'s error message (<see langword="null"/> for a clean hang-up).</summary>
    public string? Text { get; init; }

    /// <summary>A <see cref="SessionLogRecordKind.Session"/> record's connection definition (<c>tcp://192.168.0.107:23</c>).</summary>
    public string? Connection { get; init; }

    /// <summary>A <see cref="SessionLogRecordKind.Session"/> record's saved-profile name, when the connection is one.</summary>
    public string? Profile { get; init; }

    /// <summary>A <see cref="SessionLogRecordKind.Session"/> record's connection state when the logger attached (<c>open</c>/<c>closed</c>).</summary>
    public string? State { get; init; }

    /// <summary>For <see cref="SessionLogRecordKind.Unknown"/>: the record's JSON line exactly as read, written back unchanged.</summary>
    public string? RawJson { get; init; }

    public static SessionLogRecord Note(DateTimeOffset timestamp, string text) =>
        new() { Kind = SessionLogRecordKind.Note, Timestamp = timestamp, Text = text };
}
