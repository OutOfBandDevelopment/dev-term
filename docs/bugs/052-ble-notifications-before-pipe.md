# 052: BLE notifications arriving right after subscribe are dropped

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
| **Confidence** | Confirmed |
| **Area** | DevTerm.Transports.Ble |
| **Found** | 2026-09-26 code review of `main` @ `42758db` (branch `dev/review-code`); static review, not yet reproduced |

## Where
`src/DevTerm.Transports.Ble/BleTransport.cs:60, 65, 77, 122-126`

## What happens
`adapter.ConnectAsync` enables notifications, but `_pipe` is created only afterwards, and `OnNotificationReceived`
returns early while `_pipe is null`.

## Failure scenario
A greeting a device sends as soon as notifications are enabled is lost.

## Suggested fix
Create the pipe before `ConnectAsync`.
