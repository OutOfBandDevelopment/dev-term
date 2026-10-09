# 023: One malformed SCPI profile file breaks all SCPI features for the rest of the run

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Fixed |
| **Confidence** | Confirmed |
| **Area** | DevTerm.Devices.Scpi (ScpiProfileCatalog) |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Devices.Scpi/ScpiProfileCatalog.cs:42, 95-102`

## What happens
`All` is a static-initialized property, and `LoadFrom` calls `JsonSerializer.Deserialize` with no try/catch.

## Failure scenario
A bad file in `~/.dev-term/scpi-profiles` throws `TypeInitializationException` on every later access, for the life of
the process: the SCPI picker, auto-detect and panels all fail.

## Suggested fix
Catch `JsonException`/`IOException` per file, skip the file, and report it.

## Tests to add
A catalog folder with one malformed file still loads the rest and reports the bad one.

## Resolution
Fixed in `dev/fix-bugs` on 2026-09-26: `ScpiProfileCatalog.LoadFrom` now catches `JsonException`,
`IOException`, and `UnauthorizedAccessException` per file (the same exception set
`ManifestCatalog.NameOf` already uses for device manifests), skipping the bad file and appending a
`"<filename>: <message>"` entry to a new `errors` list instead of letting the exception propagate out
of `All`'s static field initializer. A new `Load(string baseDirectory, out List<string> errors)`
overload exposes this to callers/tests; the existing single-argument `Load(string baseDirectory)` is
now a thin wrapper (`=> Load(baseDirectory, out _)`) so its four existing callers are unaffected. The
real, static-initializer-backed errors are exposed via a new `ScpiProfileCatalog.LoadErrors` property
(nothing surfaces it in the UI yet). Regression test:
`ScpiProfileCatalogTests.Load_WithOneMalformedProfileFile_StillLoadsTheRestAndReportsTheBadOne`.

Resolution recorded in commit `babdf36` (backfilled 2026-10-09 from git history).
