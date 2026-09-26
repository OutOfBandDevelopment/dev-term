# 043: A typed value containing {OtherParam} is itself substituted

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
| **Confidence** | Confirmed |
| **Area** | DevTerm.Devices.Scpi, DevTerm.DeviceManifests (control surfaces) |
| **Found** | 2026-09-26 code review of `main` @ `42758db` (branch `dev/review-code`); static review, not yet reproduced |

## Where
`src/DevTerm.DeviceManifests/ManifestControlSurface.cs:292`, `src/DevTerm.Devices.Scpi/ScpiControlSurface.cs:150`

## What happens
Parameters are substituted one after another with `Replace`, so a value typed into an earlier parameter that contains
`{Later}` is replaced by the later parameter's value.

## Suggested fix
Substitute in a single pass (one regex over `{name}` tokens).
