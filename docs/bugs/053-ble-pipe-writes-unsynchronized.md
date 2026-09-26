# 053: BLE pipe writes from Bluetooth threads aren't synchronized

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
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
