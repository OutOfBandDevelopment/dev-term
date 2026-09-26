# 044: A profile's numeric parameter limits can throw or force every value to 0

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
| **Confidence** | Confirmed (user profiles only) |
| **Area** | DevTerm.Devices.Scpi (ScpiControlSurface) |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Devices.Scpi/ScpiControlSurface.cs:163`

## What happens
`Math.Clamp(number, Min, Max)` throws `ArgumentException` when Min > Max. A Numeric parameter that omits both gets
0/0, so every value is sent as 0.

## Suggested fix
Treat missing limits as unbounded, and reject Min > Max when the profile loads.
