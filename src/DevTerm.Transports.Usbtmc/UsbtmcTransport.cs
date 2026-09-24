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
/// tracking a remaining-byte count against the first transfer's declared TransferSize, never by
/// re-decoding a header on a continuation transfer - see
/// docs/design/features/usbtmc-bulk-in-reassembly-fix.md) into <see cref="Input"/>. This heuristic
/// is verified against real Rigol DM3058E/DS1102E/DG1022 hardware; the DG1022's own bulk-IN stall
/// in that verification is a device/USB-level issue this transport surfaces as a clean exception
/// rather than resolves - see BACKLOG.md's USBTMC entry.
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

    // A SCPI-style query's mnemonic ends with '?', but a query can still take a
    // space-separated parameter after it (e.g. ":MEAS:VPP? CHAN1") - checking only the very
    // last character misses every one of those. Confirmed against a real Rigol DS1102E: with
    // the last-character-only check, ":MEAS:VPP? CHAN1" was sent as a fire-and-forget write with
    // no REQUEST_DEV_DEP_MSG_IN/read ever issued, silently dropping the reply (and leaving it
    // unread in the device, which then risked misaligning the next command's read). Any trailing
    // line-ending bytes a presenter/CLI added to the typed line are trimmed off first.
    private static bool IsQuery(ReadOnlySpan<byte> data)
    {
        var trimmed = data;
        while (trimmed.Length > 0 && (trimmed[^1] == (byte)'\r' || trimmed[^1] == (byte)'\n'))
        {
            trimmed = trimmed[..^1];
        }

        var spaceIndex = trimmed.IndexOf((byte)' ');
        var mnemonic = spaceIndex >= 0 ? trimmed[..spaceIndex] : trimmed;

        return mnemonic.Length > 0 && mnemonic[^1] == (byte)'?';
    }

    // Per USBTMC 1.0, one logical DEV_DEP_MSG_IN response can span multiple physical bulk-IN
    // transfers. Only the FIRST physical transfer carries the 12-byte header - every subsequent
    // transfer for the same logical response is raw continuation payload with no header at all
    // (mirrors libsigrok's scpi_usbtmc_libusb.c, which decodes the header exactly once and then
    // tracks a remaining-byte count). Re-decoding a continuation transfer's payload bytes as a
    // header produces a bogus TransferSize and hangs forever waiting for bytes that will never
    // arrive - see docs/design/proposals/usbtmc-lockup-fix-prompt.md.
    private List<byte[]> ReadReply(IUsbtmcDevice device)
    {
        var requestTag = SendRequestDevDepMsgIn(device);

        var chunks = new List<byte[]>();
        var readBuffer = new byte[UsbtmcCodec.HeaderSize + device.MaxTransferSize];

        var count = device.ReadBulkIn(readBuffer, out var stalled);
        if (count <= 0)
        {
            // Some Rigol firmware answers a REQUEST_DEV_DEP_MSG_IN with an empty transfer before
            // the real one (see libsigrok's scpi_usbtmc_libusb.c, which retries for exactly this
            // reason against a Rigol DS1054Z), and a reported stall can mean the device discarded
            // the pending request rather than just being slow to answer it - re-send the request
            // first in that case, then give the read one more chance either way before giving up.
            if (stalled)
            {
                requestTag = SendRequestDevDepMsgIn(device);
            }

            count = device.ReadBulkIn(readBuffer, out _);
            if (count <= 0)
            {
                throw new IOException("USBTMC device returned no data for the query.");
            }
        }

        var header = UsbtmcCodec.DecodeHeader(readBuffer.AsSpan(0, count), requestTag);
        if (header.TransferSize > _options.Value.MaxResponseSize)
        {
            throw new IOException(
                $"USBTMC device declared a TransferSize of {header.TransferSize} byte(s), exceeding the configured MaxResponseSize of {_options.Value.MaxResponseSize} byte(s).");
        }

        if (header.TransferSize == 0)
        {
            // Confirmed against a real Rigol DS1102E: a query sent immediately after OpenAsync
            // sometimes gets back a completely well-formed, EOM-terminated, zero-byte logical
            // message - not a physical zero-byte transfer (that case is already handled above by
            // the stalled/count<=0 retry) but a valid header declaring TransferSize=0. This is the
            // same "phantom empty reply before the real one" firmware behavior the comment above
            // already retries for, just manifesting as a complete empty message instead of a
            // failed read. Re-issue the request once and take whatever comes back, real or empty -
            // a second empty reply in a row is treated as a legitimately empty response rather than
            // retried forever.
            requestTag = SendRequestDevDepMsgIn(device);
            count = device.ReadBulkIn(readBuffer, out _);
            if (count <= 0)
            {
                throw new IOException("USBTMC device returned no data for the query.");
            }

            header = UsbtmcCodec.DecodeHeader(readBuffer.AsSpan(0, count), requestTag);
            if (header.TransferSize > _options.Value.MaxResponseSize)
            {
                throw new IOException(
                    $"USBTMC device declared a TransferSize of {header.TransferSize} byte(s), exceeding the configured MaxResponseSize of {_options.Value.MaxResponseSize} byte(s).");
            }
        }

        var firstPayload = UsbtmcCodec.ExtractPayload(readBuffer.AsSpan(0, count), header);
        if (firstPayload.Length > 0)
        {
            chunks.Add(firstPayload.ToArray());
        }

        var remaining = header.TransferSize - firstPayload.Length;

        while (remaining > 0)
        {
            count = device.ReadBulkIn(readBuffer, out _);
            if (count <= 0)
            {
                throw new IOException("USBTMC continuation read returned no data before TransferSize was fully received.");
            }

            var take = Math.Min(count, remaining);
            chunks.Add(readBuffer.AsSpan(0, take).ToArray());
            remaining -= take;
        }

        return chunks;
    }

    private byte SendRequestDevDepMsgIn(IUsbtmcDevice device)
    {
        _bulkOutTag = UsbtmcCodec.NextTag(_bulkOutTag);
        var requestFrame = UsbtmcCodec.EncodeRequestDevDepMsgIn(_bulkOutTag, int.MaxValue, termChar: 0, termCharEnabled: false);
        device.WriteBulkOut(requestFrame);
        return _bulkOutTag;
    }
}
