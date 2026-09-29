# 054: A cancelled BLE connect leaves its ValueChanged handler attached

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
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
