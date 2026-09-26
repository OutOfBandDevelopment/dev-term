# 030: Adding a note to a log that's still being recorded fails and leaves memory and disk out of step

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Open |
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
