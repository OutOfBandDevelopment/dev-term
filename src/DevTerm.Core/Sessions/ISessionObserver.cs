using System.Buffers;

namespace DevTerm.Core.Sessions;

/// <summary>
/// A passive tap on a <see cref="Session"/>'s raw traffic and lifecycle, registered with
/// <see cref="Session.AddObserver"/> — what the session logger (<c>DevTerm.Logging.SessionLogger</c>)
/// records from. It sees exactly the bytes the presenter pipeline sees (received) and exactly the
/// bytes handed to the transport (sent), without being part of the pipeline itself, so attaching
/// or detaching one never changes what any presenter renders.
/// </summary>
/// <remarks>
/// Callbacks run synchronously on whichever thread raised them — <see cref="OnReceived"/> on the
/// session's background read loop, <see cref="OnSent"/> on the sender's thread — so an observer
/// must be thread-safe, must copy any data it keeps (the buffers are only valid for the duration of
/// the call), and should return quickly. An observer that throws is ignored (logged via
/// <c>Debug.WriteLine</c>): a failing log file must never take a live connection down with it.
/// Takes <see cref="ReadOnlySequence{T}"/>/<see cref="ReadOnlyMemory{T}"/> rather than a span so the
/// interface stays mockable (see CLAUDE.md).
/// </remarks>
public interface ISessionObserver
{
    /// <summary>The session's transport opened successfully.</summary>
    void OnOpened();

    /// <summary>
    /// A chunk arrived from the device, before it's rendered — exactly the chunk every presenter is
    /// about to be handed, one call per read.
    /// </summary>
    void OnReceived(ReadOnlySequence<byte> data);

    /// <summary>
    /// <paramref name="data"/> is about to be written to the transport. Raised before the write, not
    /// after it, so a device's reply that races the write's completion can never be recorded ahead of
    /// the request that caused it; a write that then fails is followed by
    /// <see cref="OnClosed"/> with the failure.
    /// </summary>
    void OnSent(ReadOnlyMemory<byte> data);

    /// <summary>
    /// The connection closed — <paramref name="requested"/> is <see langword="true"/> for a caller's
    /// own <see cref="Session.CloseAsync"/>/<see cref="Session.DisposeAsync"/>, <see langword="false"/>
    /// when the session closed itself (see <see cref="Session.Disconnected"/>), in which case
    /// <paramref name="error"/> is the failure, or <see langword="null"/> for a clean hang-up by the
    /// device. Only raised for a connection that was actually open.
    /// </summary>
    void OnClosed(bool requested, Exception? error);
}
