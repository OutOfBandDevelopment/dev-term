# 057: A serial read may never notice an unplugged adapter

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
| **Confidence** | Plausible |
| **Area** | DevTerm.Transports.Serial |
| **Found** | 2026-09-26 code review of `main` @ `42758db` (branch `dev/review-code`); static review, not yet reproduced |

## Where
`src/DevTerm.Transports.Serial/SerialPortReadStream.cs:31-47`

## What happens
On a USB-serial unplug, `DataReceived` never fires, so the pump waits forever; the loss only surfaces on the next write.

## Suggested fix
Also listen for `SerialPort.ErrorReceived`/`PinChanged`, or poll `IsOpen` on an interval while waiting. Verify
against real hardware first (CLAUDE.md's serial cancellation note).
