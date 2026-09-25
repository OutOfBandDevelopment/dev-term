namespace DevTerm.Core.Sessions;

/// <summary>
/// Why a <see cref="Session"/> disconnected on its own (see <see cref="Session.Disconnected"/>) —
/// never raised for a disconnect the caller asked for via <see cref="Session.CloseAsync"/>.
/// </summary>
public sealed class SessionDisconnectedEventArgs : EventArgs
{
    public SessionDisconnectedEventArgs(Exception? error) => Error = error;

    /// <summary>
    /// The failure that ended the connection (a read or send error), or <see langword="null"/>
    /// when the device/peer closed the connection cleanly (e.g. a TCP peer hanging up).
    /// </summary>
    public Exception? Error { get; }

    /// <summary>A one-line, user-facing description of why the connection ended.</summary>
    public string Message => Error is null
        ? "The device closed the connection."
        : $"Connection lost: {Error.Message}";
}
