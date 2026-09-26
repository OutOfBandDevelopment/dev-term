# 054: A cancelled BLE connect leaves its ValueChanged handler attached

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
| **Confidence** | Confirmed |
| **Area** | DevTerm.Transports.Ble.Windows |
| **Found** | 2026-09-26 code review of `main` @ `42758db` (branch `dev/review-code`); static review, not yet reproduced |

## Where
`src/DevTerm.Transports.Ble.Windows/WindowsBleAdapter.cs:63, 79`

## What happens
If cancellation or a WinRT exception hits the CCCD write, the outer catch disposes the device but never unsubscribes
`ValueChanged`.

## Suggested fix
Unsubscribe in the failure path (or subscribe only after the CCCD write succeeds).
