# 006: One missing SCPI or manifest reply shifts every later reply onto the wrong field

| | |
|---|---|
| **Severity** | High |
| **Status** | Fixed |
| **Confidence** | Confirmed from code; effect on real devices inferred (found by two reviewers) |
| **Area** | DevTerm.Core (LineReplyPresenter), DevTerm.Devices.Scpi, DevTerm.DeviceManifests |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
- `src/DevTerm.Core/Presenters/LineReplyPresenter.cs:28, 39, 98`
- `src/DevTerm.Devices.Scpi/ScpiAutoDetect.cs:285-291`
- `src/DevTerm.Devices.Scpi/ScpiControlSurface.cs:69-85`, `src/DevTerm.DeviceManifests/ManifestControlSurface.cs:243-248`

## What happens
`QuerySent` adds the query's reply id to a FIFO queue, and each reply line dequeues one. Nothing ever removes an
entry: not on a timeout, not when the send fails after `QuerySent`, not on reconnect. The presenter (and its
`_buffer`/`_pendingCr`) survives `CloseAsync`/`OpenAsync`. `_buffer` also has no maximum length, unlike
`AsciiPresenter`'s.

## Failure scenario
- `ScpiAutoDetect.DetectAsync` queues `scpiAutoDetect.reply`, and on `NoReply` returns without removing it. The TUI
  (`TuiMode.cs:893-927`) and WPF then open the Generic panel on that same presenter. The user clicks Identify: the
  reply is taken by the stale id, `idn.reply` stays blank, and from then on every reply shows on the previous
  query's indicator.
- The same shift follows a query the device ignores (a rejected SCPI command goes to its error queue with no reply
  line), a failed send, an unsolicited line, or a stray empty line (`\n\n`, or LF then CR).
- A hinted binary block (a screen dump) also flows through this presenter; it splits on random 0x0A/0x0D bytes and
  each fragment dequeues an id.
- Stale ids and a partial line carry into the next connection. `AsciiPresenter`'s partial line has the same
  carry-over.

## Suggested fix
- Return a handle from `QuerySent`; remove it on timeout or send failure.
- Add a `Reset()` that clears the queue and buffer, called on session open (for example through an optional
  presenter-reset hook in `Session.OpenAsync`).
- Expire pending ids after a deadline; skip empty lines while an id is pending; cap the buffer.

## Tests to add
A query registered after an auto-detect `NoReply`; after a failed send; a missing reply followed by a good one;
reconnect with a pending id.

## Resolution
Fixed on 2026-09-26 (branch `dev/fix-bugs`):

- `IReplyTracker` gained `Cancel(string replyIndicatorId)` (`src/DevTerm.Core/Presenters/IReplyTracker.cs`).
  `LineReplyPresenter` (the shared base of `ScpiReplyPresenter`/`ManifestReplyPresenter`) implements it by
  scanning/rebuilding its `ConcurrentQueue<string>` to drop exactly one matching entry
  (`src/DevTerm.Core/Presenters/LineReplyPresenter.cs`).
- `ScpiAutoDetect.DetectAsync` now calls `Cancel` on both a failed `*IDN?` send and a `NoReply` timeout
  (`src/DevTerm.Devices.Scpi/ScpiAutoDetect.cs`), so the next query no longer inherits the stale
  `scpiAutoDetect.reply` id.
- `ScpiControlSurface.InvokeAsync` and `ManifestControlSurface.InvokeAsync` route their sends through a private
  `SendAsync` helper that calls `Cancel` on the tracker if the send throws, then rethrows
  (`src/DevTerm.Devices.Scpi/ScpiControlSurface.cs`, `src/DevTerm.DeviceManifests/ManifestControlSurface.cs`).
- A new `IResettablePresenter` interface (`src/DevTerm.Core/Presenters/IResettablePresenter.cs`) exposes `Reset()`;
  `LineReplyPresenter` and `AsciiPresenter` both implement it to clear their pending-reply queue and partial line.
  `Session.OpenAsync` calls `Reset()` on every bound presenter that implements it, before starting the read loop
  (`src/DevTerm.Core/Sessions/Session.cs`), so stale state from a previous connection never carries into a
  reopened one.
- `LineReplyPresenter._buffer` now caps at 4096 bytes, flushing whatever's buffered once reached, rather than
  growing unbounded against a terminatorless/binary stream.
- Not implemented: a generic pending-id expiry timer, and skipping empty lines while an id is pending — the two
  concrete cases the report calls out (a failed send, an auto-detect timeout) are handled at the exact call sites
  where "no reply is coming" becomes known, which covers the report's failure scenarios without a new generic
  subsystem; skipping empty lines was judged unsafe in general (some devices legitimately reply with an empty line).

Regression tests: `ScpiReplyPresenterTests.QuerySent_ThenCancel_TheNextLineIsUnsolicitedInstead`,
`QuerySent_ThenCancel_LeavesAnEarlierStillPendingQueryInPlace`, `Cancel_WithNoMatchingPendingId_IsANoOp`,
`Reset_ClearsAPendingQueryAndAPartialLine`; `ScpiAutoDetectTests.DetectAsync_AfterNoReply_ALaterQueryStillCorrelatesCorrectly`;
`ScpiControlSurfaceTests.InvokeAsync_QueryCommand_WhenSendFails_CancelsTheReplyIndicatorAndRethrows`;
`ManifestControlSurfaceTests.InvokeAsync_QueryCommand_WhenSendFails_CancelsTheReplyIndicatorAndRethrows`;
`SessionTests.OpenAsync_ResetsAnyResettablePresenterBeforeStartingTheReadLoop`,
`OpenAsync_AfterClose_ResetsTheResettablePresenterAgain`. All confirmed to fail (mostly by not compiling, since
each depends directly on a new member) against the pre-fix code, and pass with it.
