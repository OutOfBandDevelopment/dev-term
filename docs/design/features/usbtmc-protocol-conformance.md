# USBTMC transport: protocol-conformance rework and Rigol quirks

## Status: implemented (2026-09-25), DG1022 and DS1102E verified against real hardware

Branch `dev/usbtmc-fix`. Follows [`usbtmc-bulk-in-reassembly-fix.md`](usbtmc-bulk-in-reassembly-fix.md),
whose reassembly fix stays, generalized below. Verified against a real **Rigol DG1022** and a real
**Rigol DS1102E**, including a byte-exact waveform read (`docs/test/2026-09-25-18-03-06.md`; covered by
`DevTerm.Console.Tests.RealHardwareUsbtmcTransportTests`). **Not yet re-verified on the DG1062Z or
DM3058E.** Those devices need a bench pass before merge, because this change alters what goes over the wire:
- REN_CONTROL now actually asserts REN.
- Aborts are sent on a timeout.
- Continuation reads run until a short packet arrives.

## What was wrong (a review of `DevTerm.Transports.Usbtmc` against `docs/protocols/usbtmc/`)

1. **EOM was decoded and never used.** USBTMC 1.0 §3.3.1.1 rules 9 and 13 allow a message to be sent as several
   transfers. Each has its own header, EOM is clear on all but the last, and each needs its own
   `REQUEST_DEV_DEP_MSG_IN`. Only the first was read. The rest stayed queued and answered the next query.
2. **A short packet wasn't treated as the end of a transfer.** Rule 11 says a device sends nothing more after a
   short packet. A transfer that ended early therefore waited out the read timeout and threw, which discarded the bytes that
   did arrive; Table 11 index 4 says the host must keep them.
3. **The read buffer size wasn't a whole number of packets.** It was `12 + MaxTransferSize` = 65548. libusb overflows once a reply
   reaches the buffer's last partial packet. On WinUSB (`ALLOW_PARTIAL_READS`) the surplus is instead carried silently into the *next* read.
4. **There was no abort or clear.** A timed-out request stayed pending in the device, so its late reply answered the next
   query. The tag check then threw, and every query after that was off by one. Table 11 requires
   `INITIATE_ABORT_BULK_IN`.
5. **REN_CONTROL was sent with wValue=0,** which de-asserts REN (USB488 Table 15). An earlier "spec nit" change had
   introduced this.
6. **A zero-length `DEV_DEP_MSG_OUT` could be sent.** Table 3 says TransferSize must be > 0.
7. **`SystemUsbtmcDevice.Open` leaked devices.** Every device listed after the match went undisposed, which is the finalizer
   access-violation hazard its own comments describe. A candidate that failed `Open()` also aborted the whole search.
8. **There was no serialization.** Concurrent writes interleaved on the endpoints, and `CloseAsync` could dispose the
   device mid-read.
9. **A declared TransferSize above 2³¹ wrapped negative** and turned into a silent empty reply.
10. **Query detection missed** leading whitespace, tab-separated parameters, and `;`-chained queries such as
    `SYST:ERR?;*CLS`.
11. **Disposing twice threw** `ObjectDisposedException` (found live: `Session.DisposeAsync` disposes the
    transport, and the owner's `await using` disposes it again).

## What changed

- **`ReadReply`:**
  - Loops transfer by transfer until EOM, with a fresh request and a fresh header bTag check for each one.
  - Each transfer is read until a short packet. Continuation reads are raw payload, and trailing alignment bytes are discarded. More than wMaxPacketSize−1 of them is a protocol error.
  - Bytes that arrive before an error are still flushed to `Input`.
  - A well-formed reply carrying another request's bTag is drained and skipped, up to 4 times, instead of desyncing permanently.
  - The DS1102E "phantom empty reply" retry is kept.
- **Buffers:** bulk-IN buffers come from `UsbtmcCodec.BulkInBufferSize`, which rounds header + `MaxTransferSize` up to whole packets. `IUsbtmcDevice.MaxPacketSize` now exposes the endpoint's wMaxPacketSize.
- **Timeouts and recovery:**
  - `ReadBulkIn`/`WriteBulkOut` throw `TimeoutException` on a timeout. A 0-byte read now means a real zero-length packet.
  - A bulk-IN timeout or a bad header runs `AbortBulkIn` (INITIATE_ABORT_BULK_IN, drain, then CHECK_ABORT_BULK_IN_STATUS).
  - A bulk-OUT timeout runs `AbortBulkOut`.
  - If an abort fails, `Clear()` (INITIATE_CLEAR / CHECK_CLEAR_STATUS) runs instead.
  - Every status poll is bounded.
- **Remote/local:**
  - `SetRemote(true)` sends REN_CONTROL with wValue=1.
  - `SetRemote(false)` sends GO_TO_LOCAL, then REN_CONTROL with wValue=0.
  - Both are gated on `bInterfaceProtocol == 1` and GET_CAPABILITIES byte 14 bit 1.
- **Locking and opening:**
  - All I/O runs under one `SemaphoreSlim`, and `CloseAsync` waits for an exchange in progress.
  - `OpenAsync`'s blocking device work runs off the caller's thread.
- **Other fixes:** leak-free device matching, active-configuration interface selection, a TransferSize > int.MaxValue check, empty writes skipped, better `IsQuery`, and idempotent dispose.
- **New options:**
  - `ClearOnOpen` (default **off**)
  - `RemoteOnOpen` (default on, subject to quirks)
  - `RequestDelayMs` (null = quirk default)
  - `[Range]` validation on the timeouts and `MaxTransferSize`
- **`UsbtmcDeviceQuirks`**, keyed by VID:PID. The only entry so far is Rigol `0x1AB1:0x0588`, which the DS1102E *and* the DG1022 share:
  - `RequestDelayMs = 20`.
  - `SupportsRemoteControl = false`.

## Rigol findings that shaped the defaults

- **DG1022 needs a delay before the read request.** Confirmed on real hardware: a `REQUEST_DEV_DEP_MSG_IN` sent
  back-to-back with the query's `DEV_DEP_MSG_OUT` is never answered. Every query timed out, over 3 of 3 runs. A gap of
  1 ms or more (5/10/20/50/100 ms were all tried) made every query answer. This is the root cause of the intermittent
  "returned no data" / rapid-fire failures in `BACKLOG.md`. The earlier "`*IDN?` works, the next query fails" pattern
  was the open-time control requests happening to supply that gap for the first query only.
- **Never send REN_CONTROL to 0x1AB1:0x0588.** libsigrok blacklists it: "publishes RL1 support, but doesn't support it". A
  real DG1022 timed out GO_TO_LOCAL.
- **INITIATE_CLEAR on open is off by default.** It is reported to hang the Rigol DS1000Z (python-usbtmc PR #62, issue #43),
  and neither the Linux driver nor pyvisa-py sends it on open.
- **The DG1022 ends replies with `\n\r`** (for example `OFF\n\r`), so an ASCII presenter splitting on both shows a blank line.
  This is device behavior, not a framing bug.

## Open / follow-up (see `BACKLOG.md`)

- **DS1102E `:WAV:DATA?`, checked on the bench:** TransferSize is exact (`#800000600` + 600 samples = 610). The 10 extra
  bytes that follow are constant padding, and the rework correctly drops them as alignment. That contradicts pyvisa-py
  #472's "10 bytes short" reading. Still unexercised: #472's missing ZLP at an exact packet boundary, which would cost one
  `ReadTimeoutMs` and then raise an error. It needs a reply that lands on a 64-byte boundary.
- **ClearHalt on both endpoints at open** (kept from before) is implicated in libsigrok's 0x0588 hang workaround and
  can desync data toggles. It hasn't been A/B tested on the bench.
- **Real-hardware re-verification** on the DG1062Z and DM3058E.
