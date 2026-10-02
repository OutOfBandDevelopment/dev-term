# 030: Adding a note to a log that's still being recorded fails and leaves memory and disk out of step

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Fixed |
| **Confidence** | Confirmed from code; not run |
| **Area** | DevTerm.Logging (PlaybackController, SessionLog) |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Logging/Playback/PlaybackController.cs:505-518`, `SessionLog.cs:115`

## What happens
`SessionLog.Load` deliberately allows opening a live log (`FileShare.ReadWrite`), and no front end stops you opening
the file the active `SessionLogger` is writing. `AddNote` inserts the note in memory, advances `Position`, then calls
`Log.Save(Path)`, which does `File.Move(tmp, path, overwrite)`. The writer's `FileStream` was opened with
`FileShare.Read` only (not Delete).

## Failure scenario
- On Windows the replace fails with a sharing violation. The front end shows the error, but the note stays in memory
  and is shown as added; a retry inserts a duplicate.
- Where the replace succeeds (non-Windows), it drops every record captured after the load and leaves the live writer
  on an unlinked file.

## Suggested fix
Refuse `AddNote`/`Save` over the path of an active logger; and save first, inserting into memory only on success.

## Tests to add
`AddNote` on a log held open by a writer: no in-memory change on failure.

## Resolution
Fixed in `dev/fix-bugs` on 2026-09-26: `PlaybackController.AddNote` (`src/DevTerm.Logging/Playback/PlaybackController.cs`)
now builds a trial `SessionLog` (a defensive copy via `new SessionLog(Log.Header, Log.Records)`, since the
constructor copies the record list), inserts the note into that copy, and calls `Save(Path)` on it *before*
touching the real `Engine`/selection state. Only once that save succeeds does it call `Engine.AddNote` (which
mutates the live log and advances playback) and adjust `SelectionStart`/`SelectionEnd`. A save failure (a sharing
violation from an active `SessionLogger` still holding `Path` open, or any other I/O error) now propagates before
any in-memory mutation, so the note never appears added when it wasn't, and a retry after resolving the conflict
can't insert a duplicate. Refusing outright when a live logger holds the path was not implemented — no such
linkage exists between `PlaybackController` (`DevTerm.Logging.Playback`) and `SessionLogger`, and save-first already
removes the memory/disk desync regardless of *why* the save failed. Confirmed with a new regression test,
`PlaybackControllerTests.AddNote_WhenTheLogFileIsHeldOpenByAWriter_ThrowsAndLeavesMemoryUnchanged` (tagged
`BugRegression`), which holds `_path` open with `FileShare.Read` (mirroring `SessionLogWriter`'s own share mode) and
confirms `AddNote` throws (`UnauthorizedAccessException` on Windows, confirmed directly against the pre-fix code)
while leaving `Log.Records.Count`, `Engine.Position` and `SelectionEnd` all unchanged.
