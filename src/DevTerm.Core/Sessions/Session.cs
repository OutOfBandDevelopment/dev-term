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
            await _transport.OpenAsync(cancellationToken).ConfigureAwait(false);

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
            await StopAsync(cancellationToken).ConfigureAwait(false);
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

            await StopAsync(CancellationToken.None).ConfigureAwait(false);
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

    // Caller holds _lifecycleLock.
    private async Task StopAsync(CancellationToken cancellationToken)
    {
        _generation++;

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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Debug.WriteLine($"Session: closing the transport failed: {ex}");
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
}
