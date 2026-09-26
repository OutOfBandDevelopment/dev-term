using System.IO.Pipelines;
using DevTerm.Core.Transports;
using Microsoft.Extensions.Options;

namespace DevTerm.Transports.Ble;

/// <summary>
/// <see cref="ITransport"/> for a BLE peripheral connection. See docs/design/transports.md's "BLE"
/// section. Unlike serial/HID/USBTMC there's no blocking <see cref="Stream"/> to pump on a
/// background thread: <see cref="IBleAdapter"/> already delivers notifications asynchronously via
/// an event, so incoming bytes are written into <see cref="Input"/>'s pipe directly from that event.
/// </summary>
public sealed class BleTransport : ITransport
{
    private readonly IBleAdapterFactory _adapterFactory;
    private readonly IOptions<BleTransportOptions> _options;
    private IBleAdapter? _adapter;
    private ConnectionState _state = ConnectionState.Closed;
    private Pipe? _pipe;

    public BleTransport(IBleAdapterFactory adapterFactory, IOptions<BleTransportOptions> options)
    {
        ArgumentNullException.ThrowIfNull(adapterFactory);
        ArgumentNullException.ThrowIfNull(options);

        _adapterFactory = adapterFactory;
        _options = options;
    }

    public ConnectionState State
    {
        get => _state;
        private set
        {
            if (_state == value)
            {
                return;
            }

            var previous = _state;
            _state = value;
            StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(previous, value));
        }
    }

    public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    public PipeReader Input => _pipe?.Reader ?? throw new InvalidOperationException("The BLE transport has not been opened.");

    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        if (State is ConnectionState.Open or ConnectionState.Opening)
        {
            return;
        }

        State = ConnectionState.Opening;

        var adapter = _adapterFactory.Create(_options.Value);
        adapter.NotificationReceived += OnNotificationReceived;
        adapter.Disconnected += OnAdapterDisconnected;

        try
        {
            await adapter.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            adapter.NotificationReceived -= OnNotificationReceived;
            adapter.Disconnected -= OnAdapterDisconnected;
            await adapter.DisposeAsync().ConfigureAwait(false);
            State = ConnectionState.Faulted;
            throw;
        }

        _adapter = adapter;
        _pipe = new Pipe();
        State = ConnectionState.Open;
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_adapter is null)
        {
            return;
        }

        State = ConnectionState.Closing;

        var adapter = _adapter;
        _adapter = null;
        adapter.NotificationReceived -= OnNotificationReceived;
        adapter.Disconnected -= OnAdapterDisconnected;

        await adapter.DisconnectAsync(cancellationToken).ConfigureAwait(false);
        await adapter.DisposeAsync().ConfigureAwait(false);

        _pipe?.Writer.Complete();
        _pipe = null;

        State = ConnectionState.Closed;
    }

    public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (_adapter is null || State != ConnectionState.Open)
        {
            throw new InvalidOperationException("The BLE transport is not open.");
        }

        return _adapter.WriteAsync(data, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private void OnNotificationReceived(object? sender, ReadOnlyMemory<byte> data)
    {
        var pipe = _pipe;
        if (pipe is null)
        {
            return;
        }

        _ = WriteToPipeAsync(pipe.Writer, data);
    }

    private static async Task WriteToPipeAsync(PipeWriter writer, ReadOnlyMemory<byte> data)
    {
        try
        {
            await writer.WriteAsync(data).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // The pipe was already completed (transport closing/closed) - the notification arrived
            // too late to matter.
        }
    }

    private void OnAdapterDisconnected(object? sender, EventArgs e)
    {
        if (State != ConnectionState.Open)
        {
            return;
        }

        _pipe?.Writer.Complete();
        State = ConnectionState.Closed;
    }
}
