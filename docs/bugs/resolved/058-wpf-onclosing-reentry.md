# 058: Two close requests during a slow cleanup run OnClosing twice

| | |
|---|---|
| **Severity** | Low |
| **Status** | Fixed |
| **Confidence** | Plausible |
| **Area** | WPF (MainWindow) |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Wpf/MainWindow.xaml.cs:672-706`

## What happens
`_closeConfirmed` is set only after the awaits. A second Ctrl+Q or Alt+F4 while `CloseAsync` is pending re-enters:
the monitor is disposed twice, the session closed twice, and `Close()` called twice; the second may throw inside an
`async void` handler.

## Suggested fix
A `_closing` flag that sets `e.Cancel = true` and returns while cleanup is running.

## Resolution
Fixed in `src/DevTerm.Wpf/MainWindow.xaml.cs` (`OnClosing`) on 2026-09-29, per the suggested fix: a
new `_closing` flag is set before the method's first genuine `await` and checked at the top, so a
second close request arriving while the first is still awaiting `Session.CloseAsync`/`DisposeAsync`
just cancels itself and returns instead of re-running the cleanup body.

Reproducing this first (per the fixing lifecycle for a `Plausible` report) surfaced a more precise
picture than the report's wording: `Window.Closing` genuinely re-fires and re-enters `OnClosing`
while the first call's cleanup is still in flight (confirmed via a new test-only
`internal int ClosingCleanupRunCount` counter — 2 runs before the fix, 1 after), so the reentrancy
itself is real. But the report's specific claim that "the second may throw inside an `async void`
handler" did **not** reproduce, deterministically, across 30 stress-test iterations against the
pre-fix code — every step the reentrant call repeats (`StreamMonitor.Dispose()`, `StopLogging`,
`Session.CloseAsync`/`DisposeAsync`) already happens to be idempotent/guarded against a second call,
so the redundant run completed harmlessly rather than throwing. The underlying defect (uncontrolled
reentrancy relying on that idempotency by accident, rather than by design) is real and is what this
fix removes; the crash symptom specifically was not observed and is not what the regression test
asserts against.

`StaTestRunner` (`tests/DevTerm.Wpf.Tests/StaTestRunner.cs`) was extended with a
`Dispatcher.UnhandledException` handler so a future test can safely probe an `async void` handler's
failure path without risking a process-crashing unhandled exception escaping the dispatcher loop.

Regression test:
`MainWindowTests.Close_CalledAgainWhileTheFirstCloseIsStillCleaningUp_ClosesOnceWithoutThrowing`
(fails against the pre-fix code — `ClosingCleanupRunCount` is 2, not 1 — confirmed by temporarily
neutralizing the `_closing` guard in place, since the fix and its supporting
`ClosingCleanupRunCount` test hook live in the same file and couldn't be cleanly separated via
`git stash`). Full `TestCategory=Unit` run green across the whole solution (no regressions); full
solution `dotnet build` clean (0 warnings, 0 errors).
