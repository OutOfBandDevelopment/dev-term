# 034: Export All deletes the existing zip before checking the profile names

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
| **Confidence** | Confirmed |
| **Area** | DevTerm.Configuration (ConnectionProfileStore) |
| **Found** | 2026-09-26 code review of `main` @ `42758db` (branch `dev/review-code`); static review, not yet reproduced |

## Where
`src/DevTerm.Configuration/ConnectionProfileStore.cs:132-150`

## Failure scenario
Export All runs after another process deleted a profile (the watcher refresh is asynchronous). The user's existing zip
is deleted first, then the export throws on the missing name, leaving a partial zip.

## Suggested fix
Check the names first, or write to a temp file and move it into place.
