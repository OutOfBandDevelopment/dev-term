# 026: BLE writes aren't split to the packet size

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Open |
| **Confidence** | Plausible |
| **Area** | DevTerm.Transports.Ble.Windows |
| **Found** | 2026-09-26 code review of `main` @ `42758db` (branch `dev/review-code`); static review, not yet reproduced |

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
