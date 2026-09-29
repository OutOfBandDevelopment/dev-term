# 050: A manifest can make strip-chart history grow without limit

| | |
|---|---|
| **Severity** | Low |
| **Status** | Fixed |
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

## Resolution
Fixed on 2026-09-29 on `dev/fix-bugs`: `StripChartState.Capacity`
(`src/DevTerm.UiDefinitions/LiveDisplayState.cs`) now reads
`Math.Clamp(Control.HistoryLength, 1, MaxCapacity)` against a new public `MaxCapacity = 10_000`
constant, instead of only enforcing a floor (`Math.Max(Control.HistoryLength, 1)`) with no ceiling.
`DeviceManifestValidator.ValidateUi` (`src/DevTerm.DeviceManifests/DeviceManifestValidator.cs`) now
adds a warning (not an error — the value is clamped, not rejected) when a `StripChartControl`'s
`HistoryLength` exceeds `StripChartState.MaxCapacity`, matching the existing "loads and opens but
would misbehave" warning convention documented on `DeviceManifestValidator`'s own doc comment.

Regression tests: `ChartControlsTests.StripChart_HistoryLengthAboveTheHardMaximum_IsClampedRatherThanUnbounded`
and `DeviceManifestTests.Validate_StripChartHistoryLengthAboveTheHardMaximum_Warns` (both fail against
the pre-fix code, confirmed via a temporary revert of just the clamp/warning behavior — the
`MaxCapacity` constant itself was kept in place since the tests reference it directly). Full
`TestCategory=Unit` run green across the whole solution (no regressions).
