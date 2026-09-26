# 041: An unknown first record with no timestamp makes 1x playback wait effectively forever

| | |
|---|---|
| **Severity** | Low |
| **Status** | Fixed |
| **Confidence** | Confirmed |
| **Area** | DevTerm.Logging (SessionLogFormat) |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Logging/SessionLogFormat.cs:20` (`ReadRecord`)

## What happens
An unknown record with no `t` gets `DateTimeOffset.MinValue`. If it's the first record, `SessionLog.Start` becomes
MinValue and every later record is billions of seconds "later".

## Suggested fix
Skip records without a timestamp when computing `Start`, or give them the previous record's time.

## Resolution
Fixed on 2026-09-26 on `dev/fix-bugs`: `SessionLog.Start` (`src/DevTerm.Logging/SessionLog.cs`) now walks the
records looking for the first one that isn't an `Unknown`-kind record with a `DateTimeOffset.MinValue`
placeholder timestamp, using that one's timestamp; it falls back to `Header.Created` if the log is empty or
every record is such a placeholder. Regression tests:
`SessionLogTests.Start_FirstRecordIsUnknownWithNoTimestamp_SkipsItInsteadOfUsingMinValue`,
`SessionLogTests.Start_AllRecordsAreUnknownWithNoTimestamp_FallsBackToHeaderCreated`.
