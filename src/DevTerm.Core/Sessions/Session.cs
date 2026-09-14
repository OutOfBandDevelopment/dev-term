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

    public Session(ITransport transport, Pipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(pipeline);

        _transport = transport;
        _pipeline = pipeline;
        _transport.DataReceived += OnTransportDataReceived;
    }

    public ConnectionState State => _transport.State;

    public IReadOnlyList<IPresenter> Presenters => _pipeline.Presenters;

    public event EventHandler<PresenterOutput>? Output;

    public Task OpenAsync(CancellationToken cancellationToken = default) =>
        _transport.OpenAsync(cancellationToken);

    public Task CloseAsync(CancellationToken cancellationToken = default) =>
        _transport.CloseAsync(cancellationToken);

    public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) =>
        _transport.WriteAsync(data, cancellationToken);

    private void OnTransportDataReceived(object? sender, TransportDataReceivedEventArgs e)
    {
        foreach (var output in _pipeline.Render(e.Data))
        {
            Output?.Invoke(this, output);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _transport.DataReceived -= OnTransportDataReceived;
        await _transport.DisposeAsync();
    }
}
