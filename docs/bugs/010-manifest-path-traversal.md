# 010: A manifest's UiFile/KaitaiFile path can make Save write, and Load read, anywhere on disk

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Open |
| **Confidence** | Confirmed |
| **Area** | DevTerm.DeviceManifests (loader, writer, validator) |
| **Found** | 2026-09-26 code review of `main` @ `42758db` (branch `dev/review-code`); static review, not yet reproduced |

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
