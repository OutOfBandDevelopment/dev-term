# 045: Device control surfaces parse numbers with throwing Parse

| | |
|---|---|
| **Severity** | Low |
| **Status** | Fixed |
| **Confidence** | Confirmed |
| **Area** | DevTerm.Devices.Busylight, K8055, RadexOne |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`BusylightControlSurface.cs:196` (`ParseByte`), `K8055ControlSurface.cs:377` (`ParseUInt16`), `RadexOneControlSurface.cs:153`

## What happens
`double.Parse` throws `FormatException` on blank or non-numeric text, and `(byte)NaN` is undefined. The renderers'
validation normally stops bad input before it gets here, so this only matters when that's bypassed.

## Suggested fix
Use `TryParse` and report a validation failure.

## Resolution
Fixed on 2026-09-29 on `dev/fix-bugs`: the report's cited locations were stale (found at an earlier
commit) — the actual throwing helpers at the fix commit are
`src/DevTerm.Devices.Busylight/BusylightControlSurface.cs`'s `ParseByte`,
`src/DevTerm.Devices.K8055/K8055ControlSurface.cs`'s `ParseByte`, and
`src/DevTerm.Devices.RadexOne/RadexOneControlSurface.cs`'s `ParseUInt16` — all three built on a bare
`double.Parse(value ?? "0", CultureInfo.InvariantCulture)`, throwing `FormatException` for blank or
non-numeric text. Critically, `K8055ControlSurface.InvokeAsync` had no protection at all around this:
its own `PreviewCommand` already wrapped the shared `Plan` method in a
`catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)`, but
`InvokeAsync` did not, so bad numeric text bypassing UI validation threw an *unhandled* exception
straight out of `InvokeAsync` for this device specifically (Busylight/RadexOne route setters through
their own switch in `InvokeAsync` directly, with the same missing protection).

Rather than inventing a new validation-reporting channel on `IControlSurface` (whose `InvokeAsync`
returns a plain `Task`, with no result/error channel to report through), all three `ParseByte`/
`ParseUInt16` helpers now mirror the already-shipped, non-throwing pattern in
`ManifestControlSurface.FormatNumber` (`src/DevTerm.DeviceManifests/ManifestControlSurface.cs`):
`double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0`
before the existing `Math.Clamp`. Unparsable text now falls back to `0` instead of throwing, for both
`InvokeAsync` and `PreviewCommand` alike — the two paths are symmetric again. A pre-existing
K8055 test (`PreviewCommand_UnknownCommandOrBadValue_IsNull`) asserted that a bad value previewed as
`null`, which was really this same throw being caught by `PreviewCommand`'s try/catch, not a
deliberate "reject bad values" contract — it's now split into
`PreviewCommand_UnknownCommand_IsNull` (still null — an unknown command id still throws
`ArgumentException`) and `PreviewCommand_NonNumericValue_FallsBackToZeroRatherThanNull` (documents the
new, non-throwing behavior).

Regression tests: `K8055ControlSurfaceTests.InvokeAsync_AnalogOutWithNonNumericValue_DoesNotThrow_AndFallsBackToZero`,
`BusylightControlSurfaceTests.InvokeAsync_OnMsWithNonNumericValue_DoesNotThrow_AndFallsBackToZero`,
`RadexOneControlSurfaceTests.InvokeAsync_ThresholdWithNonNumericValue_DoesNotThrow_AndFallsBackToZero`.
