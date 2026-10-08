using System.Buffers;
using System.Buffers.Binary;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;

namespace DevTerm.Transports.Rfc2217;

/// <summary>The serial settings a remote RFC 2217 client has asked for.</summary>
public sealed record Rfc2217PortSettings(int BaudRate = 9600, int DataBits = 8, Parity Parity = Parity.None, StopBits StopBits = StopBits.One, bool Dtr = true, bool Rts = true);

/// <summary>
/// Shares one live session on a TCP port as an RFC 2217 (Telnet COM-PORT-OPTION) server, so a client such as pyserial's
/// <c>rfc2217://host:port</c> can connect to it like a remote serial port. Data is Telnet-escaped both ways; the
/// client's SET-BAUDRATE/DATASIZE/PARITY/STOPSIZE/CONTROL requests are acknowledged and recorded in
/// <see cref="Settings"/> (raising <see cref="SettingsChanged"/>), but they are NOT applied to the underlying device:
/// the session's transport has no line-control hook yet, so the device keeps the settings dev-term opened it with.
/// One client at a time (a second connection is closed at once); loopback unless told otherwise, with no authentication
/// or encryption. Register with <see cref="Session.AddObserver"/>. See docs/design/rfc2217.md ("Server mode").
/// </summary>
public sealed class Rfc2217ServerBridge : ISessionObserver, IAsyncDisposable
{
    private readonly Session _session;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _gate = new();
    private readonly Task _acceptLoop;
    private Channel<byte[]>? _client;
    private Rfc2217PortSettings _settings = new();

    public Rfc2217ServerBridge(Session session, IPAddress bindAddress, int port)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(bindAddress);
        _session = session;
        _listener = new TcpListener(bindAddress, port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = Task.Run(AcceptAsync);
    }

    /// <summary>Raised (on the connection's thread) whenever the client changes a serial setting.</summary>
    public event Action<Rfc2217PortSettings>? SettingsChanged;

    public int Port { get; }

    /// <summary>The settings the client last requested (defaults until it asks).</summary>
    public Rfc2217PortSettings Settings
    {
        get
        {
            lock (_gate)
            {
                return _settings;
            }
        }
    }

    public void OnOpened()
    {
    }

    public void OnReceived(ReadOnlySequence<byte> data)
    {
        var escaped = Rfc2217Codec.Escape(data.ToArray());
        lock (_gate)
        {
            _client?.Writer.TryWrite(escaped);
        }
    }

    public void OnSent(ReadOnlyMemory<byte> data)
    {
    }

    public void OnClosed(bool requested, Exception? error)
    {
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        try
        {
            await _acceptLoop.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
        {
        }

        _stop.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }

            Channel<byte[]>? queue = null;
            lock (_gate)
            {
                if (_client is null)
                {
                    queue = _client = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
                }
            }

            if (queue is null)
            {
                client.Dispose();
                continue;
            }

            _ = Task.Run(() => ServeAsync(client, queue));
        }
    }

    private async Task ServeAsync(TcpClient client, Channel<byte[]> queue)
    {
        using (client)
        {
            var stream = client.GetStream();
            using var writeLock = new SemaphoreSlim(1, 1);

            async Task WriteAsync(byte[] bytes)
            {
                await writeLock.WaitAsync(_stop.Token).ConfigureAwait(false);
                try
                {
                    await stream.WriteAsync(bytes, _stop.Token).ConfigureAwait(false);
                }
                finally
                {
                    writeLock.Release();
                }
            }

            var writer = Task.Run(async () =>
            {
                try
                {
                    await foreach (var chunk in queue.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false))
                    {
                        await WriteAsync(chunk).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
                {
                }
            });

            try
            {
                await ReadLoopAsync(stream, WriteAsync).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
            {
            }
            finally
            {
                lock (_gate)
                {
                    _client = null;
                }

                queue.Writer.TryComplete();
                client.Dispose();
                await writer.ConfigureAwait(false);
            }
        }
    }

    private async Task ReadLoopAsync(NetworkStream stream, Func<byte[], Task> write)
    {
        var buffer = new byte[4096];
        var data = new List<byte>();
        var sub = new List<byte>();
        var offered = new HashSet<byte>();
        var accepted = new HashSet<byte>();
        var state = 0; // 0 data, 1 IAC, 2 option command, 3 SB option, 4 in SB, 5 in SB after IAC
        byte command = 0;
        byte subOption = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, _stop.Token).ConfigureAwait(false)) > 0)
        {
            data.Clear();
            var replies = new List<byte[]>();
            for (var i = 0; i < read; i++)
            {
                var b = buffer[i];
                switch (state)
                {
                    case 0:
                        if (b == Telnet.Iac)
                        {
                            state = 1;
                        }
                        else
                        {
                            data.Add(b);
                        }

                        break;
                    case 1:
                        if (b == Telnet.Iac)
                        {
                            data.Add(b);
                            state = 0;
                        }
                        else if (b is Telnet.Will or Telnet.Wont or Telnet.Do or Telnet.Dont)
                        {
                            command = b;
                            state = 2;
                        }
                        else if (b == Telnet.Sb)
                        {
                            state = 3;
                        }
                        else
                        {
                            state = 0;
                        }

                        break;
                    case 2:
                        HandleOption(command, b, offered, accepted, replies);
                        state = 0;
                        break;
                    case 3:
                        subOption = b;
                        sub.Clear();
                        state = 4;
                        break;
                    case 4:
                        if (b == Telnet.Iac)
                        {
                            state = 5;
                        }
                        else
                        {
                            sub.Add(b);
                        }

                        break;
                    default:
                        if (b == Telnet.Se)
                        {
                            if (subOption == Telnet.ComPortOption)
                            {
                                HandleComPort([.. sub], replies);
                            }

                            state = 0;
                        }
                        else
                        {
                            if (b == Telnet.Iac)
                            {
                                sub.Add(b);
                            }

                            state = 4;
                        }

                        break;
                }
            }

            foreach (var reply in replies)
            {
                await write(reply).ConfigureAwait(false);
            }

            if (data.Count > 0)
            {
                await _session.SendAsync(data.ToArray(), _stop.Token).ConfigureAwait(false);
            }
        }
    }

    // Accept BINARY (0), SGA (3) and COM-PORT (44) in both directions, once each; refuse everything else.
    private static void HandleOption(byte command, byte option, HashSet<byte> offered, HashSet<byte> accepted, List<byte[]> replies)
    {
        var supported = option is 0 or 3 or Telnet.ComPortOption;
        switch (command)
        {
            case Telnet.Will:
                if (!supported)
                {
                    replies.Add([Telnet.Iac, Telnet.Dont, option]);
                }
                else if (accepted.Add(option))
                {
                    replies.Add([Telnet.Iac, Telnet.Do, option]);
                }

                break;
            case Telnet.Do:
                if (!supported)
                {
                    replies.Add([Telnet.Iac, Telnet.Wont, option]);
                }
                else if (offered.Add(option))
                {
                    replies.Add([Telnet.Iac, Telnet.Will, option]);
                }

                break;
        }
    }

    // The session's transport takes the client's line settings live when it fronts a serial line.
    private void ApplyToTransport(Rfc2217PortSettings settings)
    {
        if (_session.Transport is not IComPortControl control)
        {
            return;
        }

        try
        {
            control.SetBaudRate(settings.BaudRate)
                .SetDataBits(settings.DataBits)
                .SetParity(settings.Parity switch { Parity.Odd => ComParity.Odd, Parity.Even => ComParity.Even, Parity.Mark => ComParity.Mark, Parity.Space => ComParity.Space, _ => ComParity.None })
                .SetStopBits(settings.StopBits switch { StopBits.Two => ComStopBits.Two, StopBits.OnePointFive => ComStopBits.OnePointFive, _ => ComStopBits.One })
                .SetDtr(settings.Dtr)
                .SetRts(settings.Rts);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException)
        {
            // A setting the device rejects (an unsupported baud rate) is not the client's connection failing.
        }
    }

    private static byte[] Reply(byte command, params byte[] payload) =>
        Rfc2217Codec.EncodeSubnegotiation((byte)(command + Rfc2217Command.ServerOffset), payload);

    private void HandleComPort(byte[] payload, List<byte[]> replies)
    {
        var message = Rfc2217Codec.DecodeComPortMessage(payload);
        if (message is null || message.IsFromServer)
        {
            return;
        }

        var changed = false;
        lock (_gate)
        {
            switch (message)
            {
                case Rfc2217SignatureMessage:
                    replies.Add(Reply(Rfc2217Command.Signature, Encoding.ASCII.GetBytes("dev-term")));
                    break;
                case Rfc2217BaudRateMessage baud:
                    if (baud.BaudRate > 0)
                    {
                        _settings = _settings with { BaudRate = baud.BaudRate };
                        changed = true;
                    }

                    var value = new byte[4];
                    BinaryPrimitives.WriteInt32BigEndian(value, _settings.BaudRate);
                    replies.Add(Reply(Rfc2217Command.SetBaudRate, value));
                    break;
                case Rfc2217DataSizeMessage size:
                    if (size.DataSize != ComPortDataSize.Request)
                    {
                        _settings = _settings with { DataBits = (int)size.DataSize };
                        changed = true;
                    }

                    replies.Add(Reply(Rfc2217Command.SetDataSize, (byte)_settings.DataBits));
                    break;
                case Rfc2217ParityMessage parity:
                    if (parity.Parity != ComPortParity.Request)
                    {
                        _settings = _settings with { Parity = parity.Parity switch { ComPortParity.Odd => Parity.Odd, ComPortParity.Even => Parity.Even, ComPortParity.Mark => Parity.Mark, ComPortParity.Space => Parity.Space, _ => Parity.None } };
                        changed = true;
                    }

                    replies.Add(Reply(Rfc2217Command.SetParity, (byte)(_settings.Parity switch { Parity.Odd => 2, Parity.Even => 3, Parity.Mark => 4, Parity.Space => 5, _ => 1 })));
                    break;
                case Rfc2217StopSizeMessage stop:
                    if (stop.StopSize != ComPortStopSize.Request)
                    {
                        _settings = _settings with { StopBits = stop.StopSize switch { ComPortStopSize.Two => StopBits.Two, ComPortStopSize.OneAndAHalf => StopBits.OnePointFive, _ => StopBits.One } };
                        changed = true;
                    }

                    replies.Add(Reply(Rfc2217Command.SetStopSize, (byte)(_settings.StopBits switch { StopBits.Two => 2, StopBits.OnePointFive => 3, _ => 1 })));
                    break;
                case Rfc2217ControlMessage control:
                    var answer = control.Value;
                    switch (control.Value)
                    {
                        case Rfc2217ControlValue.SetDtrStateOn:
                        case Rfc2217ControlValue.SetDtrStateOff:
                            _settings = _settings with { Dtr = control.Value == Rfc2217ControlValue.SetDtrStateOn };
                            changed = true;
                            break;
                        case Rfc2217ControlValue.SetRtsStateOn:
                        case Rfc2217ControlValue.SetRtsStateOff:
                            _settings = _settings with { Rts = control.Value == Rfc2217ControlValue.SetRtsStateOn };
                            changed = true;
                            break;
                        case Rfc2217ControlValue.RequestDtrState:
                            answer = _settings.Dtr ? Rfc2217ControlValue.SetDtrStateOn : Rfc2217ControlValue.SetDtrStateOff;
                            break;
                        case Rfc2217ControlValue.RequestRtsState:
                            answer = _settings.Rts ? Rfc2217ControlValue.SetRtsStateOn : Rfc2217ControlValue.SetRtsStateOff;
                            break;
                        case Rfc2217ControlValue.RequestOutboundFlowControl:
                        case Rfc2217ControlValue.RequestInboundFlowControl:
                            answer = Rfc2217ControlValue.UseNoFlowControl;
                            break;
                        case Rfc2217ControlValue.RequestBreakState:
                            answer = Rfc2217ControlValue.SetBreakStateOff;
                            break;
                    }

                    replies.Add(Reply(Rfc2217Command.SetControl, (byte)answer));
                    break;
                case Rfc2217LineStateMaskMessage lineMask:
                    replies.Add(Reply(Rfc2217Command.SetLineStateMask, (byte)lineMask.Mask));
                    break;
                case Rfc2217ModemStateMaskMessage modemMask:
                    replies.Add(Reply(Rfc2217Command.SetModemStateMask, (byte)modemMask.Mask));
                    break;
                case Rfc2217PurgeDataMessage purge:
                    replies.Add(Reply(Rfc2217Command.PurgeData, (byte)purge.Target));
                    break;
            }
        }

        if (changed)
        {
            ApplyToTransport(Settings);
            SettingsChanged?.Invoke(Settings);
        }
    }
}
