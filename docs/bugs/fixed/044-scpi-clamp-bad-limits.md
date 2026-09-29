# 044: A profile's numeric parameter limits can throw or force every value to 0

| | |
|---|---|
| **Severity** | Low |
| **Status** | Fixed |
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

## Resolution
Fixed on 2026-09-29 on `dev/fix-bugs`: `ScpiParameterDefinition.Minimum`/`Maximum`
(`src/DevTerm.Devices.Scpi/ScpiParameterDefinition.cs`) are now nullable `double?` (null = unbounded),
mirroring `DeviceManifest.CommandParameter.Minimum`/`Maximum`. `ScpiControlSurface.FormatNumeric`'s
clamp now reads `Math.Clamp(number, parameter.Minimum ?? double.NegativeInfinity, parameter.Maximum ??
double.PositiveInfinity)`, mirroring `ManifestControlSurface.FormatNumber`, so an omitted limit no
longer forces every value to 0. `ScpiUiDefinitionBuilder.BuildParameterControl`'s `NumericControl`/
`SliderControl`/default-value reads fall back to `?? 0` to keep today's widget-rendering behavior for
an omitted bound (`ValueConstraint.Minimum`/`Maximum` were already nullable and needed no change).
`ScpiProfileCatalog.LoadFrom` now rejects (skips, adds to `errors`, same as an unreadable/malformed
file) any loaded profile with a Numeric parameter whose `Minimum` and `Maximum` are both set with
`Minimum > Maximum`, via a new `FindBadNumericLimits` helper — `Math.Clamp` never sees that
combination at all now. Regression tests:
`ScpiControlSurfaceTests.InvokeAsync_NumericParameterWithNoLimitsSet_IsNotForcedToZero`,
`ScpiProfileCatalogTests.Load_WithANumericParameterWhereMinimumExceedsMaximum_SkipsThatProfileAndReportsIt`.
