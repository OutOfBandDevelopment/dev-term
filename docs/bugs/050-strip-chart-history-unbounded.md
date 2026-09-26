# 050: A manifest can make strip-chart history grow without limit

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
| **Confidence** | Confirmed |
| **Area** | DevTerm.UiDefinitions / DeviceManifests (LiveDisplayState) |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`LiveDisplayState.cs` (`StripChartState.Capacity`)

## What happens
`HistoryLength` comes from the manifest with no upper bound.

## Suggested fix
Clamp it (for example to 10,000 points) and have the validator warn.
