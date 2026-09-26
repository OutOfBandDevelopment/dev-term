# 039: Calling SendAsync from the read-loop thread deadlocks if the send fails

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
| **Confidence** | Confirmed from code; no current caller |
| **Area** | DevTerm.Core (Session) |
| **Found** | 2026-09-26 code review of `main` @ `42758db` (branch `dev/review-code`); static review, not yet reproduced |

## Where
`src/DevTerm.Core/Sessions/Session.cs` (`SendAsync` fault path)

## What happens
If a send fails, `FaultAsync` is awaited inline, and `StopAsync` awaits the read loop, which is the caller. No caller
does this today (ZoomH4n's TCS uses `RunContinuationsAsynchronously`), but an `Output` handler or presenter that
replies synchronously would.

## Suggested fix
Make the send-fault path non-awaiting (`Task.Run`, as the read-loop fault already is), or document the rule.
