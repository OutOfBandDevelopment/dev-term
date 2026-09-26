# 034: Export All deletes the existing zip before checking the profile names

| | |
|---|---|
| **Severity** | Low |
| **Status** | Fixed |
| **Confidence** | Confirmed |
| **Area** | DevTerm.Configuration (ConnectionProfileStore) |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Configuration/ConnectionProfileStore.cs:132-150`

## Failure scenario
Export All runs after another process deleted a profile (the watcher refresh is asynchronous). The user's existing zip
is deleted first, then the export throws on the missing name, leaving a partial zip.

## Suggested fix
Check the names first, or write to a temp file and move it into place.

## Tests to add
A regression test that exports a subset of names to a path that already holds a valid zip, where one
of the requested names doesn't exist: assert the call throws `FileNotFoundException` and the original
zip at that path is left completely intact (same entry count/content), not deleted or truncated.

## Resolution
Fixed on 2026-09-26 on `dev/fix-bugs`: `ExportZip` now validates every name exists first, then builds
the archive at `<zipPath>.tmp` and only `File.Move`s it over `zipPath` once every entry was written
successfully — the existing zip is never deleted/truncated unless the new one fully succeeded.
Regression test: `ConnectionProfileStoreTests.ExportZip_WhenOneOfTheNamesIsMissing_LeavesAnExistingZipAtThatPathUntouched`.
