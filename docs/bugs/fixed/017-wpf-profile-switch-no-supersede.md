# 017: A WPF profile switch can't be superseded by a second switch

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Fixed |
| **Confidence** | Confirmed |
| **Area** | WPF (MainWindow) |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Wpf/MainWindow.xaml.cs:618-667`; compare the TUI's `switchCts` at `src/DevTerm.Console/TuiMode.cs:551-637`.

## What happens
`_session.OpenAsync()` is called with no cancellation token. A second switch's `await _session.CloseAsync()` waits on
`_lifecycleLock`, which the pending `transport.OpenAsync` holds (`Session.cs:116-119, 154`).

## Failure scenario
The TUI comment's ".108 then .107" case: switch to an unreachable host, then immediately to a good one. In WPF the
second switch waits out the first's OS connect timeout (about 20 s), showing "Connecting".

## Suggested fix
Port the TUI's `CancellationTokenSource` plus `ReferenceEquals(switchCts, cts)` guards.

## Tests to add
WPF: a switch superseded by another before it resolves (the TUI already has this test).

## Resolution
Fixed in `dev/fix-bugs` on 2026-09-26: ported the TUI's `_switchCts`/`ReferenceEquals(_switchCts, cts)` guards
around `mySession.OpenAsync(cts.Token)`, plus two things the naive port alone still got wrong (both found by
this fix's own regression test failing after the port, with the port otherwise looking identical to the TUI's):
- `MainWindow.SwitchProfileAsync` closed/disposed the *old* session by re-reading the shared `_session` field
  after an `await`, not a value captured before any `await` - so a second, overlapping switch's own read of that
  same field (while the first switch's close of it was still pending) could end up closing/disposing whichever
  session the *other* switch had by then installed there, including a second attempt's brand-new, just-opened
  session. Fixed by capturing `var oldSession = _session;` synchronously at the top of the method, before the
  first `await`, and closing/disposing that local, never the field, from then on.
- `_session = mySession;` (and the subsequent subscribe/UI setup) ran unconditionally, with no supersede check -
  a stale attempt could still reach it after a newer attempt had already adopted its own session, reassigning
  `_session` back to the stale one's (never fully opened) session. Fixed by checking
  `ReferenceEquals(_switchCts, cts)` right before that reassignment and bailing out (disposing `mySession`
  without ever subscribing to it) if superseded by then.

`src/DevTerm.Console/TuiMode.cs`'s `SwitchProfileAsync` has the same two gaps (it reads the shared `session`
variable for the old-session close, and reassigns `session = mySession` with no supersede check) - not fixed
here since this report was WPF-only; see [060](060-tui-profile-switch-session-race.md).

Regression test: `MainWindowSwitchProfileTests.SwitchProfileAsync_SupersededByAnotherSwitchBeforeItResolves_DoesNotStompTheNewerOne`.
