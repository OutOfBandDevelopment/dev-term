# 035: Typing an export path marks the editor as having unsaved changes

| | |
|---|---|
| **Severity** | Low |
| **Status** | Fixed |
| **Confidence** | Confirmed |
| **Area** | DevTerm.Configuration (ConnectionEditorViewModel) |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Configuration/ConnectionEditorViewModel.cs:94-113` (`_nonDirtyProperties`)

## What happens
`SaveName` and `ImportExportPath` aren't in `_nonDirtyProperties`.

## Failure scenario
Type an export path and export successfully; closing the editor still asks "discard unsaved changes?" though nothing
would be lost.

## Suggested fix
Add both to `_nonDirtyProperties`.

## Tests to add
A test that sets `SaveName` and `ImportExportPath` on a freshly constructed view model and asserts
`IsDirty` stays `false`.

## Resolution
Fixed on 2026-09-26 on `dev/fix-bugs`: added `nameof(SaveName)` and `nameof(ImportExportPath)` to
`_nonDirtyProperties`. Regression test:
`ConnectionEditorViewModelTests.SettingSaveNameOrImportExportPath_DoesNotMarkTheEditorDirty`.
