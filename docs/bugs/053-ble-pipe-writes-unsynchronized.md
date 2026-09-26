# 053: BLE pipe writes from Bluetooth threads aren't synchronized

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
| **Confidence** | Plausible |
| **Area** | DevTerm.Transports.Ble |
| **Found** | 2026-09-26 code review of `main` @ `42758db` (branch `dev/review-code`); static review, not yet reproduced |

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
