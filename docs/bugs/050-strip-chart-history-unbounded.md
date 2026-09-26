# 050: A manifest can make strip-chart history grow without limit

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
| **Confidence** | Confirmed |
| **Area** | DevTerm.UiDefinitions / DeviceManifests (LiveDisplayState) |
| **Found** | 2026-09-26 code review of `main` @ `42758db` (branch `dev/review-code`); static review, not yet reproduced |

## Where
`LiveDisplayState.cs` (`StripChartState.Capacity`)

## What happens
`HistoryLength` comes from the manifest with no upper bound.

## Suggested fix
Clamp it (for example to 10,000 points) and have the validator warn.
