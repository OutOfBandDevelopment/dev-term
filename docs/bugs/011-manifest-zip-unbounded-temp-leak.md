# 011: Manifest zips extract with no size limit and leave a temp folder behind on every open

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Open |
| **Confidence** | Confirmed |
| **Area** | DevTerm.DeviceManifests (loader) |
| **Found** | 2026-09-26 code review of `main` @ `42758db` (branch `dev/review-code`); static review, not yet reproduced |

## Where
`src/DevTerm.DeviceManifests/DeviceManifestLoader.cs:52-57`

## What happens
Zip-slip is not an issue (.NET's `ExtractToDirectory` refuses entries that escape the folder), but extraction has no
cap on total size or entry count, and each load creates `%TEMP%\devterm-manifests\<random>` that is never deleted.
The picker, the panel and the editor re-extract on every open.

## Failure scenario
- A zip bomb fills the disk.
- Normal use slowly fills `%TEMP%` with copies of the same manifest.

## Suggested fix
Cap the total uncompressed size and entry count before extracting, and delete the extraction folder once the
manifest is loaded (or reuse one folder per zip).

## Tests to add
An oversize zip is refused; loading a zip leaves no extraction folder behind.
