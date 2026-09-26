# 045: Device control surfaces parse numbers with throwing Parse

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
| **Confidence** | Confirmed |
| **Area** | DevTerm.Devices.Busylight, K8055, RadexOne |
| **Found** | 2026-09-26 code review of `main` @ `42758db` (branch `dev/review-code`); static review, not yet reproduced |

## Where
`BusylightControlSurface.cs:196` (`ParseByte`), `K8055ControlSurface.cs:377` (`ParseUInt16`), `RadexOneControlSurface.cs:153`

## What happens
`double.Parse` throws `FormatException` on blank or non-numeric text, and `(byte)NaN` is undefined. The renderers'
validation normally stops bad input before it gets here, so this only matters when that's bypassed.

## Suggested fix
Use `TryParse` and report a validation failure.
