using System.IO.Pipelines;
using DevTerm.Core.Transports;
using DevTerm.Transports.Tcp;
using Microsoft.Extensions.Options;

namespace DevTerm.Transports.Rfc2217;

/// <summary>
/// <see cref="ITransport"/> for RFC 2217 (Telnet COM Port Control) client mode — a remote serial
/// port (e.g. behind <c>ser2net</c>) controlled over a plain TCP socket. Reuses
/// <c>DevTerm.Transports.Tcp</c>'s connect machinery for the socket itself; see
/// docs/design/rfc2217.md for the wire format and <see cref="Rfc2217TelnetReadStream"/> for the
/// Telnet framing layered on top of it.
/// </summary>
public sealed class Rfc2217Transport : ITransport
{
    private readonly ITcpConnectionSource _connectionSource;
    private readonly IOptions<Rfc2217TransportOptions> _options;
    private ITcpConnection? _connection;
    private Rfc2217TelnetReadStream? _telnetStream;
    private ConnectionState _state = ConnectionState.Closed;
    private Pipe? _pipe;
    private CancellationTokenSource? _pumpCts;
    private Task? _pumpTask;

    public Rfc2217Transport(ITcpConnectionSource connectionSource, IOptions<Rfc2217TransportOptions> options)
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

    public PipeReader Input => _pipe?.Reader ?? throw new InvalidOperationException("The RFC 2217 transport has not been opened.");

    /// <summary>
    /// Whether the remote peer accepted COM-PORT-OPTION negotiation. When <c>false</c> (the peer
    /// stayed silent past <see cref="Rfc2217TransportOptions.NegotiationTimeoutMs"/> — some plain
    /// TCP-to-serial bridges never answer it at all), data still flows but baud/parity/DTR/RTS are
    /// whatever the remote port already had configured, not what this session asked for.
    /// </summary>
    public bool ComPortControlNegotiated { get; private set; }

    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        if (State is ConnectionState.Open or ConnectionState.Opening)
        {
            return;
        }

        State = ConnectionState.Opening;
        ComPortControlNegotiated = false;
        var options = _options.Value;

        ITcpConnection connection;
        try
        {
            var tcpOptions = new TcpTransportOptions
            {
                Mode = TcpTransportMode.Client,
                Host = options.Host,
                Port = options.Port,
                WriteTimeoutMs = options.WriteTimeoutMs,
            };
            connection = await _connectionSource.ConnectAsync(tcpOptions, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            State = ConnectionState.Faulted;
            throw;
        }

        _connection = connection;
        var telnetStream = new Rfc2217TelnetReadStream(connection.Stream);
        _telnetStream = telnetStream;

        // Subscribe before the pump ever runs: the pump's first read can already have the peer's
        // reply sitting in the socket buffer (seen in practice when the fake-server test stages the
        // reply ahead of the offer), and decodes it synchronously on the thread pool. Subscribing
        // after starting the pump races that decode — the event can fire into zero subscribers and
        // negotiation falls through to the timeout even though the peer actually answered.
        var negotiationTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnNegotiation(bool accepted) => negotiationTcs.TrySetResult(accepted);
        telnetStream.ComPortOptionNegotiationReceived += OnNegotiation;

        _pipe = new Pipe();
        _pumpCts = new CancellationTokenSource();

        var pipe = _pipe;
        _pumpTask = Task.Run(() => StreamToPipePump.RunAsync(telnetStream, pipe.Writer, _pumpCts.Token), CancellationToken.None);
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

        try
        {
            try
            {
                await NegotiateComPortControlAsync(telnetStream, options, negotiationTcs, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                telnetStream.ComPortOptionNegotiationReceived -= OnNegotiation;
            }
        }
        catch
        {
            State = ConnectionState.Faulted;
            await StopPumpAndDisposeConnectionAsync().ConfigureAwait(false);
            throw;
        }

        State = ConnectionState.Open;
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is null)
        {
            return;
        }

        State = ConnectionState.Closing;
        await StopPumpAndDisposeConnectionAsync().ConfigureAwait(false);
        State = ConnectionState.Closed;
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (_connection is null || _telnetStream is null || State != ConnectionState.Open)
        {
            throw new InvalidOperationException("The RFC 2217 transport is not open.");
        }

        using var timeoutCts = new CancellationTokenSource(_options.Value.WriteTimeoutMs);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await _telnetStream.WriteAsync(data, linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Writing to the RFC 2217 connection timed out after {_options.Value.WriteTimeoutMs} ms.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        GC.SuppressFinalize(this);
    }

    private async Task NegotiateComPortControlAsync(Rfc2217TelnetReadStream telnetStream, Rfc2217TransportOptions options, TaskCompletionSource<bool> negotiationTcs, CancellationToken cancellationToken)
    {
        await telnetStream.SendRawFramedAsync(Telnet.BuildWillDo(Telnet.ComPortOption), cancellationToken).ConfigureAwait(false);

        var delayTask = Task.Delay(options.NegotiationTimeoutMs, cancellationToken);
        var completed = await Task.WhenAny(negotiationTcs.Task, delayTask).ConfigureAwait(false);

        if (completed != negotiationTcs.Task)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Genuine timeout, not caller cancellation: some plain TCP-to-serial bridges never
            // answer COM-PORT-OPTION at all. Tolerant fallback — data still flows.
            ComPortControlNegotiated = false;
            return;
        }

        var accepted = await negotiationTcs.Task.ConfigureAwait(false);
        if (!accepted)
        {
            throw new InvalidOperationException(
                "The remote peer refused RFC 2217 COM-PORT-OPTION negotiation (WONT/DONT). It may not " +
                "actually be a standards-compliant RFC 2217 server — see docs/design/rfc2217.md's " +
                "vendor-specific-variant warning.");
        }

        ComPortControlNegotiated = true;
        await SendInitialComPortConfigurationAsync(telnetStream, options, cancellationToken).ConfigureAwait(false);
    }

    private static async Task SendInitialComPortConfigurationAsync(Rfc2217TelnetReadStream telnetStream, Rfc2217TransportOptions options, CancellationToken cancellationToken)
    {
        // Configure, don't wait for acks — mirrors how SerialTransport configures a local port
        // before first use rather than round-tripping confirmation for every setting.
        await telnetStream.SendRawFramedAsync(Rfc2217Codec.EncodeSetBaudRate(options.BaudRate), cancellationToken).ConfigureAwait(false);
        await telnetStream.SendRawFramedAsync(Rfc2217Codec.EncodeSetDataSize((ComPortDataSize)options.DataBits), cancellationToken).ConfigureAwait(false);
        await telnetStream.SendRawFramedAsync(Rfc2217Codec.EncodeSetParity(ToComPortParity(options.Parity)), cancellationToken).ConfigureAwait(false);
        await telnetStream.SendRawFramedAsync(Rfc2217Codec.EncodeSetStopSize(ToComPortStopSize(options.StopBits)), cancellationToken).ConfigureAwait(false);
        await telnetStream.SendRawFramedAsync(
            Rfc2217Codec.EncodeSetControl(options.DtrEnable ? Rfc2217ControlValue.SetDtrStateOn : Rfc2217ControlValue.SetDtrStateOff),
            cancellationToken).ConfigureAwait(false);
        await telnetStream.SendRawFramedAsync(
            Rfc2217Codec.EncodeSetControl(options.RtsEnable ? Rfc2217ControlValue.SetRtsStateOn : Rfc2217ControlValue.SetRtsStateOff),
            cancellationToken).ConfigureAwait(false);
    }

    private static ComPortParity ToComPortParity(System.IO.Ports.Parity parity) => parity switch
    {
        System.IO.Ports.Parity.None => ComPortParity.None,
        System.IO.Ports.Parity.Odd => ComPortParity.Odd,
        System.IO.Ports.Parity.Even => ComPortParity.Even,
        System.IO.Ports.Parity.Mark => ComPortParity.Mark,
        System.IO.Ports.Parity.Space => ComPortParity.Space,
        _ => ComPortParity.None,
    };

    private static ComPortStopSize ToComPortStopSize(System.IO.Ports.StopBits stopBits) => stopBits switch
    {
        System.IO.Ports.StopBits.One => ComPortStopSize.One,
        System.IO.Ports.StopBits.Two => ComPortStopSize.Two,
        System.IO.Ports.StopBits.OnePointFive => ComPortStopSize.OneAndAHalf,
        _ => ComPortStopSize.One,
    };

    private async Task StopPumpAndDisposeConnectionAsync()
    {
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

            _telnetStream?.Dispose();
            _telnetStream = null;

            _connection?.Dispose();
            _connection = null;
        }
    }
}
