using System.Buffers;

namespace DevTerm.Core.Presenters;

/// <summary>
/// The ordered set of presenters a session's raw byte stream is fanned out to.
/// See docs/design/architecture.md and docs/design/presenters.md.
/// </summary>
public sealed class Pipeline
{
    // A plain mutable List<T>, synchronized with a lock — not ConcurrentQueue: this collection is
    // never dequeued from (AddPresenter only ever appends, Render only ever enumerates), so a queue's
    // producer/consumer naming and API don't match what it's actually used for. A lock is cheap here
    // since AddPresenter is rare (a UI thread opening a device control panel); Render (the session's
    // background read loop, called many times a second) only holds the lock long enough to copy a
    // snapshot, so the presenters' own Render calls never run while the lock is held.
    private readonly List<IPresenter> _presenters;
    private readonly Lock _gate = new();

    public Pipeline(IEnumerable<IPresenter> presenters)
    {
        ArgumentNullException.ThrowIfNull(presenters);
        _presenters = [.. presenters];
    }

    public IReadOnlyList<IPresenter> Presenters
    {
        get
        {
            lock (_gate)
            {
                return [.. _presenters];
            }
        }
    }

    /// <summary>
    /// Adds <paramref name="presenter"/> to this pipeline's live presenter list (a no-op if it's
    /// already present). This mutates the existing <see cref="Pipeline"/> instance rather than
    /// requiring a caller to build a replacement one, because <see cref="Session"/>'s read loop holds
    /// a single reference to this exact instance for the session's whole lifetime — a freshly-built
    /// <see cref="Pipeline"/> handed back from a "resolve/rebuild" style API would never be picked up
    /// by that already-running loop. Lets a device control panel bind a presenter like
    /// <c>ScpiReplyPresenter</c> onto an already-open connection.
    /// </summary>
    public void AddPresenter(IPresenter presenter)
    {
        ArgumentNullException.ThrowIfNull(presenter);
        lock (_gate)
        {
            if (!_presenters.Contains(presenter))
            {
                _presenters.Add(presenter);
            }
        }
    }

    /// <summary>
    /// Removes <paramref name="presenter"/> from this pipeline's live presenter list (a no-op if it
    /// isn't present) — the counterpart of <see cref="AddPresenter"/> for a presenter that only lives
    /// as long as one control panel (e.g. a device manifest's reply presenter), so reopening that
    /// panel doesn't stack up one more presenter per opening.
    /// </summary>
    public void RemovePresenter(IPresenter presenter)
    {
        ArgumentNullException.ThrowIfNull(presenter);
        lock (_gate)
        {
            _presenters.Remove(presenter);
        }
    }

    public IReadOnlyList<PresenterOutput> Render(ReadOnlySequence<byte> data)
    {
        IPresenter[] snapshot;
        lock (_gate)
        {
            snapshot = [.. _presenters];
        }

        var results = new List<PresenterOutput>(snapshot.Length);
        foreach (var presenter in snapshot)
        {
            foreach (var text in presenter.Render(data))
            {
                results.Add(new PresenterOutput(presenter.Name, text));
            }
        }

        return results;
    }
}
