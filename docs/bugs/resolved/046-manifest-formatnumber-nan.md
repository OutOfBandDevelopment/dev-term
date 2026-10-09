# 046: Manifest numbers can go out as NaN, or rounded past Max

| | |
|---|---|
| **Severity** | Low |
| **Status** | Fixed |
| **Confidence** | Confirmed |
| **Area** | DevTerm.DeviceManifests (ManifestControlSurface) |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.DeviceManifests/ManifestControlSurface.cs:300` (`FormatNumber`)

## What happens
`"NaN"`/`"Infinity"` parse and clamping doesn't remove NaN, so `NaN` can be sent. Rounding happens after clamping, so an
integer can exceed a fractional Max (Max 10.5, value 10.5, sent 11).

## Suggested fix
Reject non-finite values, and round before clamping.

## Resolution
Fixed on 2026-09-29 on `dev/fix-bugs`: the report's `:300` line reference was stale — the actual method
is `FormatNumber` at `src/DevTerm.DeviceManifests/ManifestControlSurface.cs:177-197`. Both halves of the
bug are fixed there:

- A new private `TryParseFinite` helper wraps `double.TryParse` with an added `double.IsFinite(number)`
  check, used for both the raw value and the `parameter.DefaultValue` fallback — `double.TryParse`
  accepts `"NaN"`/`"Infinity"`/`"-Infinity"` as literal text regardless of `NumberStyles`, and
  `Math.Clamp(double, double, double)` passes `NaN` through unchanged (every comparison against `NaN`
  is false), so a non-finite value previously reached the wire verbatim instead of being rejected. A
  value that fails `TryParseFinite` (and whose default also fails it) now falls back to `0`, matching
  the existing fallback for unparsable/blank text.
- `FormatNumber` now rounds (for an `IsInteger` parameter) *before* clamping, not after — rounding an
  already-in-range value can push it past a fractional bound (e.g. Max 10.5, value 10.5 rounds to
  11 > 10.5) if rounding happens after the clamp.

Regression tests:
`ManifestControlSurfaceTests.InvokeAsync_NonFiniteNumericValue_DoesNotSendNaNOrInfinityLiterally`
(fails against the pre-fix code — sent `VSET1:NaN\n` instead of falling back to the parameter's
default) and
`ManifestControlSurfaceTests.InvokeAsync_IntegerParameterRoundingToAFractionalMaximum_IsStillClampedToIt`
(fails against the pre-fix code — sent `TRIM11\n`, past the parameter's `Maximum` of `10.5`, instead of
`TRIM10.5\n`). Both pass with the fix; the existing round/clamp assertions in
`InvokeAsync_SendsTheFormattedTemplatePlusTheTerminator` are unaffected by the reordering. Full
`TestCategory=Unit` run green across the whole solution (no regressions).

Resolution recorded in commit `babdf36` (backfilled 2026-10-09 from git history).
