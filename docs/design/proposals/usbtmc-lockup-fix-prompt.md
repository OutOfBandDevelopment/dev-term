# Task: Fix USBTMC bulk-IN reassembly bug causing Rigol (DG1022, DM3058E, DS1102E) communication lockups

## Context
`DevTerm.Transports.Usbtmc` talks SCPI over USBTMC to Rigol test instruments via
LibUsbDotNet/libusb. Field reports show communication "locking up" against
DG1022, DS1102E, and DM3058E. Code review found the root cause plus two
supporting hardening gaps. Fix all of them together — they interact.

Relevant files:
- `UsbtmcCodec.cs` — pure header encode/decode, no device I/O.
- `UsbtmcTransport.cs` — the `ITransport` implementation; `ReadReply()` is where
  the bug lives.
- `SystemUsbtmcDevice.cs` — raw bulk transport (`WriteBulkOut`/`ReadBulkIn`).
  No changes needed here.
- `UsbtmcTransportOptions.cs` — config/options class.

## Root cause (the actual lockup)
Per the USBTMC 1.0 spec, one logical `DEV_DEP_MSG_IN` response can span
**multiple physical bulk-IN transfers** when the reply is larger than one USB
transfer. Only the **first** physical transfer carries the 12-byte header
(MsgID, bTag, `~bTag`, TransferSize, attributes/EOM). Every subsequent
transfer for that same logical message is **raw continuation payload with no
header at all** — this mirrors libsigrok's `scpi_usbtmc_libusb.c`, which
decodes the header exactly once in `scpi_usbtmc_bulkin_start` and reads pure
bytes in `scpi_usbtmc_bulkin_continue`, tracked via a `remaining_length` counter.

`UsbtmcTransport.ReadReply()` currently calls `UsbtmcCodec.DecodeHeader` on
**every** iteration of its read loop, including continuation reads:

```csharp
while (count > 0)
{
    var header = UsbtmcCodec.DecodeHeader(readBuffer.AsSpan(0, count)); // WRONG on 2nd+ read
    ...
    if (header.Eom) break;
    count = device.ReadBulkIn(readBuffer, out _);
}
```

For short replies (single transfer) this never surfaces. For any reply long
enough to span a second physical transfer, the second read's raw payload bytes
get reinterpreted as a bogus header, which can produce an absurd
`TransferSize` — and the loop then blocks on `ReadBulkIn` waiting for bytes
that were never coming. That reads externally as "communication locks up,"
and is intermittent because it depends on response length crossing a
transfer-size boundary (varies by query and by instrument).

A second, compounding bug: `SendRequestDevDepMsgIn` requests only
`device.MaxTransferSize` (one buffer's worth) instead of an effectively
unlimited size. Sigrok always requests `INT32_MAX` in a single
`REQUEST_DEV_DEP_MSG_IN` per logical response and never issues a second one
for the same response — requesting a small size instead risks the device
truncating and clearing EOM at that cap, with nothing in this code to then
issue a follow-up request, which is a second path to the same hang.

## Required fixes

### 1. `UsbtmcTransport.ReadReply()` — decode the header exactly once, track remaining bytes
Rewrite so the header is parsed only on the first physical read of a logical
response, then continuation reads are pure byte-counted payload against
`TransferSize`, e.g.:

```csharp
private List<byte[]> ReadReply(IUsbtmcDevice device)
{
    var requestTag = SendRequestDevDepMsgIn(device);

    var chunks = new List<byte[]>();
    var readBuffer = new byte[UsbtmcCodec.HeaderSize + device.MaxTransferSize];

    var count = device.ReadBulkIn(readBuffer, out var stalled);
    if (count <= 0)
    {
        if (stalled)
        {
            SendRequestDevDepMsgIn(device); // a stall may mean the device dropped the request
        }
        count = device.ReadBulkIn(readBuffer, out _);
        if (count <= 0)
        {
            throw new IOException("USBTMC device returned no data for the query.");
        }
    }

    var header = UsbtmcCodec.DecodeHeader(readBuffer.AsSpan(0, count), requestTag);
    var firstPayload = UsbtmcCodec.ExtractPayload(readBuffer.AsSpan(0, count), header);
    if (firstPayload.Length > 0)
        chunks.Add(firstPayload.ToArray());

    long remaining = header.TransferSize - firstPayload.Length;

    while (remaining > 0)
    {
        count = device.ReadBulkIn(readBuffer, out _);
        if (count <= 0)
            throw new IOException("USBTMC continuation read returned no data before TransferSize was fully received.");

        var take = (int)Math.Min(count, remaining);
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
```

### 2. `UsbtmcCodec.DecodeHeader` — validate MsgID, bTag/`~bTag`, and the expected tag
Currently it decodes fields with no integrity check. A corrupted or desynced
packet (e.g. after a prior timeout/abort) can produce a garbage `TransferSize`
with nothing to catch it — which, combined with bug #1's loop, is a second
independent way to hang forever waiting for phantom bytes. Add an
`expectedTag` parameter and validate before returning:

```csharp
public static DecodedHeader DecodeHeader(ReadOnlySpan<byte> transfer, byte expectedTag)
{
    if (transfer.Length < HeaderSize)
        throw new InvalidOperationException(
            $"USBTMC bulk-IN transfer of {transfer.Length} byte(s) is shorter than the {HeaderSize}-byte header.");

    var msgId = transfer[0];
    var bTag = transfer[1];
    var bTagInverse = transfer[2];

    if (msgId != DevDepMsgIn)
        throw new InvalidOperationException($"USBTMC bulk-IN header has unexpected MsgID {msgId} (expected {DevDepMsgIn}).");
    if (bTagInverse != unchecked((byte)~bTag))
        throw new InvalidOperationException($"USBTMC bulk-IN header failed bTag/~bTag consistency check (bTag={bTag}, ~bTag byte={bTagInverse}).");
    if (bTag != expectedTag)
        throw new InvalidOperationException($"USBTMC bulk-IN header bTag {bTag} does not match expected {expectedTag} (desynced?).");

    var transferSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(transfer[4..8]);
    var eom = (transfer[8] & EomBit) != 0;
    return new DecodedHeader(msgId, bTag, transferSize, eom);
}
```

Update the one call site in `ReadReply` to pass `requestTag` (the tag it just
sent in `SendRequestDevDepMsgIn`).

### 3. `UsbtmcTransportOptions` — add a cap on declared response size
Even with #2's validation, a header can be well-formed (correct MsgID, correct
bTag/`~bTag`, correct tag) but still declare an implausible `TransferSize` due
to a firmware bug rather than framing corruption — #2 can't catch that, since
nothing about the header itself is malformed. Add:

```csharp
/// <summary>
/// Upper bound on a single logical response's declared TransferSize. A well-formed but
/// implausible value here (firmware bug, not framing corruption) is otherwise
/// indistinguishable from a legitimately large reply, and the continuation-read loop
/// has no other way to bail out.
/// </summary>
[Range(1, int.MaxValue)]
public int MaxResponseSize { get; set; } = 16 * 1024 * 1024; // 16 MB
```

And in `ReadReply`, right after decoding the first header, check
`header.TransferSize` against `_options.Value.MaxResponseSize` and throw a
clear `IOException` if it's exceeded, instead of entering the drain loop.

### 4. Confirm options validation is actually wired up
`[Range]` attributes on `UsbtmcTransportOptions` do nothing unless the DI
registration calls `.ValidateDataAnnotations()` (ideally also
`.ValidateOnStart()`) on the `OptionsBuilder<UsbtmcTransportOptions>`. Find
wherever this options class is registered and confirm/add that, so a bad
VID/PID/`MaxResponseSize` from config fails fast at startup with a clear
message instead of surfacing later as a confusing device-not-found or
buffer-size exception.

## Testing requirement — this is the part most likely to be skipped
A unit test with a mocked `IUsbtmcDevice` that only ever returns one
`ReadBulkIn` call (one packet, header + full payload, EOM set) will pass both
before and after this fix and will NOT catch this bug or a regression of it.
You must add a test that mocks `ReadBulkIn` to return **at least two**
physical transfers for one logical response: the first with a real header and
partial payload (EOM not set), the second with pure continuation payload (no
header) that completes `TransferSize`. Assert the reassembled reply is
correct. Also add a test where the second call intentionally contains data
that would decode as a bogus header if (incorrectly) re-parsed, to prove the
fix doesn't do that.

Also add tests for `UsbtmcCodec.DecodeHeader`:
- Rejects a header with `bTag != expectedTag`.
- Rejects a header where `transfer[2] != ~transfer[1]`.
- Rejects a header with the wrong `MsgID`.
- Accepts a valid, matching header.

If real hardware (any of DG1022 / DM3058E / DS1102E / DG1062Z) is available,
verify against it with a query known to return a longer response (e.g. a
longer `*IDN?`/status string, or a waveform preamble query) to confirm the
multi-transfer path is genuinely exercised, not just unit-mocked.

## Do not change
- `SystemUsbtmcDevice.cs` and `SystemUsbtmcDeviceDiscovery.cs` are correct as
  reviewed; no changes needed there.
- Don't reintroduce a second `REQUEST_DEV_DEP_MSG_IN` per logical response —
  that reintroduces the original multi-request lockup this codebase already
  fixed once (see git history / docs/design/usbtmc-transport.md).
