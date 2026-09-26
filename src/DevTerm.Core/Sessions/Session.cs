using System.Diagnostics;
using DevTerm.Core.Presenters;
using DevTerm.Core.Transports;

namespace DevTerm.Core.Sessions;

/// <summary>
/// Binds one <see cref="ITransport"/> to a <see cref="Pipeline"/> of presenters for a single
/// logical connection to a device. See docs/design/architecture.md.
///
/// A connection that ends on its own — the read side fails, the device closes it, or a send
/// fails — is closed here and reported once through <see cref="Disconnected"/>, so every front end
/// (and every control panel sharing this session) can show why and offer to reconnect, instead of
/// each one having to notice a dead read loop or a failed write for itself. The session can be
/// reopened with <see cref="OpenAsync"/> afterward.
/// </summary>
public sealed class Session : IAsyncDisposable
{
    private readonly ITransport _transport;
    private readonly Pipeline _pipeline;

    // Serializes open/close so a self-initiated close (a fault) and a caller's CloseAsync/OpenAsync
    // can't interleave - e.g. a read-loop fault racing a user clicking Disconnect.
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);

    private CancellationTokenSource? _readLoopCts;
    private Task? _readLoopTask;

    // Bumped on every open and close. A fault captured under one generation is ignored once the
    // connection it belonged to is gone (already closed or reopened) - so a late fault from a
    // previous connection can never tear down, or be reported against, a newer one.
    private int _generation;

    // Copy-on-write: AddObserver/remove replace the array under _observersGate, and every notify
    // reads one snapshot without locking - notifications happen on the read loop many times a
    // second, subscriptions only when logging starts or stops.
    private readonly Lock _observersGate = new();
    private ISessionObserver[] _observers = [];

    public Session(ITransport transport, Pipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(pipeline);

        _transport = transport;
        _pipeline = pipeline;
    }

    public ConnectionState State => _transport.State;

    public IReadOnlyList<IPresenter> Presenters => _pipeline.Presenters;

    /// <summary>
    /// Binds <paramref name="presenter"/> into this session's live pipeline in place (see
    /// <see cref="Pipeline.AddPresenter"/>) so a device control panel opened after the session was
    /// built can still have its replies decoded/correlated, without requiring the presenter to have
    /// been part of the connection's original <c>CliOptions.EffectivePresenters</c> selection.
    /// </summary>
    public void AddPresenter(IPresenter presenter) => _pipeline.AddPresenter(presenter);

    /// <summary>Unbinds a presenter previously bound with <see cref="AddPresenter"/> (see <see cref="Pipeline.RemovePresenter"/>).</summary>
    public void RemovePresenter(IPresenter presenter) => _pipeline.RemovePresenter(presenter);

    public event EventHandler<PresenterOutput>? Output;

    /// <summary>
    /// Registers a passive tap on this session's raw traffic and lifecycle (see
    /// <see cref="ISessionObserver"/>) - e.g. the session logger. Dispose the returned handle to
    /// detach it. Doesn't touch the presenter pipeline.
    /// </summary>
    public IDisposable AddObserver(ISessionObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        lock (_observersGate)
        {
            _observers = [.. _observers, observer];
        }

        return new ObserverRegistration(this, observer);
    }

    private void RemoveObserver(ISessionObserver observer)
    {
        lock (_observersGate)
        {
            _observers = [.. _observers.Where(o => !ReferenceEquals(o, observer))];
        }
    }

    // An observer's own failure (a full disk under the session logger, say) must never take the
    // connection down with it, so each callback is isolated.
    private void Notify(Action<ISessionObserver> callback)
    {
        foreach (var observer in Volatile.Read(ref _observers))
        {
            try
            {
                callback(observer);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Session: an observer threw: {ex}");
            }
        }
    }

    /// <summary>
    /// Raised (on a background thread) after the session has closed itself because the connection
    /// ended without being asked to: a read failure, the device closing it, or a failed send. Not
    /// raised for <see cref="CloseAsync"/>/<see cref="DisposeAsync"/>.
    /// </summary>
    public event EventHandler<SessionDisconnectedEventArgs>? Disconnected;

    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_readLoopTask is not null)
            {
                // Already open (or opening) under this same lock - a second call (e.g. a slow
                // connect racing a second Connect click) must not start a second read loop on the
                // same PipeReader. See docs/bugs/fixed/001-session-double-open.md.
                return;
            }

            await _transport.OpenAsync(cancellationToken).ConfigureAwait(false);

            // A presenter (a pending SCPI/manifest reply queue, a partial ASCII line) survives
            // Close/OpenAsync on this same Session instance - without this, a stale pending id or a
            // half-received line from the previous connection carried into the new one. See
            // docs/bugs/fixed/006-reply-queue-desync.md.
            foreach (var presenter in _pipeline.Presenters.OfType<IResettablePresenter>())
            {
                presenter.Reset();
            }

            // Before the read loop starts, so an observer always sees "opened" ahead of the first
            // received chunk.
            Notify(o => o.OnOpened());

            var generation = ++_generation;

            // A fresh CancellationTokenSource each time, not one reused for the Session's whole
            // lifetime: a CTS can only ever be cancelled once, so re-opening after a Close (a real
            // scenario now that front ends have a Connect/Disconnect menu item) would otherwise start
            // the new read loop with an already-cancelled token, ending it immediately.
            var cts = new CancellationTokenSource();
            _readLoopCts = cts;

            // CancellationToken.None, deliberately: OpenAsync's own cancellationToken governs opening
            // the transport above, not the read loop's lifetime - that's _readLoopCts.Token, owned by
            // StopAsync. Forwarding OpenAsync's token here would let it cancel Task.Run's scheduling
            // before PumpAsync ever starts, leaving the loop silently never running while the caller
            // still sees an open connection.
            _readLoopTask = Task.Run(() => PumpAsync(generation, cts.Token), CancellationToken.None);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <summary>
    /// Closes the connection. Safe to call at any time, including when already closed or after
    /// the session disconnected on its own; never throws for a failure while closing the
    /// transport, since the connection is going away regardless.
    /// </summary>
    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await StopAsync(cancellationToken, requested: true, error: null).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <summary>
    /// Sends <paramref name="data"/>. A failure here is a device I/O failure (the bytes are
    /// already encoded - input validation happens before this), so the session closes itself,
    /// raises <see cref="Disconnected"/>, and then rethrows so the caller knows this send failed.
    /// </summary>
    public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        var generation = Volatile.Read(ref _generation);
        var wasOpen = State == ConnectionState.Open;

        // Before the write, so a reply racing the write's completion can't be observed first.
        // Only for an open connection - a write attempted while closed is never really sent.
        if (wasOpen)
        {
            Notify(o => o.OnSent(data));
        }

        try
        {
            await _transport.WriteAsync(data, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (wasOpen && !(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            await FaultAsync(generation, ex).ConfigureAwait(false);
            throw;
        }
    }

    private async Task PumpAsync(int generation, CancellationToken cancellationToken)
    {
        Exception? error = null;
        try
        {
            var reader = _transport.Input;
            while (true)
            {
                var result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                var buffer = result.Buffer;

                if (!buffer.IsEmpty)
                {
                    Notify(o => o.OnReceived(buffer));
                    foreach (var output in _pipeline.Render(buffer))
                    {
                        Output?.Invoke(this, output);
                    }
                }

                reader.AdvanceTo(buffer.End);

                if (result.IsCanceled && cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (result.IsCompleted || result.IsCanceled)
                {
                    // The transport's input ended without StopAsync asking it to - the device or
                    // peer closed the connection.
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected when CloseAsync/DisposeAsync cancels the read loop.
            return;
        }
        catch (Exception ex)
        {
            // A read failure (the transport completed its pipe with an exception - an unplugged
            // cable, a reset socket) or a presenter failing to render. Either way the connection
            // can't be trusted any more.
            error = ex;
        }

        // Not awaited: FaultAsync closes the session, which awaits this very read loop.
        _ = Task.Run(() => FaultAsync(generation, error), CancellationToken.None);
    }

    private async Task FaultAsync(int generation, Exception? error)
    {
        await _lifecycleLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (generation != _generation || _readLoopTask is null)
            {
                // That connection is already gone (closed by the caller, or already faulted) -
                // nothing to tear down or report.
                return;
            }

            await StopAsync(CancellationToken.None, requested: false, error).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleLock.Release();
        }

        try
        {
            Disconnected?.Invoke(this, new SessionDisconnectedEventArgs(error));
        }
        catch (Exception ex)
        {
            // A subscriber's own failure must not become an unobserved background crash.
            Debug.WriteLine($"Session.Disconnected handler threw: {ex}");
        }
    }

    // Caller holds _lifecycleLock. requested/error are only what observers are told about why.
    private async Task StopAsync(CancellationToken cancellationToken, bool requested, Exception? error)
    {
        _generation++;
        var wasOpen = _readLoopTask is not null;

        _readLoopCts?.Cancel();
        if (_readLoopTask is { } readLoop)
        {
            _readLoopTask = null;

            // PumpAsync catches everything itself; this can only be a pending cancellation.
            try
            {
                await readLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _readLoopCts?.Dispose();
        _readLoopCts = null;

        try
        {
            await _transport.CloseAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            // A transport-internal cancellation (its own pump task, not this call's own token)
            // must not escape CloseAsync's "never throws" contract.
            Debug.WriteLine($"Session: closing the transport failed: {ex}");
        }

        if (wasOpen)
        {
            Notify(o => o.OnClosed(requested, error));
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await _transport.DisposeAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Session: disposing the transport failed: {ex}");
        }
    }

    private sealed class ObserverRegistration : IDisposable
    {
        private readonly Session _session;
        private readonly ISessionObserver _observer;
        private int _disposed;

        public ObserverRegistration(Session session, ISessionObserver observer)
        {
            _session = session;
            _observer = observer;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _session.RemoveObserver(_observer);
            }
        }
    }
}
