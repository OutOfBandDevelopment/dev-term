# 002: USBTMC Close hangs forever while a reply over 64 KB is being read

| | |
|---|---|
| **Severity** | High |
| **Status** | Open |
| **Confidence** | Code path confirmed; trigger plausible |
| **Area** | DevTerm.Transports.Usbtmc |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Transports.Usbtmc/UsbtmcTransport.cs:188, 207-221, 141`

## What happens
`WriteAsync` holds `_ioLock` while it awaits `writer.FlushAsync(CancellationToken.None)`. The pipe is a default
`new Pipe()`, which pauses the writer once 64 KB is unread. `Session.StopAsync` stops its read loop first and only
then calls `transport.CloseAsync`, which waits for `_ioLock` with no cancellation.

## Failure scenario
Disconnect (or a session fault) while a reply over 64 KB is arriving, such as a scope screenshot or waveform (the
default `MaxResponseSize` is 16 MB). Nobody reads the pipe again, so the flush never completes and `CloseAsync`
waits forever, holding `Session._lifecycleLock`. Every later Close, Open, Dispose and app exit hangs.

## Suggested fix
In `CloseAsync`, call `_pipe?.Writer.CancelPendingFlush()` (or complete the reader) before waiting for `_ioLock`.
Alternatives: `PauseWriterThreshold = 0`, or flush outside the lock.

## Tests to add
Close during a reply larger than the pipe's pause threshold completes within a timeout.
