# 033: Profiles, the saved default and preferences are written non-atomically

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
| **Confidence** | Confirmed pattern |
| **Area** | DevTerm.Configuration |
| **Found** | 2026-09-26 code review of `main` @ `42758db` (branch `dev/review-code`); static review, not yet reproduced |

## Where
`File.WriteAllText` in `ConnectionProfileStore` (Save, imports), `DevTermConfiguration.SaveLocalProfile`
(`DevTermConfiguration.cs:68`) and the preferences store.

## Failure scenario
A crash or power loss mid-write leaves a truncated file, which then feeds [004](004-bad-profile-value-crashes-title.md)
and [032](032-startup-bind-failure-crash.md). (`AppPreferencesStore.Load` does tolerate a corrupt file.)

## Suggested fix
Write to a temp file in the same folder, then `File.Move(temp, path, overwrite: true)`.
