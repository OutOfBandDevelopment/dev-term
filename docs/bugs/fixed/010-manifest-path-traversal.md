# 010: A manifest's UiFile/KaitaiFile path can make Save write, and Load read, anywhere on disk

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Fixed |
| **Confidence** | Confirmed |
| **Area** | DevTerm.DeviceManifests (loader, writer, validator) |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
- Write: `src/DevTerm.DeviceManifests/DeviceManifestWriter.cs:40-45` (`UiFile`), `:53-61` (`KaitaiFile`)
- Read: `src/DevTerm.DeviceManifests/DeviceManifestLoader.cs:86-104`
- `DeviceManifestValidator` checks neither path.

## What happens
Both use `Path.Combine(directory, manifest.UiFile)`. A rooted second argument (`C:\...`) or `..\..\` escapes the
manifest folder.

## Failure scenario
- The user opens a manifest zip from someone else in the manifest editor and saves it. The UI JSON is written to,
  say, `%APPDATA%\...\Startup\x.json`, or over any user-writable file. `KaitaiFile` can copy an arbitrary
  source-relative file to an arbitrary destination.
- Loading reads (and parses as a UI definition) any file on disk, and the `KaitaiFile` existence check works as a
  "does this file exist" probe. Impact on the read side is limited, since content is only parsed.

## Suggested fix
In the validator, reject rooted paths and any path whose `Path.GetFullPath` falls outside the manifest folder.
Enforce the same check in the loader and the writer.

## Tests to add
Manifests with `UiFile`/`KaitaiFile` set to a rooted path and to `..\` are rejected on load and on save.

## Resolution
Fixed in `dev/fix-bugs` on 2026-09-26: new `ManifestRelativePath` (`src/DevTerm.DeviceManifests/ManifestRelativePath.cs`)
rejects a rooted `UiFile`/`KaitaiFile` path and any path containing a `..` segment, and confirms the resolved
(`Path.GetFullPath`) result still falls inside the manifest's own folder. `DeviceManifestLoader.LoadFromFile` and
`DeviceManifestWriter.Save` now combine both paths through `ManifestRelativePath.CombineSafely` (which throws
`InvalidOperationException`) instead of a bare `Path.Combine`, on both the load side (including the writer's own
Kaitai *source* path, which is just as attacker-controlled as its destination) and the save side. This runs before
validation ever gets a chance to run (`Load(path, validate: false, ...)`, used by the manifest editor to open a
broken manifest, still calls `LoadFromFile` first), so the guard has to live in the loader/writer themselves, not
only in `DeviceManifestValidator`. `DeviceManifestValidator.Validate` also gained the same `ManifestRelativePath.IsSafe`
check as an error, so the manifest editor's own save-time validation catches an unsafe path before `Save` is ever
reached. Regression tests: `DeviceManifestTests.Load_UiFileIsARootedPath_ThrowsInsteadOfReadingIt`,
`Load_UiFileEscapesTheManifestFolderWithDotDot_Throws`, `Load_KaitaiFileIsARootedPath_ThrowsInsteadOfProbingIt`,
`Save_UiFileIsARootedPath_ThrowsInsteadOfWritingOutsideTheManifestFolder`,
`Save_KaitaiFileEscapesTheManifestFolderWithDotDot_Throws`,
`Validate_UiFileOrKaitaiFileEscapesTheManifestFolder_ReportsAnError`.
