# 038: Session logging does blocking file I/O on the read loop for every chunk

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
| **Confidence** | Plausible |
| **Area** | DevTerm.Logging (SessionLogger, SessionLogWriter) |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Logging/SessionLogger.cs:87, 133`, `SessionLogWriter.cs:100-103`

## What happens
Each chunk does `ToArray`, builds the JSON as a string, encodes it again, writes twice and calls `Flush()`, all under
two locks on the read loop. `Session.PumpAsync`/`Pipeline.Render` also allocate on every chunk (a `Notify` closure, a
snapshot array, a results list).

## Failure scenario
1-byte serial reads at 115200 baud mean roughly 11,000 flushes a second. A slow disk or an AV scanner stalls the read
loop and, through backpressure, the device read.

## Suggested fix
Hand records to a `Channel` drained by a writer task that flushes on a timer.
