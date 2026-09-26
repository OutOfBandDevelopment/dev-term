# 036: A session-log write failure is silent and can leave a torn line that makes the whole log unloadable

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
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
