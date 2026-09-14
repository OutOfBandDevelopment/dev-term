using DevTerm.Core.Transports;
using Microsoft.Extensions.Options;

namespace DevTerm.Transports.Serial;

/// <summary>
/// <see cref="ITransport"/> for a serial/UART connection. See docs/design/transports.md.
/// </summary>
public sealed class SerialTransport : ITransport
{
    private readonly ISerialPortFactory _portFactory;
    private readonly IOptions<SerialTransportOptions> _options;
    private ISerialPort? _port;
    private ConnectionState _state = ConnectionState.Closed;

    public SerialTransport(ISerialPortFactory portFactory, IOptions<SerialTransportOptions> options)
    {
        ArgumentNullException.ThrowIfNull(portFactory);
        ArgumentNullException.ThrowIfNull(options);

        _portFactory = portFactory;
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

    public event EventHandler<TransportDataReceivedEventArgs>? DataReceived;

    public Task OpenAsync(CancellationToken cancellationToken = default)
    {
        if (State is ConnectionState.Open or ConnectionState.Opening)
        {
            return Task.CompletedTask;
        }

        State = ConnectionState.Opening;

        var port = _portFactory.Create(_options.Value);
        port.DataReceived += OnPortDataReceived;

        try
        {
            port.Open();
        }
        catch
        {
            port.DataReceived -= OnPortDataReceived;
            port.Dispose();
            State = ConnectionState.Faulted;
            throw;
        }

        _port = port;
        State = ConnectionState.Open;
        return Task.CompletedTask;
    }

    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_port is null)
        {
            return Task.CompletedTask;
        }

        State = ConnectionState.Closing;

        _port.DataReceived -= OnPortDataReceived;
        _port.Close();
        _port.Dispose();
        _port = null;

        State = ConnectionState.Closed;
        return Task.CompletedTask;
    }

    public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (_port is null || State != ConnectionState.Open)
        {
            throw new InvalidOperationException("The serial transport is not open.");
        }

        var buffer = data.ToArray();
        _port.Write(buffer, 0, buffer.Length);
        return Task.CompletedTask;
    }

    private void OnPortDataReceived(object? sender, SerialPortDataReceivedEventArgs e) =>
        DataReceived?.Invoke(this, new TransportDataReceivedEventArgs(e.Data));

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        GC.SuppressFinalize(this);
    }
}
