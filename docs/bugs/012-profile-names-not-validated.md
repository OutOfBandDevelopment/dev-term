# 012: Profile names are never validated

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Open |
| **Confidence** | Confirmed for traversal and invalid characters; the ADS behaviour plausible |
| **Area** | DevTerm.Configuration (ConnectionProfileStore, DevTermUserDataPaths) |
| **Found** | 2026-09-26 code review of `main` @ `42758db` (branch `dev/review-code`); static review, not yet reproduced |

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
