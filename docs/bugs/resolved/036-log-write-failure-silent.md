# 036: A session-log write failure is silent and can leave a torn line that makes the whole log unloadable

| | |
|---|---|
| **Severity** | Low |
| **Status** | Fixed |
| **Confidence** | Plausible |
| **Area** | DevTerm.Logging (SessionLogWriter, SessionLogger), DevTerm.Core (Session) |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Logging/SessionLogWriter.cs:98-103`, `SessionLogger.cs:133`, `src/DevTerm.Core/Sessions/Session.cs:100`,
`SessionLog.cs:90-93`

## What happens
When the disk fills during capture, `WriteLine` throws inside an observer callback, and `Session.Notify` swallows it to
Debug output. The UI still shows logging as active and `RecordCount` still increments (the sequence number is taken
before the failed write).

## Failure scenario
A partial write followed by later successful writes (after space frees up) leaves a malformed line that isn't the last
line. `SessionLog.Read` tolerates a torn line only at the end, so the whole file fails to load.

## Suggested fix
On the first write failure, mark the logger faulted, stop writing and surface it (an event, or `IsActive` false). Or
make `Read` skip malformed lines with a warning each.

## Tests to add
A test that fails a `SessionLogWriter`'s underlying stream partway through a write (torn line, no trailing
newline), then confirms: (1) `SessionLogger.IsActive` becomes `false`, (2) `RecordCount` stops advancing, and (3)
no further record - valid or not - is appended to the file after the torn line.

## Resolution
Fixed on 2026-09-26 on `dev/fix-bugs`: `SessionLogWriter.WriteLine` (`src/DevTerm.Logging/SessionLogWriter.cs`) now
catches any exception from the underlying stream, sets a new `IsFaulted` flag, and rethrows; both `Write` overloads
check `IsFaulted` first and no-op (without even building the record, so a `SessionLogger`'s sequence number stops
advancing too) once set - so a torn line from a failed write is never followed by another line, valid or not, which
is what let the whole file become unloadable. `SessionLogger.IsActive` now also returns `false` once its writer is
faulted, so a caller polling it (as `SessionLogging.Follow` already does before continuing a log across a profile
switch) sees the fault instead of treating the logger as still capturing. Regression test:
`SessionLoggerTests.AWriteFailure_MarksTheLoggerInactive_AndStopsAdvancingTheRecordCount`, using a custom
`Stream` that fails on a specific write call to simulate a disk filling up partway through a record. Live UI
surfacing (an in-progress capture indicator flipping the moment a fault happens, rather than the next time the
menu/status is refreshed) was left out of scope - both front ends' logging-status text is only refreshed on
explicit Start/Stop today, and wiring a live update is a larger change than this report's core defect (an
unloadable log file) needed.
