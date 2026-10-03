namespace DevTerm.Core.Sessions;

/// <summary>
/// Pacing and connect-retry limits a <see cref="Session"/> enforces itself, independent of any one transport
/// (docs/design/transports.md, decided 2026-10-03). Every value defaults to "off", so a session without limits
/// behaves exactly as before.
/// </summary>
public sealed record SessionLimits
{
    /// <summary>No limits: the behavior of a session built without any.</summary>
    public static SessionLimits None { get; } = new();

    /// <summary>Minimum milliseconds between the starts of two consecutive sends; 0 disables the limit. A send that arrives early waits.</summary>
    public int MinSendIntervalMs { get; init; }

    /// <summary>
    /// Minimum milliseconds between handling two consecutive received chunks; 0 disables the limit. Nothing is dropped:
    /// the read loop waits, and the transport's pipe holds the backlog.
    /// </summary>
    public int MinReadIntervalMs { get; init; }

    /// <summary>Milliseconds a single connect attempt may take before it is abandoned; 0 means no limit beyond the transport's own.</summary>
    public int ConnectTimeoutMs { get; init; }

    /// <summary>Extra connect attempts after the first one fails; 0 means a single attempt.</summary>
    public int ConnectRetries { get; init; }

    /// <summary>Milliseconds to wait between connect attempts.</summary>
    public int ConnectRetryDelayMs { get; init; } = 1000;
}
