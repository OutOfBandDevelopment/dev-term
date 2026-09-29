# 060: A TUI profile switch can close/reassign the wrong session under overlap

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Open |
| **Confidence** | Confirmed |
| **Area** | TUI (Console) |
| **Created** | 2026-09-26 |
| **Found at commit** | `d5729d15f5be4945f94dd88cceb4c69bed1f101f` (`dev/fix-bugs`) |
| **Found by** | Code review while fixing bug [017](fixed/017-wpf-profile-switch-no-supersede.md) (WPF) |

## Where
`src/DevTerm.Console/TuiMode.cs`, `SwitchProfileAsync` local function (starts at line 593):
- Lines 612-615: unsubscribes/closes/disposes the *old* session by reading the shared `session`
  closure variable, not a value captured before any `await`.
- Line 617: `session = mySession;` reassigns the shared variable unconditionally, with no
  `ReferenceEquals(switchCts, cts)` supersede check beforehand.

## What happens
This is structurally identical to bug 017's WPF code before it was fixed - it was in fact the
source the WPF version was ported from. Two overlapping `SwitchProfileAsync` calls both close
whatever session the shared `session` variable currently points to, rather than each closing the
one it itself replaced:
- Lines 612-615 read `session` *after* `built = DevTermSessionBuilder.Build(newOptions)` (which can
  suspend) has already run. If a second, newer call reassigns `session = mySession` (line 617)
  before the first call's continuation resumes at line 612, the first call ends up closing and
  disposing the *second* call's brand-new session instead of its own predecessor.
- Line 617 reassigns `session = mySession;` with no check for whether `switchCts` has already moved
  on to a newer attempt. A stale attempt that reaches this line after a newer one already adopted
  its own session overwrites `session` back to the stale attempt's own session, which was never
  opened with a live `OpenAsync` guarantee at this point and may be about to be cancelled.

## Failure scenario
Same "switch to .108, then immediately to .107" scenario as bug 017, but in the TUI: two rapid
profile switches (e.g. via the connection editor or a scripted `--cli` session) can race such that
the second, intended-to-win switch has its own session closed by the first switch's stale
continuation, or has `session` reassigned back to the first switch's session after the second
switch already opened its connection - leaving the TUI's live `session` pointed at a session that
was never truly current, or torn down.

## Suggested fix
Port the same two fixes applied to `MainWindow.SwitchProfileAsync` for bug 017:
- Capture the old session into a local (`var oldSession = session;`) immediately after
  `var mySession = built.Session;`, before any `await`, and close/dispose that local instead of the
  shared variable.
- Guard the `session = mySession;` reassignment (and the following subscribe/UI setup) with
  `ReferenceEquals(switchCts, cts)`, disposing `mySession` without adopting it if already superseded.

## Tests to add
A TUI analog of `MainWindowSwitchProfileTests.SwitchProfileAsync_SupersededByAnotherSwitchBeforeItResolves_DoesNotStompTheNewerOne`:
a first switch to a connection that never resolves (e.g. a TCP listener nobody connects to) followed
by a second, real switch that succeeds - assert the second switch's session/UI state survives the
first switch's eventual (cancelled) resolution.

## Related
- [017](fixed/017-wpf-profile-switch-no-supersede.md) - the WPF analog of this exact bug, fixed first; this report exists because the TUI code it was ported from turned out to share both gaps.
