# 057: A serial read may never notice an unplugged adapter

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
| **Confidence** | Plausible |
| **Area** | DevTerm.Transports.Serial |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Transports.Serial/SerialPortReadStream.cs:31-47`

## What happens
On a USB-serial unplug, `DataReceived` never fires, so the pump waits forever; the loss only surfaces on the next write.

## Suggested fix
Also listen for `SerialPort.ErrorReceived`/`PinChanged`, or poll `IsOpen` on an interval while waiting. Verify
against real hardware first (CLAUDE.md's serial cancellation note).
