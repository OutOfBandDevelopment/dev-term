using System.Diagnostics;
using System.IO.Pipelines;
using DevTerm.Core.Transports;
using Microsoft.Extensions.Options;

namespace DevTerm.Transports.Usbtmc;

/// <summary>
/// <see cref="ITransport"/> for a USBTMC device connection. See docs/design/usbtmc-transport.md.
///
/// Unlike serial/TCP/HID, USBTMC has no unsolicited "data arrived" event to pump continuously -
/// a device only replies to an explicit read request, itself sent only after a command that
/// expects one. This transport heuristically treats an outgoing write containing a SCPI-style
/// query (a command whose mnemonic ends with '?') as expecting a reply: it writes the command,
/// then issues REQUEST_DEV_DEP_MSG_IN and pumps the reply into <see cref="Input"/>.
///
/// A reply is read per USBTMC 1.0 section 3.3: one logical message can arrive as several
/// transfers (each with its own header, EOM clear on all but the last - each needing its own
/// REQUEST_DEV_DEP_MSG_IN), and one transfer can span several bulk-IN reads (only the first
/// carries a header - see docs/design/features/usbtmc-bulk-in-reassembly-fix.md). A transfer ends
/// only when a read comes back shorter than the buffer it was given (a short packet), never just
/// because the declared TransferSize has been counted off. A timeout or protocol error aborts the
/// in-flight transfer (INITIATE_ABORT_BULK_IN/OUT) so it can't answer the next query instead.
/// </summary>
public sealed class UsbtmcTransport : ITransport
{
    // How many well-formed replies carrying some *other* request's bTag to discard while waiting
    // for this request's own reply - each one is a leftover from an earlier, abandoned request
    // (a timeout, a crashed previous session) that would otherwise answer this query instead.
    private const int _maxStaleRepliesSkipped = 4;

    private readonly IUsbtmcDeviceFactory _deviceFactory;
    private readonly IOptions<UsbtmcTransportOptions> _options;

    // Serializes every exchange with the device: a USBTMC exchange is a multi-step
    // write/request/read sequence, and two interleaved on the same endpoints corrupt each other
    // (and share _bulkOutTag). CloseAsync takes it too, so it never disposes the device out from
    // under a read still in progress on a thread-pool thread.
    private readonly SemaphoreSlim _ioLock = new(1, 1);

    private IUsbtmcDevice? _device;
    private ConnectionState _state = ConnectionState.Closed;
    private Pipe? _pipe;
    private byte _bulkOutTag;
    private bool _useRemote;
    private int _requestDelayMs;

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

    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State is ConnectionState.Open or ConnectionState.Opening)
            {
                return;
            }

            State = ConnectionState.Opening;

            var options = _options.Value;
            var quirks = UsbtmcDeviceQuirks.For(options.VendorId, options.ProductId);
            var useRemote = options.RemoteOnOpen && quirks.SupportsRemoteControl;
            var device = _deviceFactory.Create(options);

            try
            {
                // Off the caller's thread - enumerating/opening a USB device and the clear below
                // are blocking native calls, and the caller is often a UI thread.
                await Task.Run(
                    () =>
                    {
                        device.Open();

                        // A previous session that died mid-query (or was killed) can leave a
                        // reply queued in the device, which would otherwise answer this
                        // session's first query instead.
                        if (options.ClearOnOpen)
                        {
                            TryClear(device);
                        }

                        if (useRemote)
                        {
                            device.SetRemote(true);
                        }
                    },
                    CancellationToken.None).ConfigureAwait(false);
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
            _useRemote = useRemote;
            _requestDelayMs = options.RequestDelayMs ?? quirks.RequestDelayMs;

            State = ConnectionState.Open;
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        // Unblock a WriteAsync that's stuck flushing a reply over the pipe's default 64 KB pause
        // threshold into a pipe nobody is reading any more (Session stops its read loop before
        // calling CloseAsync) - otherwise this wait for _ioLock never returns, hanging every later
        // Open/Close/Dispose. See docs/bugs/002-usbtmc-close-hang-large-reply.md.
        _pipe?.Writer.CancelPendingFlush();

        await _ioLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_device is null)
            {
                return;
            }

            State = ConnectionState.Closing;

            var device = _device;
            var pipe = _pipe;
            _device = null;
            _pipe = null;

            await Task.Run(
                () =>
                {
                    if (_useRemote)
                    {
                        try { device.SetRemote(false); } catch { }
                    }

                    device.Close();
                    device.Dispose();
                },
                CancellationToken.None).ConfigureAwait(false);

            pipe?.Writer.Complete();

            State = ConnectionState.Closed;
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        // USBTMC 1.0 Table 3: a DEV_DEP_MSG_OUT's TransferSize must be > 0 - a device is entitled
        // to halt its bulk-OUT endpoint over an empty one (an empty typed line, say).
        if (data.IsEmpty)
        {
            return;
        }

        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_device is null || _pipe is null || State != ConnectionState.Open)
            {
                throw new InvalidOperationException("The USBTMC transport is not open.");
            }

            var device = _device;
            var writer = _pipe.Writer;
            var isQuery = IsQuery(data.Span);
            var command = data.ToArray();

            // Filled as the reply arrives, and flushed even if the exchange then fails part-way:
            // USBTMC 1.0 Table 11 has the host keep whatever message bytes did arrive before a
            // protocol error, not discard them.
            var replyChunks = new List<byte[]>();
            try
            {
                await Task.Run(() => Exchange(device, command, isQuery, replyChunks), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (replyChunks.Count > 0)
                {
                    foreach (var chunk in replyChunks)
                    {
                        chunk.CopyTo(writer.GetSpan(chunk.Length));
                        writer.Advance(chunk.Length);
                    }

                    // Not the caller's token: the reply has already been read off the device, and
                    // cancelling here would just throw it away.
                    await writer.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        // _ioLock is deliberately not disposed: Session.DisposeAsync disposes its transport, and
        // an owner's own `await using` then disposes it again - a second CloseAsync must stay a
        // no-op, not throw ObjectDisposedException. (A SemaphoreSlim whose AvailableWaitHandle is
        // never touched holds no unmanaged resources.)
        await CloseAsync();
        GC.SuppressFinalize(this);
    }

    // A SCPI-style query's mnemonic ends with '?', but a query can still take a
    // whitespace-separated parameter after it (e.g. ":MEAS:VPP? CHAN1") - checking only the very
    // last character misses every one of those. Confirmed against a real Rigol DS1102E: with
    // the last-character-only check, ":MEAS:VPP? CHAN1" was sent as a fire-and-forget write with
    // no REQUEST_DEV_DEP_MSG_IN/read ever issued, silently dropping the reply (and leaving it
    // unread in the device, which then risked misaligning the next command's read). A line can
    // also chain several ';'-separated commands, and needs a read if any one of them is a query
    // ("SYST:ERR?;*CLS" as well as "*RST;*IDN?"). Leading/trailing whitespace and line endings
    // are ignored.
    internal static bool IsQuery(ReadOnlySpan<byte> data)
    {
        foreach (var range in data.Split((byte)';'))
        {
            var command = TrimWhitespace(data[range]);
            var end = command.IndexOfAny((byte)' ', (byte)'\t');
            var mnemonic = end >= 0 ? command[..end] : command;

            if (mnemonic.Length > 0 && mnemonic[^1] == (byte)'?')
            {
                return true;
            }
        }

        return false;
    }

    private static ReadOnlySpan<byte> TrimWhitespace(ReadOnlySpan<byte> span)
    {
        ReadOnlySpan<byte> whitespace = [(byte)' ', (byte)'\t', (byte)'\r', (byte)'\n'];
        return span.Trim(whitespace);
    }

    // Runs on a thread-pool thread, under _ioLock.
    private void Exchange(IUsbtmcDevice device, byte[] command, bool isQuery, List<byte[]> replyChunks)
    {
        var tag = NextTag();
        WriteBulkOut(device, UsbtmcCodec.EncodeDevDepMsgOut(tag, command, eom: true), tag);

        if (isQuery)
        {
            if (_requestDelayMs > 0)
            {
                // See UsbtmcDeviceQuirks.RequestDelayMs - some firmware silently drops a read
                // request that arrives while it's still taking in the query itself.
                Thread.Sleep(_requestDelayMs);
            }

            ReadReply(device, replyChunks);
        }
    }

    // Reads one complete logical DEV_DEP_MSG_IN message: one transfer after another until one
    // arrives with EOM set (USBTMC 1.0 section 3.3.1.1 rules 9 and 13 - a device short on buffer
    // space may split a message across several transfers, each needing its own
    // REQUEST_DEV_DEP_MSG_IN).
    private void ReadReply(IUsbtmcDevice device, List<byte[]> chunks)
    {
        var buffer = new byte[UsbtmcCodec.BulkInBufferSize(device.MaxTransferSize, device.MaxPacketSize)];
        var total = 0;
        var retriedEmpty = false;

        while (true)
        {
            var (header, received) = ReadTransfer(device, buffer, chunks, total);
            total += received;

            if (header.TransferSize == 0 && total == 0 && !retriedEmpty)
            {
                // Confirmed against a real Rigol DS1102E: a query sent immediately after
                // OpenAsync sometimes gets back a completely well-formed, EOM-terminated, zero-byte
                // logical message before the real one. Re-issue the request once and take whatever
                // comes back, real or empty - a second empty reply in a row is treated as a
                // legitimately empty response rather than retried forever.
                retriedEmpty = true;
                continue;
            }

            if (received < header.TransferSize)
            {
                // USBTMC 1.0 Table 11 index 4: keep what arrived (already in chunks), report the
                // protocol error. EOM is meaningless here (section 3.3.1.1: "The Host must ignore
                // EOM if the device does not send TransferSize message data bytes").
                throw new IOException(
                    $"USBTMC device ended a transfer after {received} of its declared {header.TransferSize} byte(s).");
            }

            if (header.Eom)
            {
                return;
            }

            if (received == 0)
            {
                // An empty transfer that also claims more is coming would otherwise loop forever.
                throw new IOException("USBTMC device sent an empty, non-final reply transfer.");
            }
        }
    }

    // Requests and reads one DEV_DEP_MSG_IN transfer, appending its message bytes to chunks.
    // Returns the transfer's header and how many message bytes it actually carried.
    private (UsbtmcCodec.DecodedHeader Header, int Received) ReadTransfer(IUsbtmcDevice device, byte[] buffer, List<byte[]> chunks, int totalSoFar)
    {
        var tag = SendRequestDevDepMsgIn(device);
        var count = ReadFirst(device, buffer, ref tag);

        UsbtmcCodec.DecodedHeader header;
        for (var skipped = 0; ; skipped++)
        {
            try
            {
                header = UsbtmcCodec.DecodeHeader(buffer.AsSpan(0, count));
            }
            catch (InvalidOperationException ex)
            {
                // USBTMC 1.0 Table 11 indexes 1-3: discard, abort if the transfer is still open.
                if (count == buffer.Length)
                {
                    TryAbortBulkIn(device, tag);
                }

                throw new IOException(ex.Message, ex);
            }

            if (header.BTag == tag)
            {
                break;
            }

            if (skipped >= _maxStaleRepliesSkipped)
            {
                if (count == buffer.Length)
                {
                    TryAbortBulkIn(device, tag);
                }

                throw new IOException($"USBTMC bulk-IN header bTag {header.BTag} does not match expected {tag} (desynced?).");
            }

            // A reply to some earlier request - drain the rest of it, then look for ours.
            Debug.WriteLine($"USBTMC: discarding stale reply with bTag {header.BTag} (expected {tag}).");
            DrainTransfer(device, buffer, count, tag);
            count = ReadFirst(device, buffer, ref tag);
        }

        var maxResponseSize = _options.Value.MaxResponseSize;
        if ((long)totalSoFar + header.TransferSize > maxResponseSize)
        {
            if (count == buffer.Length)
            {
                TryAbortBulkIn(device, tag);
            }

            throw new IOException(
                $"USBTMC device declared a TransferSize of {header.TransferSize} byte(s), exceeding the configured MaxResponseSize of {maxResponseSize} byte(s).");
        }

        var payload = UsbtmcCodec.ExtractPayload(buffer.AsSpan(0, count), header);
        if (payload.Length > 0)
        {
            chunks.Add(payload.ToArray());
        }

        var received = payload.Length;
        var remaining = header.TransferSize - received;
        var alignmentBytes = count - UsbtmcCodec.HeaderSize - received;

        // A read that filled the buffer exactly hasn't seen the short packet that ends the
        // transfer yet, so keep reading - these continuation reads are raw payload (plus any
        // trailing alignment bytes), never another header. Stopping as soon as TransferSize is
        // counted off would leave the terminating short packet (or alignment bytes) queued, to be
        // misread as the next reply's header.
        var shortPacket = count < buffer.Length;
        while (!shortPacket)
        {
            try
            {
                count = device.ReadBulkIn(buffer, out _);
            }
            catch (TimeoutException ex)
            {
                TryAbortBulkIn(device, tag);
                throw new TimeoutException(
                    $"USBTMC device stopped sending after {received} of {header.TransferSize} byte(s).", ex);
            }

            shortPacket = count < buffer.Length;

            var take = Math.Min(count, remaining);
            if (take > 0)
            {
                chunks.Add(buffer.AsSpan(0, take).ToArray());
                received += take;
                remaining -= take;
            }

            // USBTMC 1.0 section 3.3 rule 10 allows at most wMaxPacketSize-1 alignment bytes.
            alignmentBytes += count - take;
            if (alignmentBytes >= device.MaxPacketSize)
            {
                if (!shortPacket)
                {
                    TryAbortBulkIn(device, tag);
                }

                throw new IOException($"USBTMC device sent more data than the {header.TransferSize} byte(s) its header declared.");
            }
        }

        return (header, received);
    }

    // Reads the first bulk-IN packet(s) of a transfer, retrying past the two ways a Rigol
    // device is known to fail to answer a REQUEST_DEV_DEP_MSG_IN on the first try.
    private int ReadFirst(IUsbtmcDevice device, byte[] buffer, ref byte tag)
    {
        var count = ReadBulkInOrAbort(device, buffer, tag, out var stalled);
        if (count > 0)
        {
            return count;
        }

        // Some Rigol firmware answers a REQUEST_DEV_DEP_MSG_IN with an empty transfer before
        // the real one (see libsigrok's scpi_usbtmc_libusb.c, which retries for exactly this
        // reason against a Rigol DS1054Z), and a reported stall can mean the device discarded
        // the pending request rather than just being slow to answer it - re-send the request
        // first in that case, then give the read one more chance either way before giving up.
        if (stalled)
        {
            tag = SendRequestDevDepMsgIn(device);
        }

        count = ReadBulkInOrAbort(device, buffer, tag, out _);
        if (count <= 0)
        {
            throw new IOException("USBTMC device returned no data for the query.");
        }

        return count;
    }

    private int ReadBulkInOrAbort(IUsbtmcDevice device, byte[] buffer, byte tag, out bool stalled)
    {
        try
        {
            return device.ReadBulkIn(buffer, out stalled);
        }
        catch (TimeoutException ex)
        {
            // Without an abort the request stays pending in the device, and its eventual reply
            // answers the *next* query instead - every query after a single slow one misaligned.
            TryAbortBulkIn(device, tag);
            throw new TimeoutException(
                $"USBTMC device did not answer within {_options.Value.ReadTimeoutMs} ms (ReadTimeoutMs).", ex);
        }
    }

    // Reads and discards the rest of a transfer whose first read returned firstCount bytes.
    private void DrainTransfer(IUsbtmcDevice device, byte[] buffer, int firstCount, byte tag)
    {
        var count = firstCount;
        var guard = (_options.Value.MaxResponseSize / buffer.Length) + 2;
        while (count == buffer.Length)
        {
            if (--guard < 0)
            {
                TryAbortBulkIn(device, tag);
                throw new IOException("USBTMC device kept sending while a stale reply was being discarded.");
            }

            count = ReadBulkInOrAbort(device, buffer, tag, out _);
        }
    }

    private byte SendRequestDevDepMsgIn(IUsbtmcDevice device)
    {
        var tag = NextTag();
        var requestFrame = UsbtmcCodec.EncodeRequestDevDepMsgIn(tag, int.MaxValue, termChar: 0, termCharEnabled: false);
        WriteBulkOut(device, requestFrame, tag);
        return tag;
    }

    private static void WriteBulkOut(IUsbtmcDevice device, byte[] frame, byte tag)
    {
        try
        {
            device.WriteBulkOut(frame);
        }
        catch (TimeoutException)
        {
            TryAbortBulkOut(device, tag);
            throw;
        }
    }

    private byte NextTag() => _bulkOutTag = UsbtmcCodec.NextTag(_bulkOutTag);

    // Recovery is best-effort: the original failure is what the caller needs to see. An abort
    // that itself fails falls back to INITIATE_CLEAR, the spec's bigger hammer.
    private static void TryAbortBulkIn(IUsbtmcDevice device, byte tag)
    {
        try
        {
            device.AbortBulkIn(tag);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"USBTMC: INITIATE_ABORT_BULK_IN failed, falling back to INITIATE_CLEAR: {ex}");
            TryClear(device);
        }
    }

    private static void TryAbortBulkOut(IUsbtmcDevice device, byte tag)
    {
        try
        {
            device.AbortBulkOut(tag);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"USBTMC: INITIATE_ABORT_BULK_OUT failed, falling back to INITIATE_CLEAR: {ex}");
            TryClear(device);
        }
    }

    private static void TryClear(IUsbtmcDevice device)
    {
        try
        {
            device.Clear();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"USBTMC: INITIATE_CLEAR failed: {ex}");
        }
    }
}
