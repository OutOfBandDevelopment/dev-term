namespace DevTerm.Logging;

/// <summary>
/// The first line of every session log: identifies the file as one, its format version, and
/// describes the connection it was captured from. See docs/design/session-logging.md.
/// </summary>
public sealed record SessionLogHeader
{
    /// <summary>The header's <c>"format"</c> value — what makes a file a dev-term session log rather than any other JSON Lines file.</summary>
    public const string FormatName = "dev-term-session-log";

    /// <summary>
    /// The version this build writes, and the newest it reads. Bumped only for an incompatible
    /// change; new optional fields and new record types don't need one (readers ignore fields they
    /// don't know and keep record types they don't know verbatim).
    /// </summary>
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    /// <summary>When logging started (UTC). The first captured record's timestamp is at or after this.</summary>
    public required DateTimeOffset Created { get; init; }

    /// <summary>What wrote the file, e.g. <c>dev-term 1.0.0 (tui)</c> — informational.</summary>
    public string? Application { get; init; }

    /// <summary>The connection definition at the start (<c>tcp://192.168.0.107:23</c>, <c>serial://COM3:9600,8,n,1</c>). Later <c>session</c> records describe any switch.</summary>
    public string? Connection { get; init; }

    /// <summary>The saved profile's name when the connection was one, otherwise <see langword="null"/>.</summary>
    public string? Profile { get; init; }

    /// <summary>The transport name (<c>tcp</c>, <c>serial</c>, <c>hid</c>, ...).</summary>
    public string? Transport { get; init; }

    /// <summary>The presenters that were displaying the traffic — playback's default presenter choice.</summary>
    public IReadOnlyList<string> Presenters { get; init; } = [];

    /// <summary>The send format (parser) in use when logging started.</summary>
    public string? Parser { get; init; }

    /// <summary>Set on a trimmed log: the file name it was cut from.</summary>
    public string? TrimmedFrom { get; init; }
}
