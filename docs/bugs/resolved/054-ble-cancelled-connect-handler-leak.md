# 054: A cancelled BLE connect leaves its ValueChanged handler attached

| | |
|---|---|
| **Severity** | Low |
| **Status** | Fixed |
| **Confidence** | Confirmed |
| **Area** | DevTerm.Transports.Ble.Windows |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Transports.Ble.Windows/WindowsBleAdapter.cs:63, 79`

## What happens
If cancellation or a WinRT exception hits the CCCD write, the outer catch disposes the device but never unsubscribes
`ValueChanged`.

## Suggested fix
Unsubscribe in the failure path (or subscribe only after the CCCD write succeeds).

## Resolution
Fixed on 2026-09-29 on `dev/fix-bugs`: `WindowsBleAdapter.ConnectAsync` now hoists `notifyCharacteristic`
out to the same scope as the already-hoisted `service`, and the outer `catch` unsubscribes
`ValueChanged` from it (when non-null) before disposing `service`/`device` and rethrowing - covering
every failure after the subscribe, not just the explicit `notifyStatus != Success` branch (which used
to unsubscribe inline and is now folded into the same catch-based cleanup, removing the duplication).

No automated regression test was added: `BluetoothLEDevice`/`GattDeviceService`/`GattCharacteristic`
are sealed WinRT-projected types with no public constructors and no interface seam in
`WindowsBleAdapter` (unlike `IBleAdapter` itself, which exists precisely so the rest of dev-term
doesn't depend on these types directly - see `DevTerm.Transports.Ble.Tests`, which mocks
`IBleAdapter`, not this class). There's no way to construct a fake `GattCharacteristic` whose
`WriteClientCharacteristicConfigurationDescriptorAsync` throws, so this path can only be verified
against real Bluetooth hardware. Confidence was already `Confirmed` by full code-path reading, and
the fix is small and directly addresses the described mechanism.
