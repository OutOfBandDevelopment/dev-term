using DevTerm.Core.Presenters;
using DevTerm.Core.Transports;

namespace DevTerm.Core.Sessions;

/// <summary>
/// Binds one <see cref="ITransport"/> to a <see cref="Pipeline"/> of presenters for a single
/// logical connection to a device. See docs/design/architecture.md.
/// </summary>
public sealed class Session : IAsyncDisposable
{
    private readonly ITransport _transport;
    private readonly Pipeline _pipeline;
    private CancellationTokenSource? _readLoopCts;
    private Task? _readLoopTask;

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

    public event EventHandler<PresenterOutput>? Output;

    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        await _transport.OpenAsync(cancellationToken).ConfigureAwait(false);

        // A fresh CancellationTokenSource each time, not one reused for the Session's whole
        // lifetime: a CTS can only ever be cancelled once, so re-opening after a Close (a real
        // scenario now that front ends have a Connect/Disconnect menu item) would otherwise start
        // the new read loop with an already-cancelled token, ending it immediately.
        _readLoopCts = new CancellationTokenSource();
        _readLoopTask = Task.Run(() => PumpAsync(_readLoopCts.Token));
    }

    public Task CloseAsync(CancellationToken cancellationToken = default) => StopAsync(cancellationToken);

    public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) =>
        _transport.WriteAsync(data, cancellationToken);

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        var reader = _transport.Input;
        try
        {
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

                if (result.IsCompleted || result.IsCanceled)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when CloseAsync/DisposeAsync cancels the read loop.
        }
    }

    private async Task StopAsync(CancellationToken cancellationToken)
    {
        _readLoopCts?.Cancel();
        if (_readLoopTask is not null)
        {
            await _readLoopTask.ConfigureAwait(false);
            _readLoopTask = null;
        }

        _readLoopCts?.Dispose();
        _readLoopCts = null;

        await _transport.CloseAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        await _transport.DisposeAsync();
    }
}
