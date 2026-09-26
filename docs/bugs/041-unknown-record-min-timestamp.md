# 041: An unknown first record with no timestamp makes 1x playback wait effectively forever

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
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
