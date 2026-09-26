# 055: HID read thread can crash the process; Close blocks the calling thread

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
| **Confidence** | Plausible (thread crash); confirmed (blocking) |
| **Area** | DevTerm.Transports.Hid |
| **Found** | 2026-09-26 code review of `main` @ `42758db` (branch `dev/review-code`); static review, not yet reproduced |

## Where
`src/DevTerm.Transports.Hid/HidReadStream.cs:50`; `SystemHidDevice.Close()`

## What happens
- `GetMaxInputReportLength()` runs before the `try` on a raw `Thread`, so any exception there kills the process.
- `SystemHidDevice.Close()` calls `Stop()`, which joins the read thread (up to `ReadTimeoutMs` + 1 s, about 2 s) on
  whichever thread called `CloseAsync`, usually the UI thread. `OpenAsync` also enumerates and opens synchronously.

## Suggested fix
Move the call inside the thread's `try`; run the blocking close/open work off the UI thread.
