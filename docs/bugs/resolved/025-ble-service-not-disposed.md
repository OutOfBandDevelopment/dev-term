# 025: BLE Disconnect doesn't actually drop the link

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Fixed |
| **Confidence** | Plausible (code confirmed; effect from the WinRT docs) |
| **Area** | DevTerm.Transports.Ble.Windows |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

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

## Resolution
Fixed in `dev/fix-bugs` on 2026-09-26: `WindowsBleAdapter` now keeps the `GattDeviceService` it resolves in
`ConnectAsync` in a `_service` field, disposing it from `Cleanup` (alongside `_device`) and from `ConnectAsync`'s
own catch block if a later step in the same connect attempt fails — previously only `_device` was tracked/disposed
either place, so the `GattDeviceService` (and, per the WinRT docs, whatever kept the connection up while it lived)
was only ever reclaimed by garbage collection. No regression test: `Windows.Devices.Bluetooth`'s types are sealed
WinRT classes with no public constructors, so neither `GattDeviceService` nor `BluetoothLEDevice` can be faked for
a unit test, and there's no BLE hardware/profile currently configured in this environment's `devterm.runsettings`
or `launchSettings.json` to verify the real-world effect (the peripheral re-advertising) against. The fix itself —
storing and disposing the one resource the report identifies, in the same places `_device` already is — is a
direct, low-risk read of the existing code, not something narrowly dependent on the hardware-only claim.

Resolution recorded in commit `babdf36` (backfilled 2026-10-09 from git history).
