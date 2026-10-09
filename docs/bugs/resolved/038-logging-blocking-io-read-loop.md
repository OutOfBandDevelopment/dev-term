# 038: Session logging does blocking file I/O on the read loop for every chunk

| | |
|---|---|
| **Severity** | Low |
| **Status** | Fixed |
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

## Tests to add
A test that writes to a `SessionLogWriter` backed by a deliberately slow `Stream` and asserts the calling thread
returns quickly (well under the stream's own per-write delay) rather than blocking until the disk write completes.

## Resolution
Fixed on 2026-09-26 on `dev/fix-bugs`: `SessionLogWriter` (`src/DevTerm.Logging/SessionLogWriter.cs`) now hands each
record to an unbounded `Channel`, drained by one dedicated background `Task` that does the actual `WriteLine`
(JSON encode, write, flush) — `Write`/`Write(Func<...>)` only enqueue and return, so a `Session`'s read loop (or any
other caller) never blocks on the underlying stream. Sequence-number assignment moved from `SessionLogger` into the
writer's drain loop (`Func<long, SessionLogRecord>` builders instead of ready-made records for the live-capture
path), since a record's position in the file is only truly known once it's this record's turn to actually be
written — `SessionLogWriter.RecordCount`/`SessionLogger.RecordCount` now report how many records have *reached the
file*, not merely how many were handed to `Write`. `Dispose()` completes the channel and blocks until the drain task
has finished everything already queued, so a disposed writer's file is always fully flushed. Every record is still
flushed individually (not batched on a timer, unlike the suggested fix) — this fix targets the reported hang risk
(blocking disk I/O on the caller) without weakening the "at most one torn line, always the file's last" crash-safety
guarantee that [036](036-log-write-failure-silent.md)'s fix added and tests. The still-mentioned per-chunk allocation
overhead (`ReadOnlySequence<byte>.ToArray()`, closures) in `SessionLogger`/`Session`/`Pipeline` is a separate,
non-blocking inefficiency and is not addressed here.

Several `SessionLoggerTests` timing assumptions changed along with `RecordCount`'s new meaning (now
"persisted", not "enqueued"): most polling waits (`WaitForAsync(() => logger.RecordCount == N)`) already tolerated
this and needed no change; three assertions that previously ran immediately after triggering an event, with no wait,
were updated to wait for the new async persistence the same way
(`RecordsOpenTxRxAndClose_InOrder_WithSequenceNumbersAndClockTimestamps`,
`AFailedSend_IsRecordedAsTheAttemptedTxThenADisconnect`, and
[036](036-log-write-failure-silent.md)'s own regression test,
`AWriteFailure_MarksTheLoggerInactive_AndStopsAdvancingTheRecordCount`). New regression test:
`SessionLoggerTests.Write_WithASlowUnderlyingStream_ReturnsWithoutWaitingForTheDiskWrite` (confirmed to fail against
the pre-fix code: 216ms observed vs. a 50ms bound, against a stream with a 100ms per-write delay).

Resolution recorded in commit `babdf36` (backfilled 2026-10-09 from git history).
