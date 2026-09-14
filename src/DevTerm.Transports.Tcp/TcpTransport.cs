using DevTerm.Core.Transports;
using Microsoft.Extensions.Options;

namespace DevTerm.Transports.Tcp;

/// <summary>
/// <see cref="ITransport"/> for a TCP connection, in either Client or Listener mode
/// (a configuration choice, not two plugins). See docs/design/transports.md.
/// </summary>
public sealed class TcpTransport : ITransport
{
    private readonly ITcpConnectionSource _connectionSource;
    private readonly IOptions<TcpTransportOptions> _options;
    private ITcpConnection? _connection;
    private ConnectionState _state = ConnectionState.Closed;

    public TcpTransport(ITcpConnectionSource connectionSource, IOptions<TcpTransportOptions> options)
    {
        ArgumentNullException.ThrowIfNull(connectionSource);
        ArgumentNullException.ThrowIfNull(options);

        _connectionSource = connectionSource;
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

    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        if (State is ConnectionState.Open or ConnectionState.Opening)
        {
            return;
        }

        State = ConnectionState.Opening;
        var options = _options.Value;

        ITcpConnection connection;
        try
        {
            connection = options.Mode == TcpTransportMode.Client
                ? await _connectionSource.ConnectAsync(options, cancellationToken).ConfigureAwait(false)
                : await _connectionSource.AcceptAsync(options, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            State = ConnectionState.Faulted;
            throw;
        }

        connection.DataReceived += OnConnectionDataReceived;
        connection.Closed += OnConnectionClosed;
        _connection = connection;
        State = ConnectionState.Open;
    }

    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is null)
        {
            return Task.CompletedTask;
        }

        State = ConnectionState.Closing;

        _connection.DataReceived -= OnConnectionDataReceived;
        _connection.Closed -= OnConnectionClosed;
        _connection.Dispose();
        _connection = null;

        State = ConnectionState.Closed;
        return Task.CompletedTask;
    }

    public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (_connection is null || State != ConnectionState.Open)
        {
            throw new InvalidOperationException("The TCP transport is not open.");
        }

        var buffer = data.ToArray();
        _connection.Write(buffer, 0, buffer.Length);
        return Task.CompletedTask;
    }

    private void OnConnectionDataReceived(object? sender, TcpDataReceivedEventArgs e) =>
        DataReceived?.Invoke(this, new TransportDataReceivedEventArgs(e.Data));

    private void OnConnectionClosed(object? sender, EventArgs e) => State = ConnectionState.Closed;

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        GC.SuppressFinalize(this);
    }
}
