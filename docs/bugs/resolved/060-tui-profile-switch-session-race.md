# 060: A TUI profile switch can close/reassign the wrong session under overlap

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Fixed |
| **Confidence** | Confirmed |
| **Area** | TUI (Console) |
| **Created** | 2026-09-26 |
| **Found at commit** | `d5729d15f5be4945f94dd88cceb4c69bed1f101f` (`dev/fix-bugs`) |
| **Found by** | Code review while fixing bug [017](017-wpf-profile-switch-no-supersede.md) (WPF) |

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
- [017](017-wpf-profile-switch-no-supersede.md) - the WPF analog of this exact bug, fixed first; this report exists because the TUI code it was ported from turned out to share both gaps.

## Resolution
Fixed on 2026-09-29 in `src/DevTerm.Console/TuiMode.cs`'s `SwitchProfileAsync`, porting bug 017's WPF
fix verbatim: `var oldSession = session;` is now captured immediately after `var mySession =
built.Session;`, before any `await`, and that local (not the shared `session` field) is what gets
unsubscribed/closed/disposed as the outgoing session; the `session = mySession;` reassignment (and
the subscribe/UI-adoption that follows it) is now guarded by `ReferenceEquals(switchCts, cts)`,
disposing `mySession` without adopting it when a newer switch has already superseded this one.

**Regression test**: `TuiModeSwitchProfileTests.SwitchProfileAsync_SupersededByAnotherSwitchBeforeItResolves_DoesNotStompTheNewerOne`
(the test this report's "Tests to add" section named, now tagged `BugRegression`) continues to pass
with the fix applied and exercises the same supersede scenario the fix targets.

**Testability constraint on a more exact test**: that test alone does not distinguish pre-fix from
post-fix code - it was run against both (via a temporary `git stash` of the fix) and passed either
way, because it only exercises the later `OpenAsync`-catch-block supersede guard, which was already
correct before this fix. The actual defect fixed here is a narrower window: `Session._lifecycleLock`
(a `SemaphoreSlim(1,1)` in `src/DevTerm.Core/Sessions/Session.cs`) serializes `OpenAsync`/`CloseAsync`
per `Session` instance, and `StopAsync` unconditionally calls `_transport.CloseAsync()` on every
`CloseAsync()`/`DisposeAsync()` invocation - so tearing down one old session during a switch acquires
and releases that lock twice (once via `CloseAsync()`, once via the following `DisposeAsync()`), with
a real gap between the two where a second, already-queued switch's own close/open on that same old
session could interleave. A test built to force this window by gating the old session's
`FakeTransport.CloseAsync()` and starting a second overlapping switch was attempted and reliably
deadlocked instead of reproducing a race: both switches target the same not-yet-reassigned old
`Session` object, so the second switch's own teardown call blocks on `_lifecycleLock` behind the
first (held at the artificial gate), a circular wait with no way out short of adding test-only hooks
into `Session.cs` production code - disproportionate for this fix. No such test was added; the fix's
correctness instead rests on the same code-level reasoning that confirmed the bug (structural
equivalence to bug 017's WPF fix, independently verified there against a deterministic test in a
codebase where the old/new sessions are two genuinely distinct objects with no shared lock), plus the
full `TestCategory=Unit` suite (1291 tests) passing unchanged with the fix applied.

Resolution recorded in commit `babdf36` (backfilled 2026-10-09 from git history).
