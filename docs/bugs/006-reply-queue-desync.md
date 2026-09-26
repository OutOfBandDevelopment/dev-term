# 006: One missing SCPI or manifest reply shifts every later reply onto the wrong field

| | |
|---|---|
| **Severity** | High |
| **Status** | Open |
| **Confidence** | Confirmed from code; effect on real devices inferred (found by two reviewers) |
| **Area** | DevTerm.Core (LineReplyPresenter), DevTerm.Devices.Scpi, DevTerm.DeviceManifests |
| **Found** | 2026-09-26 code review of `main` @ `42758db` (branch `dev/review-code`); static review, not yet reproduced |

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
