# 024: A comma inside a text parameter shifts every later parameter

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Fixed |
| **Confidence** | Confirmed |
| **Area** | DevTerm.Devices.Scpi, DevTerm.DeviceManifests (control surfaces) |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Devices.Scpi/ScpiControlSurface.cs:142`, `src/DevTerm.DeviceManifests/ManifestControlSurface.cs:287`

## What happens
Parameter values are joined with `,` by the button (`ParameterFieldIds`) and split back with `(value ?? "").Split(',')`.

Separately, the SCPI surface uses an empty text field as-is instead of the parameter's `DefaultValue`; the manifest
surface does use the default.

## Failure scenario
A text parameter `DISP:TEXT '{Msg}'` with `a,b` sends a truncated command, and the next parameter receives `b`.

## Suggested fix
Pass parameter values as a list (or escape commas) between the button and the surface.

## Tests to add
A text parameter containing a comma is sent intact; the next parameter keeps its own value.

## Resolution
Fixed in `dev/fix-bugs` on 2026-09-26: introduced a shared `DevTerm.Core.Control.ParameterValueList`
with backslash-escaping `Join`/`Split` methods (escapes a literal `,` or `\` in any one value before
joining, so a real separator comma can never be confused with an escaped one), and wired it into
every join site (`ControlPanelMode.RawParameters`/`TryReadParameters`,
`ControlPanelWindow.xaml.cs`'s two analogous sites) and split site
(`ScpiControlSurface.BuildCommandText`, `ManifestControlSurface.FormatTemplate`) instead of the raw
`string.Join(',', ...)`/`.Split(',')` pair. `ManifestControlSurface.FormatTemplate`'s prior
single-parameter shortcut (skip splitting entirely when `command.Parameters.Count == 1`) is no longer
needed or present — `ManifestUiBuilder` always routes a parameterized command's value through a
`ButtonControl.ParameterFieldIds` button (even for a single parameter), so every parameterized
command's value already arrives pre-escaped from `Join`, making a uniform `Split` call correct for
any parameter count on both surfaces. Also fixed the secondary defect the report calls out:
`ScpiControlSurface.BuildCommandText` now falls back to `parameter.DefaultValue` for an empty (but
present) split segment, matching `ManifestControlSurface.FormatTemplate`'s existing behavior.
Regression tests:
`ParameterValueListTests.Join_ThenSplit_RoundTripsAValueContainingALiteralComma`,
`ScpiControlSurfaceTests.InvokeAsync_MultiParameterCommand_TextValueContainingAComma_SurvivesIntact`,
`ScpiControlSurfaceTests.InvokeAsync_MultiParameterCommand_EmptySecondValue_FallsBackToDefault`,
`ManifestControlSurfaceTests.InvokeAsync_MultiParameterCommand_TextValueContainingAComma_SurvivesIntact`.
