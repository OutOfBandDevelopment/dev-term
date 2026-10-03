using System.IO.Pipelines;
using DevTerm.Core.Transports;
using DevTerm.Transports.Tcp;
using Microsoft.Extensions.Options;

namespace DevTerm.Transports.Vxi11;

/// <summary>
/// <see cref="ITransport"/> for VXI-11 (the ONC-RPC based LXI control protocol): create_link, then
/// device_write for what the session sends and a short-timeout device_read poll for what the
/// instrument answers, destroy_link on close. Reuses <c>DevTerm.Transports.Tcp</c>'s connect
/// machinery for both sockets (the portmapper on TCP 111 and the core channel). Not covered:
/// the abort channel, SRQ/interrupt channel, locking, device_clear and the status byte. See
/// docs/design/vxi11-transport.md.
/// </summary>
public sealed class Vxi11Transport : ITransport
{
    internal const uint PortmapperProgram = 100000;
    internal const uint CoreProgram = 0x0607AF;
    private const int _portmapperPort = 111;
    private const uint _tcpProtocol = 6;
    private const uint _deviceIoTimeoutError = 15;
    private const uint _endFlag = 0x08;
    private const uint _endReason = 0x04;
    private const uint _procCreateLink = 10;
    private const uint _procDeviceWrite = 11;
    private const uint _procDeviceRead = 12;
    private const uint _procDestroyLink = 23;
    private const int _maxRequestSize = 65536;

    private readonly ITcpConnectionSource _connectionSource;
    private readonly IOptions<Vxi11TransportOptions> _options;
    private readonly SemaphoreSlim _channelGate = new(1, 1);
    private ITcpConnection? _connection;
    private OncRpcClient? _rpc;
    private uint _linkId;
    private int _requestSize;
    private ConnectionState _state = ConnectionState.Closed;
    private Pipe? _pipe;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;

    public Vxi11Transport(ITcpConnectionSource connectionSource, IOptions<Vxi11TransportOptions> options)
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

    public PipeReader Input => _pipe?.Reader ?? throw new InvalidOperationException("The VXI-11 transport has not been opened.");

    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        if (State is ConnectionState.Open or ConnectionState.Opening)
        {
            return;
        }

        State = ConnectionState.Opening;
        var options = _options.Value;

        try
        {
            using var timeoutCts = new CancellationTokenSource(options.WriteTimeoutMs);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var port = options.Port > 0 ? options.Port : await LookUpCorePortAsync(options, linked.Token).ConfigureAwait(false);
            _connection = await ConnectAsync(options, port, linked.Token).ConfigureAwait(false);
            _rpc = new OncRpcClient(_connection.Stream);

            var arguments = new XdrWriter()
                .UInt32((uint)Random.Shared.Next(1, int.MaxValue)) // clientId
                .Bool(false) // lockDevice
                .UInt32(0) // lock_timeout
                .String(options.Device)
                .ToArray();
            var reader = new XdrReader(await _rpc.CallAsync(CoreProgram, 1, _procCreateLink, arguments, linked.Token).ConfigureAwait(false));
            var error = reader.UInt32();
            if (error != 0)
            {
                throw new InvalidOperationException($"The instrument refused create_link for device '{options.Device}' (VXI-11 error {error}).");
            }

            _linkId = reader.UInt32();
            _ = reader.UInt32(); // abortPort: the abort channel is not used
            var maxReceive = reader.UInt32();
            _requestSize = maxReceive is > 0 and < _maxRequestSize ? (int)maxReceive : _maxRequestSize;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            State = ConnectionState.Faulted;
            Release();
            throw new TimeoutException($"Opening the VXI-11 link timed out after {options.WriteTimeoutMs} ms.");
        }
        catch
        {
            State = ConnectionState.Faulted;
            Release();
            throw;
        }

        _pipe = new Pipe();
        _readCts = new CancellationTokenSource();
        var pipe = _pipe;
        var token = _readCts.Token;
        _readTask = Task.Run(() => ReadLoopAsync(pipe.Writer, token), CancellationToken.None);
        State = ConnectionState.Open;
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is null)
        {
            return;
        }

        State = ConnectionState.Closing;
        _readCts?.Cancel();
        try
        {
            if (_readTask is not null)
            {
                await _readTask.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // expected: the poll was cancelled by this close
        }

        await TryDestroyLinkAsync().ConfigureAwait(false);
        _readCts?.Dispose();
        _readCts = null;
        _readTask = null;
        _pipe = null;
        Release();
        State = ConnectionState.Closed;
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (_rpc is null || State != ConnectionState.Open)
        {
            throw new InvalidOperationException("The VXI-11 transport is not open.");
        }

        if (data.IsEmpty)
        {
            return;
        }

        var timeoutMs = _options.Value.WriteTimeoutMs;
        using var timeoutCts = new CancellationTokenSource(timeoutMs);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await _channelGate.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                var arguments = new XdrWriter()
                    .UInt32(_linkId)
                    .UInt32((uint)timeoutMs) // io_timeout
                    .UInt32(0) // lock_timeout
                    .UInt32(_endFlag)
                    .Opaque(data.Span)
                    .ToArray();
                var reader = new XdrReader(await _rpc.CallAsync(CoreProgram, 1, _procDeviceWrite, arguments, linked.Token).ConfigureAwait(false));
                var error = reader.UInt32();
                if (error != 0)
                {
                    throw new IOException($"device_write failed (VXI-11 error {error}).");
                }
            }
            finally
            {
                _channelGate.Release();
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Writing to the VXI-11 link timed out after {timeoutMs} ms.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        GC.SuppressFinalize(this);
    }

    private async Task<int> LookUpCorePortAsync(Vxi11TransportOptions options, CancellationToken cancellationToken)
    {
        using var connection = await ConnectAsync(options, _portmapperPort, cancellationToken).ConfigureAwait(false);
        var arguments = new XdrWriter().UInt32(CoreProgram).UInt32(1).UInt32(_tcpProtocol).UInt32(0).ToArray();
        var reply = await new OncRpcClient(connection.Stream).CallAsync(PortmapperProgram, 2, 3, arguments, cancellationToken).ConfigureAwait(false);
        var port = new XdrReader(reply).UInt32();
        return port is > 0 and <= 65535
            ? (int)port
            : throw new InvalidOperationException($"{options.Host} does not offer VXI-11 (its portmapper has no core channel registered).");
    }

    private Task<ITcpConnection> ConnectAsync(Vxi11TransportOptions options, int port, CancellationToken cancellationToken) =>
        _connectionSource.ConnectAsync(
            new TcpTransportOptions { Mode = TcpTransportMode.Client, Host = options.Host, Port = port, WriteTimeoutMs = options.WriteTimeoutMs },
            cancellationToken);

    private async Task ReadLoopAsync(PipeWriter writer, CancellationToken cancellationToken)
    {
        Exception? failure = null;
        try
        {
            var options = _options.Value;
            while (!cancellationToken.IsCancellationRequested)
            {
                byte[] data;
                bool ended;
                await _channelGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var arguments = new XdrWriter()
                        .UInt32(_linkId)
                        .UInt32((uint)_requestSize)
                        .UInt32((uint)options.ReadPollMs) // io_timeout
                        .UInt32(0) // lock_timeout
                        .UInt32(0) // flags: no terminator character
                        .UInt32(0) // termChar
                        .ToArray();
                    var reader = new XdrReader(await _rpc!.CallAsync(CoreProgram, 1, _procDeviceRead, arguments, cancellationToken).ConfigureAwait(false));
                    var error = reader.UInt32();
                    var reason = reader.UInt32();
                    data = reader.Opaque();
                    ended = (reason & _endReason) != 0;
                    if (error != 0 && error != _deviceIoTimeoutError)
                    {
                        throw new IOException($"device_read failed (VXI-11 error {error}).");
                    }
                }
                finally
                {
                    _channelGate.Release();
                }

                if (data.Length > 0)
                {
                    if (ended && options.AppendLineFeedAtEnd && data[^1] != (byte)'\n')
                    {
                        data = [.. data, (byte)'\n'];
                    }

                    var flush = await writer.WriteAsync(data, cancellationToken).ConfigureAwait(false);
                    if (flush.IsCompleted || flush.IsCanceled)
                    {
                        break;
                    }
                }
                else
                {
                    // A write waiting on the gate gets its turn between polls.
                    await Task.Delay(1, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // deliberate close
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            await writer.CompleteAsync(failure).ConfigureAwait(false);
            if (State == ConnectionState.Open)
            {
                State = ConnectionState.Closed;
            }
        }
    }

    private async Task TryDestroyLinkAsync()
    {
        if (_rpc is null || _linkId == 0)
        {
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(1000);
            await _rpc.CallAsync(CoreProgram, 1, _procDestroyLink, new XdrWriter().UInt32(_linkId).ToArray(), cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or InvalidOperationException or InvalidDataException or ObjectDisposedException)
        {
            // best effort: the instrument drops the link when the socket closes anyway
        }

        _linkId = 0;
    }

    private void Release()
    {
        _connection?.Dispose();
        _connection = null;
        _rpc = null;
    }
}
