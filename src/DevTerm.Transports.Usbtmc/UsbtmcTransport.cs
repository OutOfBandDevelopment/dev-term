using System.IO.Pipelines;
using DevTerm.Core.Transports;
using Microsoft.Extensions.Options;

namespace DevTerm.Transports.Usbtmc;

/// <summary>
/// <see cref="ITransport"/> for a USBTMC device connection. See docs/design/usbtmc-transport.md.
///
/// Unlike serial/TCP/HID, USBTMC has no unsolicited "data arrived" event to pump continuously -
/// a device only replies to an explicit read request, itself sent only after a command that
/// expects one. This transport heuristically treats an outgoing write that ends with '?' (after
/// trimming any trailing line ending) as a SCPI-style query: it writes the command, then issues a
/// REQUEST_DEV_DEP_MSG_IN and pumps bulk-IN transfers (reassembling across multiple transfers by
/// the EOM bit, per docs/design/usbtmc-transport.md's <c>ITransport</c> mapping) into <see cref="Input"/>.
/// This heuristic is not yet verified against real hardware - see that doc's "Open questions".
/// </summary>
public sealed class UsbtmcTransport : ITransport
{
    private readonly IUsbtmcDeviceFactory _deviceFactory;
    private readonly IOptions<UsbtmcTransportOptions> _options;
    private IUsbtmcDevice? _device;
    private ConnectionState _state = ConnectionState.Closed;
    private Pipe? _pipe;
    private byte _bulkOutTag;

    public UsbtmcTransport(IUsbtmcDeviceFactory deviceFactory, IOptions<UsbtmcTransportOptions> options)
    {
        ArgumentNullException.ThrowIfNull(deviceFactory);
        ArgumentNullException.ThrowIfNull(options);

        _deviceFactory = deviceFactory;
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

    public PipeReader Input => _pipe?.Reader ?? throw new InvalidOperationException("The USBTMC transport has not been opened.");

    public Task OpenAsync(CancellationToken cancellationToken = default)
    {
        if (State is ConnectionState.Open or ConnectionState.Opening)
        {
            return Task.CompletedTask;
        }

        State = ConnectionState.Opening;

        var device = _deviceFactory.Create(_options.Value);

        try
        {
            device.Open();
            device.SetRemote(true);
        }
        catch
        {
            device.Dispose();
            State = ConnectionState.Faulted;
            throw;
        }

        _device = device;
        _pipe = new Pipe();
        _bulkOutTag = 0;

        State = ConnectionState.Open;
        return Task.CompletedTask;
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_device is null)
        {
            return;
        }

        State = ConnectionState.Closing;

        var device = _device;
        _device = null;
        _pipe = null;

        await Task.Run(
            () =>
            {
                try { device.SetRemote(false); } catch { }
                device.Close();
                device.Dispose();
            },
            CancellationToken.None).ConfigureAwait(false);

        State = ConnectionState.Closed;
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (_device is null || _pipe is null || State != ConnectionState.Open)
        {
            throw new InvalidOperationException("The USBTMC transport is not open.");
        }

        var device = _device;
        var writer = _pipe.Writer;
        var isQuery = IsQuery(data.Span);
        _bulkOutTag = UsbtmcCodec.NextTag(_bulkOutTag);
        var commandFrame = UsbtmcCodec.EncodeDevDepMsgOut(_bulkOutTag, data.Span, eom: true);

        var replyChunks = await Task.Run(
            () =>
            {
                device.WriteBulkOut(commandFrame);
                return isQuery ? ReadReply(device) : null;
            },
            cancellationToken).ConfigureAwait(false);

        if (replyChunks is null)
        {
            return;
        }

        foreach (var chunk in replyChunks)
        {
            chunk.CopyTo(writer.GetSpan(chunk.Length));
            writer.Advance(chunk.Length);
        }

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        GC.SuppressFinalize(this);
    }

    // A SCPI-style query ends with '?', once any trailing line-ending bytes a presenter/CLI added
    // to the typed line are trimmed off.
    private static bool IsQuery(ReadOnlySpan<byte> data)
    {
        var trimmed = data;
        while (trimmed.Length > 0 && (trimmed[^1] == (byte)'\r' || trimmed[^1] == (byte)'\n'))
        {
            trimmed = trimmed[..^1];
        }

        return trimmed.Length > 0 && trimmed[^1] == (byte)'?';
    }

    private List<byte[]> ReadReply(IUsbtmcDevice device)
    {
        _bulkOutTag = UsbtmcCodec.NextTag(_bulkOutTag);
        var requestFrame = UsbtmcCodec.EncodeRequestDevDepMsgIn(_bulkOutTag, device.MaxTransferSize, termChar: 0, termCharEnabled: false);
        device.WriteBulkOut(requestFrame);

        var chunks = new List<byte[]>();
        var readBuffer = new byte[UsbtmcCodec.HeaderSize + device.MaxTransferSize];
        bool eom;
        do
        {
            var count = device.ReadBulkIn(readBuffer);
            if (count <= 0)
            {
                break;
            }

            var header = UsbtmcCodec.DecodeHeader(readBuffer.AsSpan(0, count));
            var payload = UsbtmcCodec.ExtractPayload(readBuffer.AsSpan(0, count), header);
            if (payload.Length > 0)
            {
                chunks.Add(payload.ToArray());
            }

            eom = header.Eom;
        }
        while (!eom);

        return chunks;
    }
}
