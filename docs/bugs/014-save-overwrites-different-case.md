# 014: Saving "bench" silently overwrites "Bench"

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Open |
| **Confidence** | Confirmed |
| **Area** | DevTerm.Configuration (ConnectionEditorViewModel) |
| **Found** | 2026-09-26 code review of `main` @ `42758db` (branch `dev/review-code`); static review, not yet reproduced |

## Where
`src/DevTerm.Configuration/ConnectionEditorViewModel.cs:1260` (`Profiles.Contains(name)`)

## What happens
The existence check is ordinal and case-sensitive (`Collection<string>.Contains`), but the store and NTFS are
case-insensitive, and `List()` itself uses `OrdinalIgnoreCase`.

## Failure scenario
Profile `Bench` exists; the user saves as `bench`. `ConfirmOverwrite` is never asked and `Bench.json` is overwritten.

## Suggested fix
`Profiles.Contains(name, StringComparer.OrdinalIgnoreCase)`.

## Tests to add
Save with a different-case existing name asks for confirmation.
