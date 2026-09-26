# 025: BLE Disconnect doesn't actually drop the link

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Open |
| **Confidence** | Plausible (code confirmed; effect from the WinRT docs) |
| **Area** | DevTerm.Transports.Ble.Windows |
| **Found** | 2026-09-26 code review of `main` @ `42758db` (branch `dev/review-code`); static review, not yet reproduced |

## Where
`src/DevTerm.Transports.Ble.Windows/WindowsBleAdapter.cs:46, 135-151`

## What happens
The `GattDeviceService` (`servicesResult.Services[0]`, and any other entries) is never disposed; `Cleanup` disposes
only the `BluetoothLEDevice`. Windows keeps the connection up while any `GattDeviceService` is alive.

## Failure scenario
After Disconnect the peripheral stays connected until garbage collection, so it doesn't advertise and other hosts
can't connect to it.

## Suggested fix
Keep the service in a field and dispose it in `Cleanup` and on the `ConnectAsync` failure path.

## Tests to add
Needs real hardware: after Disconnect the peripheral advertises again.
