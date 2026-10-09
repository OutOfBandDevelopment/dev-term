# 033: Profiles, the saved default and preferences are written non-atomically

| | |
|---|---|
| **Severity** | Low |
| **Status** | Fixed |
| **Confidence** | Confirmed pattern |
| **Area** | DevTerm.Configuration |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`File.WriteAllText` in `ConnectionProfileStore` (Save, imports), `DevTermConfiguration.SaveLocalProfile`
(`DevTermConfiguration.cs:68`) and the preferences store.

## Failure scenario
A crash or power loss mid-write leaves a truncated file, which then feeds [004](004-bad-profile-value-crashes-title.md)
and [032](032-startup-bind-failure-crash.md). (`AppPreferencesStore.Load` does tolerate a corrupt file.)

## Suggested fix
Write to a temp file in the same folder, then `File.Move(temp, path, overwrite: true)`.

## Tests to add
A regression test that a write failure partway through (a locked/undeletable temp path standing in
for a crash mid-write) leaves the existing file untouched, plus a plain round-trip write test.

## Resolution
Fixed on 2026-09-26 on `dev/fix-bugs`: added `AtomicFile.WriteAllText` (writes to `<path>.tmp` in the
same folder, then `File.Move(temp, path, overwrite: true)`) and replaced every bare
`File.WriteAllText` call in `ConnectionProfileStore` (Save, ExportToFile, ImportZip, RemoveMany's
rewrite), `AppPreferencesStore.Save`, and `DevTermConfiguration.SaveLocalProfile` with it. Regression
tests: `AtomicFileTests.WriteAllText_OnSuccess_WritesContentsAndLeavesNoTemporaryFile`,
`AtomicFileTests.WriteAllText_WhenTheTemporaryFileCannotBeWritten_LeavesTheExistingFileUntouched`.

Resolution recorded in commit `babdf36` (backfilled 2026-10-09 from git history).
