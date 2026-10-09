# 031: The TUI output pane has no backpressure

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Fixed |
| **Confidence** | Plausible |
| **Area** | TUI (TuiMode) |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Console/TuiMode.cs:207-219, 528`

## What happens
Every `Session.Output` does an `app.Invoke(...)`, and each invoke rebuilds `string.Join('\n', outputLines)` and resets
`Editor.Text` (wrap plus highlighting). WPF's synchronous `Dispatcher.Invoke` at least applies backpressure.

## Failure scenario
A streaming device queues invokes faster than they drain; memory and lag grow without bound.

## Suggested fix
Queue lines in a concurrent queue and schedule a single pending `Invoke` that drains it in batches.

## Tests to add
Many outputs in a burst produce a bounded number of UI updates.

## Resolution
Fixed in `dev/fix-bugs` on 2026-09-26. Confidence was `Plausible`: true unbounded-memory-growth is a timing-
dependent symptom against the real Terminal.Gui runtime, which exposes no API to inspect its internal pending-invoke
count, so it was not reproduced empirically. Instead the mechanism was confirmed by reading the code: `Session.PumpAsync`
(`src/DevTerm.Core/Sessions/Session.cs`) raises `Output?.Invoke` synchronously, once per rendered `PresenterOutput`, on
its own background read-loop thread - a single transport read that yields several presenter outputs raises several
synchronous events in a row. `TuiMode`'s handler answered each with its own `app.Invoke(...)`, which (per Terminal.Gui
v2.5.0, see CLAUDE.md) queues without blocking the calling thread, unlike WPF's synchronous `Dispatcher.Invoke` - so a
fast stream queues one UI-thread closure per line with no backpressure at all, confirming the report's diagnosis.

The fix extracts the suggested single-pending-invoke queue as its own class, `BatchedOutputQueue`
(`src/DevTerm.Console/BatchedOutputQueue.cs`): `Enqueue` (called from the read-loop thread) appends under a lock and
calls the schedule callback only the first time a line arrives with no drain already pending; `Drain` (called from the
UI thread inside that scheduled `app.Invoke`) hands every line queued since the last drain to the caller in one call
and clears the pending flag so the next `Enqueue` schedules a fresh one. `TuiMode`'s `AppendOutput` now just calls
`outputQueue.Enqueue(line)`; the single `app.Invoke` callback drains the batch, appends it to `outputLines`, trims to
`_maxOutputLines`, and rebuilds `Editor.Text` once per drain instead of once per line.

Because the class has no Terminal.Gui dependency, its coalescing behavior (not the original growth symptom) is
covered directly and deterministically: `BatchedOutputQueueTests.Enqueue_ABurstOfLinesBeforeTheDrainRuns_SchedulesOnlyOneDrain`
(tagged `BugRegression`) enqueues 1000 lines before any drain runs and asserts the schedule callback fired exactly
once; `Enqueue_AfterADrainCompletes_SchedulesAFreshDrainForTheNextBurst` (also `BugRegression`) confirms a completed
drain re-arms scheduling for the next burst. `Drain_HandsEveryPendingLineToApplyInOneCall_InOrder` and
`Drain_WithNothingPending_DoesNotCallApply` cover ordering and the empty case. Confirmed the pre-fix state was a
compile failure (`CS0246: The type or namespace name 'BatchedOutputQueue' could not be found`) via a targeted
`git stash`, consistent with introducing a brand-new type. Full `DevTerm.Console.Tests` suite and a full solution
build (0 warnings/0 errors) pass after the fix; one unrelated pre-existing failure
(`TuiToolWindowLayoutTests.Playback_PartWayThroughWithANote`, a `PlaybackMode` layout/rendering test untouched by this
fix) was confirmed present both before and after via the same stash technique.

Resolution recorded in commit `babdf36` (backfilled 2026-10-09 from git history).
