# 023: One malformed SCPI profile file breaks all SCPI features for the rest of the run

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Open |
| **Confidence** | Confirmed |
| **Area** | DevTerm.Devices.Scpi (ScpiProfileCatalog) |
| **Found** | 2026-09-26 code review of `main` @ `42758db` (branch `dev/review-code`); static review, not yet reproduced |

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
