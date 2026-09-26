using System.IO.Pipelines;
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
    private Pipe? _pipe;
    private CancellationTokenSource? _pumpCts;
    private Task? _pumpTask;

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

    public PipeReader Input => _pipe?.Reader ?? throw new InvalidOperationException("The serial transport has not been opened.");

    public Task OpenAsync(CancellationToken cancellationToken = default)
    {
        if (State is ConnectionState.Open or ConnectionState.Opening)
        {
            return Task.CompletedTask;
        }

        State = ConnectionState.Opening;

        var port = _portFactory.Create(_options.Value);

        try
        {
            port.Open();
        }
        catch
        {
            port.Dispose();
            State = ConnectionState.Faulted;
            throw;
        }

        _port = port;
        _pipe = new Pipe();
        _pumpCts = new CancellationTokenSource();

        var pipe = _pipe;
        _pumpTask = Task.Run(() => StreamToPipePump.RunAsync(port.BaseStream, pipe.Writer, _pumpCts.Token), CancellationToken.None);
        _ = _pumpTask.ContinueWith(
            _ =>
            {
                // The pump ends either because we're deliberately closing (State already moved
                // past Open by then) or because the port faulted/disconnected underneath us.
                if (State == ConnectionState.Open)
                {
                    State = ConnectionState.Closed;
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        State = ConnectionState.Open;
        return Task.CompletedTask;
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_port is null)
        {
            return;
        }

        State = ConnectionState.Closing;

        _pumpCts?.Cancel();
        try
        {
            if (_pumpTask is not null)
            {
                await _pumpTask.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected: cancelling the pump while it's blocked flushing into a paused pipe can
            // surface as the pump task itself completing Canceled rather than completing normally.
        }
        finally
        {
            _pumpCts?.Dispose();
            _pumpCts = null;
            _pumpTask = null;
            _pipe = null;

            _port.Close();
            _port.Dispose();
            _port = null;

            State = ConnectionState.Closed;
        }
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

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        GC.SuppressFinalize(this);
    }
}
