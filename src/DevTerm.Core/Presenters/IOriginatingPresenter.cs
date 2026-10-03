namespace DevTerm.Core.Presenters;

/// <summary>
/// A presenter that can also originate traffic: it raises <see cref="Originated"/> with bytes the
/// owning <see cref="Sessions.Session"/> sends to the transport, as if typed. That lets a presenter act as a
/// simulation driver (poll a device from what it has just seen) or a virtual device (answer commands).
/// The session subscribes when the presenter is added and unsubscribes when it is removed, so it works
/// on a live connection without reconnecting. See docs/design/architecture.md.
/// </summary>
public interface IOriginatingPresenter : IPresenter
{
    /// <summary>Raised with the bytes to send. May be raised from inside <see cref="IPresenter.Render"/>.</summary>
    event EventHandler<ReadOnlyMemory<byte>>? Originated;
}
