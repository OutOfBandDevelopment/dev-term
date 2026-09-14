using System.IO.Pipelines;
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
    private Pipe? _pipe;
    private CancellationTokenSource? _pumpCts;
    private Task? _pumpTask;

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

    public PipeReader Input => _pipe?.Reader ?? throw new InvalidOperationException("The TCP transport has not been opened.");

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

        _connection = connection;
        _pipe = new Pipe();
        _pumpCts = new CancellationTokenSource();

        var pipe = _pipe;
        _pumpTask = Task.Run(() => StreamToPipePump.RunAsync(connection.Stream, pipe.Writer, _pumpCts.Token), CancellationToken.None);
        _ = _pumpTask.ContinueWith(
            _ =>
            {
                // The pump ends either because we're deliberately closing (State already moved
                // past Open by then) or because the remote peer disconnected underneath us.
                if (State == ConnectionState.Open)
                {
                    State = ConnectionState.Closed;
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        State = ConnectionState.Open;
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is null)
        {
            return;
        }

        State = ConnectionState.Closing;

        _pumpCts?.Cancel();
        if (_pumpTask is not null)
        {
            await _pumpTask.ConfigureAwait(false);
        }

        _pumpCts?.Dispose();
        _pumpCts = null;
        _pumpTask = null;
        _pipe = null;

        _connection.Dispose();
        _connection = null;

        State = ConnectionState.Closed;
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

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        GC.SuppressFinalize(this);
    }
}
