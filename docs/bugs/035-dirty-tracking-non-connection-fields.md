# 035: Typing an export path marks the editor as having unsaved changes

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
| **Confidence** | Confirmed |
| **Area** | DevTerm.Configuration (ConnectionEditorViewModel) |
| **Found** | 2026-09-26 code review of `main` @ `42758db` (branch `dev/review-code`); static review, not yet reproduced |

## Where
`src/DevTerm.Configuration/ConnectionEditorViewModel.cs:94-113` (`_nonDirtyProperties`)

## What happens
`SaveName` and `ImportExportPath` aren't in `_nonDirtyProperties`.

## Failure scenario
Type an export path and export successfully; closing the editor still asks "discard unsaved changes?" though nothing
would be lost.

## Suggested fix
Add both to `_nonDirtyProperties`.
