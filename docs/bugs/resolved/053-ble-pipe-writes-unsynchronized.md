# 053: BLE pipe writes from Bluetooth threads aren't synchronized

| | |
|---|---|
| **Severity** | Low |
| **Status** | Fixed |
| **Confidence** | Plausible |
| **Area** | DevTerm.Transports.Ble |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Transports.Ble/BleTransport.cs:128, 131-142, 151`

## What happens
Each notification starts a fire-and-forget `WriteToPipeAsync`, and `OnAdapterDisconnected`/`CloseAsync` call
`Writer.Complete()` from other threads. `PipeWriter` is single-writer.

## Failure scenario
Under backpressure the next notification writes concurrently; the `InvalidOperationException` is swallowed, so data is
lost silently and ordering isn't guaranteed.

## Suggested fix
Serialize writes (a lock or a `Channel`) and complete the writer under the same lock.

## Resolution
Fixed on 2026-09-29 on `dev/fix-bugs`: reproduced first, per this report's `Plausible` confidence, with a test
that raises two notifications back-to-back where the first exceeds the `Pipe`'s default 64 KiB
pause-writer threshold (so its `WriteAsync` is still flushing, unread, when the second arrives).
The reproduction confirmed the mechanism this report predicted and found it's worse than described:
the second `WriteAsync` throws `InvalidOperationException: Concurrent reads or writes are not
supported` (swallowed by `WriteToPipeAsync`'s existing catch, exactly as predicted), but the
concurrent access also corrupts the `Pipe`'s internal state badly enough that the *reader* side
stops working afterward too - a subsequent `PipeReader.ReadAsync` throws `InvalidOperationException:
Reading is not allowed after reader was completed`, even though nothing ever called
`PipeReader.Complete()`. So the real-world effect isn't just "the notification's bytes are dropped" -
it's "the whole BLE session dies with an unhandled framework exception on some later read," with no
clean disconnect reported.

Fixed with the suggested lock approach: `BleTransport` now has a `SemaphoreSlim _writeGate` that
`WriteToPipeAsync` acquires around its `writer.WriteAsync(data)` call, and a new `CompleteWriter()`
helper (used by both `OnAdapterDisconnected` and `CloseAsync` in place of the old direct
`_pipe?.Writer.Complete()` calls) acquires the same gate before completing the writer - so a
notification's write and a disconnect's completion can never race each other or another write.
Since notifications arrive synchronously and in order via `OnNotificationReceived`, and each
fire-and-forget `WriteToPipeAsync` call's first await is acquiring the gate, this also preserves
receipt order: a second notification's write now waits for the first's full `WriteAsync` (including
its flush) to finish, rather than racing it.

Regression test: `BleTransportTests.NotificationArrivingWhileAPriorWriteIsStillFlushing_DoesNotLoseData`.
