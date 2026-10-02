# 063: A terminatorless reply over 4096 bytes burns extra entries off the SCPI/manifest reply-id queue

| | |
|---|---|
| **Severity** | High |
| **Status** | Open |
| **Confidence** | Reproduced (real hardware: Tektronix TDS2024 over a TCP serial-to-Ethernet bridge, 2026-10-01) |
| **Area** | DevTerm.Core (LineReplyPresenter), DevTerm.Devices.Scpi (ScpiReplyPresenter), DevTerm.DeviceManifests (ManifestReplyPresenter) |
| **Created** | 2026-10-01 |
| **Found at commit** | `2d7c640a83540f5c5255e2b47dca1a00a940ccf1` (`dev/hardware-review`) |
| **Found by** | Live hardware session (user-reported symptom, investigated interactively) |

## Where
- `src/DevTerm.Core/Presenters/LineReplyPresenter.cs:126-129` (the size-cap force-flush)
- `src/DevTerm.Core/Presenters/LineReplyPresenter.cs:146-163` (`Complete`, unconditionally dequeues a reply id)
- `src/DevTerm.Devices.Scpi/ScpiReplyPresenter.cs` (the `scpi` presenter — this is the concrete `[scpi]`-tagged
  output seen in the failure scenario below; same base class also backs `DevTerm.DeviceManifests.ManifestReplyPresenter`)

## What happens
`QuerySent` (`LineReplyPresenter.cs:45`) enqueues one reply id per command sent, and `Complete` (line 146) always
dequeues exactly one id and assigns it the line just finished (line 153-156), regardless of *why* that line ended.
`Render` calls `Complete` for three different reasons, which `Complete` can't tell apart:

1. A real CR/LF terminator (lines 102, 118) — a genuine, complete reply.
2. `_terminatorless` mode ending at the end of one `Render` call (line 133-136) — also a genuine, complete reply for
   a no-terminator device.
3. **The buffer hitting its 4096-byte safety cap** (line 126-129, `_maxBufferLength`) — this exists purely so an
   unbounded terminatorless/binary stream can't grow the buffer forever (see
   [006](fixed/006-reply-queue-desync.md)'s resolution). It is *not* the end of the reply; it's an arbitrary chunk
   boundary partway through one.

Because case 3 calls the same `Complete` as cases 1/2, every forced 4096-byte chunk of a long terminatorless/binary
reply dequeues one entry from `_pendingReplyIds` that was never actually answered. A single query whose real reply
is, say, 20 KB of binary data with no CR/LF anywhere in it (a Tektronix `HARDCOPY START` dump, a device-manifest
binary block) forces several of these spurious completions before the real end of that reply is reached — so by the
time the *next* real query's reply comes back, the queue has already been drained by N phantom completions, and that
next reply gets matched to the wrong (or no) id. The same desync reaches `ScpiControlSurface`/`ManifestControlSurface`'s
reply-indicator correlation (`IStructuredPresenter.ValuesChanged`), not just the raw text view — this is the same
failure class [006](fixed/006-reply-queue-desync.md) fixed for *missing* replies, left open for *oversized* ones;
006's own failure scenario called this out directly ("A hinted binary block … splits on random 0x0A/0x0D bytes and
each fragment dequeues an id") but the landed fix only capped the buffer's growth, without separating "flushed for
safety" from "this is a complete reply."

## Failure scenario
Real-hardware transcript against a Tektronix TDS2024 (TCP, 192.168.0.110:23), SCPI presenter active:

```
Out> HARDCOPY START
[scpi] (tens of KB of raw BMP bytes, no CR/LF anywhere in the pixel data - forces several 4096-byte
       chunk completions, each dequeuing one entry from _pendingReplyIds)
Out> *IDN?
[scpi] QQQQQQQQQQ...QQTEKTRONIX,TDS 2024,0,CF:91.1CT FV:v4.12 TDS2CM:CMV:v1.04
```

`*IDN?`'s own `QuerySent` call registered its reply id *after* `HARDCOPY START`'s chunked completions had already
drained several ids ahead of it, so by the time `*IDN?`'s real reply line finally completes (on its own trailing
CR), the id dequeued for it is not the one `*IDN?` itself registered — and the visible text itself is also wrong,
carrying the unflushed binary tail of the previous forced chunk as a prefix. Any `ScpiControlSurface`/
`ManifestControlSurface` panel field reading from this presenter during the dump would show the device's binary
reply (or garbage) on the *wrong* field, exactly as [006](fixed/006-reply-queue-desync.md) described for a missing
reply — just triggered by an oversized one instead.

## Suggested fix
Distinguish the size-cap flush from a genuine end-of-reply in `Render`/`Complete`:
- Give `Complete` a parameter (or a second, non-correlating method) for "flushed for buffer-safety only" that still
  emits the chunk as display text (so the raw transcript doesn't stall or lose data) but does **not** dequeue
  `_pendingReplyIds`, call `AddLineValues`, or raise `ValuesChanged` — only a real terminator (or terminatorless
  end-of-`Render`) should consume a reply id and correlate a value.
- Keep the first chunk's correlation if the eventual real reply can be reassembled from ordered chunks (optional;
  not required to fix the desync, since the control-panel use case only needs the final complete value and the
  oversized case is inherently a raw/binary dump that has no useful "value" until it's complete anyway).

## Tests to add
- `ScpiReplyPresenterTests`: a query whose reply exceeds `_maxBufferLength` (concatenate >4096 raw bytes with no
  CR/LF), followed by a second, short, normal query/reply — assert the second query's `ValuesChanged` carries the
  second query's own reply (not the first query's tail, and not empty), and that no stale id is left in the queue.
- A regression test mirroring the real transcript above: a long terminatorless binary block, then `*IDN?` — assert
  the correlated value for `*IDN?` is exactly the identity string, with no leading garbage and no id-queue
  desync.
