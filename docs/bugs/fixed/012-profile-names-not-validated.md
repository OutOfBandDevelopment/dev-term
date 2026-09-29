# 012: Profile names are never validated

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Fixed |
| **Confidence** | Confirmed for traversal and invalid characters; the ADS behaviour plausible |
| **Area** | DevTerm.Configuration (ConnectionProfileStore, DevTermUserDataPaths) |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
- `src/DevTerm.Configuration/ConnectionProfileStore.cs:291` (`Path.Combine(_profilesDirectory, $"{name}.json")`)
- `src/DevTerm.Configuration/DevTermUserDataPaths.cs:61` (`ResolveManifestDirectory`)

## What happens / failure scenario
- `..\..\Desktop\x` or `C:\temp\x` saves outside the profiles directory; Delete and Load follow the same path.
- `ab:c` becomes an NTFS alternate data stream on a file named `ab`. `List()` (`*.json`) never shows it, so the
  saved profile silently vanishes.
- Zip import is safe from zip-slip (`Path.GetFileNameWithoutExtension` strips separators), but a name that's legal
  on Linux (`a?b`, exported by a Linux user) fails in Replace All only *after* every existing profile has been
  deleted. `ReadZip` checks JSON syntax, not names.
- `ResolveManifestDirectory` passes a profile's `ManifestName` (meant to be a name, not a path) straight to
  `Path.Combine`, so an imported profile can point it at an absolute or `..` path.

## Suggested fix
One `IsValidProfileName` check: reject `Path.GetInvalidFileNameChars()`, `:`, `..`, rooted names and reserved device
names (`CON`, `NUL`, ...). Use it in Save, ImportZip and `ReadZip`, so Replace All refuses before deleting anything.
Apply the rooted/`..` check to `ManifestName`.

## Tests to add
Save with `..\x`, `a:b`, `CON`; Replace All with an entry name invalid on Windows leaves existing profiles intact.

## Resolution
Fixed in `dev/fix-bugs` on 2026-09-26: new `ProfileName` (`src/DevTerm.Configuration/ProfileName.cs`) rejects
`Path.GetInvalidFileNameChars()` (which already covers `:` and both separators, so `..\..\Desktop\x`, `C:\temp\x`
and `a:b` are all caught by this one check), a rooted name, a `..` segment, and the Windows-reserved device names
(`CON`, `NUL`, `COM1`-`COM9`, `LPT1`-`LPT9`). `ConnectionProfileStore.Save` now throws `ArgumentException` on an
invalid name before writing anything; `ImportZip` skips (rather than writes) a zip entry whose name fails the
check, since it doesn't delete anything first; `ReadZip` throws `InvalidDataException` on a bad name, so
`ReplaceAll` (which reads the whole zip via `ReadZip` before deleting a single existing profile) refuses the
import up front instead of only failing after every existing profile is already gone.
`DevTermUserDataPaths.ResolveManifestDirectory` also runs a profile's `ManifestName` through the same check,
returning `null` (its existing "couldn't resolve" contract) instead of combining a rooted or escaping name into a
real path — confirmed with a test pointing `ManifestName` at a real, existing directory (`Path.GetTempPath()`'s
own guaranteed-to-exist temp folder), since a nonexistent target would have returned `null` either way and proven
nothing. Regression tests:
`ConnectionProfileStoreTests.Save_WithAnInvalidProfileName_ThrowsInsteadOfWritingIt`,
`ConnectionProfileStoreTests.ImportZip_WithAnInvalidEntryName_SkipsItInsteadOfWritingIt`,
`ConnectionProfileStoreTests.ReadZip_WithAnEntryNameInvalidOnWindows_ThrowsSoReplaceAllRefusesBeforeDeletingAnything`,
`DevTermUserDataPathsTests.ResolveManifestDirectory_WithARootedNamePointingAtARealDirectory_ReturnsNullInsteadOfThatDirectory`.
