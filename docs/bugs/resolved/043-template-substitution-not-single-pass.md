# 043: A typed value containing {OtherParam} is itself substituted

| | |
|---|---|
| **Severity** | Low |
| **Status** | Fixed |
| **Confidence** | Confirmed |
| **Area** | DevTerm.Devices.Scpi, DevTerm.DeviceManifests (control surfaces) |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.DeviceManifests/ManifestControlSurface.cs:292`, `src/DevTerm.Devices.Scpi/ScpiControlSurface.cs:150`

## What happens
Parameters are substituted one after another with `Replace`, so a value typed into an earlier parameter that contains
`{Later}` is replaced by the later parameter's value.

## Suggested fix
Substitute in a single pass (one regex over `{name}` tokens).

## Resolution
Fixed on 2026-09-26 on `dev/fix-bugs`: both `ManifestControlSurface.FormatTemplate` and
`ScpiControlSurface.BuildCommandText` now collect every parameter's formatted value into a
`Dictionary<string, string>` first, then substitute in exactly one `Regex.Replace` pass over the
original template (a `[GeneratedRegex(@"\{([A-Za-z_][A-Za-z0-9_.]*)\}")]` on each class, mirroring
the existing pattern in `DeviceManifestValidator.Placeholder()`) — a `MatchEvaluator` looks up each
match against the dictionary, so a substituted value's own `{name}`-shaped text is never re-scanned.
Regression tests:
`ScpiControlSurfaceTests.InvokeAsync_MultiParameterCommand_EarlierValueContainingALaterPlaceholder_IsNotItselfSubstituted`,
`ManifestControlSurfaceTests.InvokeAsync_MultiParameterCommand_EarlierValueContainingALaterPlaceholder_IsNotItselfSubstituted`.

Resolution recorded in commit `babdf36` (backfilled 2026-10-09 from git history).
