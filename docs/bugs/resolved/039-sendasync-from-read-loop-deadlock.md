# 039: Calling SendAsync from the read-loop thread deadlocks if the send fails

| | |
|---|---|
| **Severity** | Low |
| **Status** | Fixed |
| **Confidence** | Confirmed from code; no current caller |
| **Area** | DevTerm.Core (Session) |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Core/Sessions/Session.cs` (`SendAsync` fault path)

## What happens
If a send fails, `FaultAsync` is awaited inline, and `StopAsync` awaits the read loop, which is the caller. No caller
does this today (ZoomH4n's TCS uses `RunContinuationsAsynchronously`), but an `Output` handler or presenter that
replies synchronously would.

## Suggested fix
Make the send-fault path non-awaiting (`Task.Run`, as the read-loop fault already is), or document the rule.

## Resolution
Fixed on 2026-09-26: deferring `SendAsync`'s fault path universally (`Task.Run` before `FaultAsync`) was tried
first and reverted - it broke the documented "a send's catch only reports when `session.State` is still `Open`"
guarantee (`TuiModeErrorHandlingTests.SendAsync_DeviceFailure_DisconnectsAndLeavesReportingToTheDisconnectedHandler`),
which relies on the fault path synchronously closing the transport before the exception reaches a normal caller's
catch block. Instead, `Session` now tracks reentrancy directly: a new `AsyncLocal<bool> _onReadLoop` field is set
at the top of `PumpAsync` and stays `true` for its entire logical call chain, including a synchronous `Output`
handler that calls `SendAsync` inline - but not for an unrelated, concurrent caller (e.g. a normal `CloseAsync`)
even while `PumpAsync` is itself still blocked in a read, since `AsyncLocal` only flows down the chain it was set
on. The one call that legitimately needs to *not* inherit `true` - `PumpAsync`'s own natural end-of-loop dispatch
to `FaultAsync` via `Task.Run` - is wrapped in `ExecutionContext.SuppressFlow()` so that task starts fresh.
`StopAsync` checks `_onReadLoop.Value` (replacing an earlier, incorrect `Task.CurrentId == readLoop.Id` attempt,
which never matched: `Task.Run(Func<Task>)` returns an unwrapped proxy task with a different `Id` than the actual
async state machine executing `PumpAsync`'s body) and throws `InvalidOperationException` instead of awaiting the
read loop, which would otherwise deadlock on itself. Regression test:
`SessionTests.SendAsync_CalledSynchronouslyFromTheReadLoopsOutputHandler_FailsFastInsteadOfDeadlockingWhenTheSendFails`.

Resolution recorded in commit `babdf36` (backfilled 2026-10-09 from git history).
