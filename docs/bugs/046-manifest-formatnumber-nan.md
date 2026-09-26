# 046: Manifest numbers can go out as NaN, or rounded past Max

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
| **Confidence** | Confirmed |
| **Area** | DevTerm.DeviceManifests (ManifestControlSurface) |
| **Found** | 2026-09-26 code review of `main` @ `42758db` (branch `dev/review-code`); static review, not yet reproduced |

## Where
`src/DevTerm.DeviceManifests/ManifestControlSurface.cs:300` (`FormatNumber`)

## What happens
`"NaN"`/`"Infinity"` parse and clamping doesn't remove NaN, so `NaN` can be sent. Rounding happens after clamping, so an
integer can exceed a fractional Max (Max 10.5, value 10.5, sent 11).

## Suggested fix
Reject non-finite values, and round before clamping.
