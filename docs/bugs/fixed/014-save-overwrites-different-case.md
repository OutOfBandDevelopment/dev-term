# 014: Saving "bench" silently overwrites "Bench"

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Fixed |
| **Confidence** | Confirmed |
| **Area** | DevTerm.Configuration (ConnectionEditorViewModel) |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

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

## Resolution
Fixed in `dev/fix-bugs` on 2026-09-26: `ConnectionEditorViewModel.SaveAsProfile`
(`src/DevTerm.Configuration/ConnectionEditorViewModel.cs`) now checks
`Profiles.Contains(name, StringComparer.OrdinalIgnoreCase)`, matching the store's own `List()` and NTFS's
case-insensitivity, so saving "bench" over an existing "Bench" asks `ConfirmOverwrite` instead of silently
overwriting it. Regression test:
`ConnectionEditorViewModelTests.SaveCommand_WhenNameAlreadyExistsUnderADifferentCase_AsksForConfirmationFirst`.
