# 024: A comma inside a text parameter shifts every later parameter

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Open |
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
