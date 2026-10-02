# 026: BLE writes aren't split to the packet size

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Fixed |
| **Confidence** | Plausible |
| **Area** | DevTerm.Transports.Ble.Windows |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Transports.Ble.Windows/WindowsBleAdapter.cs:96-103`

## What happens
The code prefers `WriteWithoutResponse` (which NUS RX supports) and sends the whole buffer in one write. A
without-response write is limited to one ATT packet: MTU - 3, which is 20 bytes on a peripheral with the default MTU
of 23.

## Failure scenario
Any command over the limit fails, or is truncated by the stack, on common NUS/HM-10-class devices.

## Suggested fix
Split writes by `GattSession.MaxPduSize - 3`, or use `WriteWithResponse` (long write) when the data is larger.

## Tests to add
A fake adapter asserting writes are chunked to the negotiated size; confirm on real hardware.

## Resolution
Fixed in `dev/fix-bugs` on 2026-09-26: new `BleWriteChunker.Chunk` (`src/DevTerm.Transports.Ble/BleWriteChunker.cs`,
platform-independent and unit-testable, unlike `WindowsBleAdapter` itself — see [025](025-ble-service-not-disposed.md)'s
Resolution on why that type can't be unit-tested directly) splits a write into successive chunks no larger than a
new `BleTransportOptions.MaxWriteChunkSize` (`src/DevTerm.Transports.Ble/BleTransportOptions.cs`), defaulting to
20 — the usable payload of one ATT packet at the default, unnegotiated 23-byte MTU. `WindowsBleAdapter.WriteAsync`
(`src/DevTerm.Transports.Ble.Windows/WindowsBleAdapter.cs`) now writes each chunk in turn instead of the whole
buffer in one call. Chose a configurable safe default over querying the peripheral's actual negotiated MTU
(`GattSession.MaxPduSize`) at connect time, since the fixed 20-byte default is already correct for any peripheral
that hasn't negotiated a larger MTU (the common case for NUS/HM-10-class devices), and a profile for a specific
device known to negotiate a larger one can raise `MaxWriteChunkSize` itself; querying the live session adds a
second async WinRT call and a new failure mode for comparatively little benefit given no BLE hardware is
available in this environment to verify a real negotiated-MTU path against. Confirmed with four new regression
tests (`BleWriteChunkerTests`, one tagged `BugRegression`) that exercise the chunking boundary directly — the
larger-than-MTU-truncation failure mode itself still needs real hardware to confirm, as the report says.

