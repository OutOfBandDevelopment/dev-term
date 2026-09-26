# 044: A profile's numeric parameter limits can throw or force every value to 0

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
| **Confidence** | Confirmed (user profiles only) |
| **Area** | DevTerm.Devices.Scpi (ScpiControlSurface) |
| **Found** | 2026-09-26 code review of `main` @ `42758db` (branch `dev/review-code`); static review, not yet reproduced |

## Where
`src/DevTerm.Devices.Scpi/ScpiControlSurface.cs:163`

## What happens
`Math.Clamp(number, Min, Max)` throws `ArgumentException` when Min > Max. A Numeric parameter that omits both gets
0/0, so every value is sent as 0.

## Suggested fix
Treat missing limits as unbounded, and reject Min > Max when the profile loads.
