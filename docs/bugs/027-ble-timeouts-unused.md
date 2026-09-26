# 027: BLE connect and write timeouts are documented but never used

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Open |
| **Confidence** | Confirmed |
| **Area** | DevTerm.Transports.Ble, DevTerm.Configuration |
| **Found** | 2026-09-26 code review of `main` @ `42758db` (branch `dev/review-code`); static review, not yet reproduced |

## Where
`src/DevTerm.Transports.Ble/BleTransportOptions.cs:25, 28`

## What happens
`ConnectTimeoutMs` and `WriteTimeoutMs` are documented as bounding a hung connect or write with a `TimeoutException`.
Nothing reads them, and `AddDevTermFrontEnd` doesn't bind them from `CliOptions`.

## Failure scenario
A peripheral that stops responding hangs Connect or a write indefinitely.

## Suggested fix
Wrap `ConnectAsync`/`WriteAsync` in a linked `CancelAfter` and bind the options; or remove them.

## Tests to add
A fake adapter that never completes: connect and write time out.
