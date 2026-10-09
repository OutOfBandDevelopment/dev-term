# 052: BLE notifications arriving right after subscribe are dropped

| | |
|---|---|
| **Severity** | Low |
| **Status** | Fixed |
| **Confidence** | Confirmed |
| **Area** | DevTerm.Transports.Ble |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Transports.Ble/BleTransport.cs:60, 65, 77, 122-126`

## What happens
`adapter.ConnectAsync` enables notifications, but `_pipe` is created only afterwards, and `OnNotificationReceived`
returns early while `_pipe is null`.

## Failure scenario
A greeting a device sends as soon as notifications are enabled is lost.

## Suggested fix
Create the pipe before `ConnectAsync`.

## Resolution
Fixed on 2026-09-29 on `dev/fix-bugs`: `BleTransport.OpenAsync`
(`src/DevTerm.Transports.Ble/BleTransport.cs`) now creates `_pipe` right after subscribing to
`adapter.NotificationReceived`/`Disconnected` and before calling `adapter.ConnectAsync`, exactly as
suggested, instead of only afterwards — so a notification arriving while `ConnectAsync` is still
running (some devices greet immediately once notifications are enabled, which happens inside that
call) has somewhere to land instead of being silently dropped by `OnNotificationReceived`'s
`_pipe is null` guard. The catch block that transitions to `Faulted` on a failed connect now also
resets `_pipe = null`, so a failed open doesn't leave a pipe with no reader ever draining it.

Regression test: `BleTransportTests.NotificationArrivingDuringConnectAsync_IsNotDropped` (a fake
adapter raises `NotificationReceived` from inside its `ConnectAsync` mock before completing; fails
against the pre-fix code — the read from `transport.Input` times out because the notification was
dropped). Full `TestCategory=Unit` run green across the whole solution (no regressions).

Resolution recorded in commit `babdf36` (backfilled 2026-10-09 from git history).
