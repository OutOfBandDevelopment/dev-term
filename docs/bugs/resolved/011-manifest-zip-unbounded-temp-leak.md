# 011: Manifest zips extract with no size limit and leave a temp folder behind on every open

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Fixed |
| **Confidence** | Confirmed |
| **Area** | DevTerm.DeviceManifests (loader) |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

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

## Resolution
Fixed in `dev/fix-bugs` on 2026-09-26: `DeviceManifestLoader.ResolveManifestFile`'s zip branch now goes through a
new `ExtractZip` helper. Before extracting anything, it walks the zip's `ZipArchive.Entries` (via
`ZipFile.OpenRead`, no extraction yet) and throws `InvalidDataException` if the zip has more than 500 entries or
more than 100 MiB of total uncompressed content — both caps far above any real manifest bundle (`device.json`, a UI
file, maybe a Kaitai file). The extraction folder itself is no longer `Path.GetRandomFileName()`'d on every open;
it's now `%TEMP%\devterm-manifests\<sha256 of the zip's full path + length + last-write-time>`, deleted and
re-extracted fresh on each load but reused across repeated opens of the *same* zip content — chosen over deleting
the folder right after load because `ManifestEditorViewModel.Open` keeps the extracted directory around as
`SourceDirectory` for a later `DeviceManifestWriter.Save` (to copy a referenced Kaitai file from the manifest's
original location), so an immediate delete would break that path. Regression tests:
`DeviceManifestTests.Load_ZipWithTooManyEntries_ThrowsAndDoesNotExtract`,
`DeviceManifestTests.Load_SameZipTwice_ReusesOneExtractionFolderInsteadOfLeakingANewOneEachTime`.
