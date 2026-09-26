# 008: Real read failures are reported as a clean hang-up with no error

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Fixed |
| **Confidence** | Confirmed (found by two reviewers) |
| **Area** | DevTerm.Core (StreamToPipePump), Serial/TCP/HID |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Core/Transports/StreamToPipePump.cs:36-41, 56-59`

## What happens
A read `IOException` (TCP reset, USB-serial unplug) is caught, the loop breaks, and the writer is completed with no
exception. `Session.PumpAsync` then sees `IsCompleted` and faults with `error = null`. The path `Session.cs:235`
describes ("the transport completed its pipe with an exception") never runs for a real transport. Exceptions
outside the filter (`InvalidOperationException`, `TimeoutException` from `SerialPort`) also end in a no-error
completion, and the pump task's own failure only surfaces as a `Debug.WriteLine` from `StopAsync`.

## Failure scenario
Unplug a USB-serial cable or reset a TCP socket. The user sees "The device closed the connection." instead of
"Connection lost: <reason>", and the session log's `disconnect` record has no error.

`SessionTests.ReadFailure_*` passes only because its fake completes the pipe with an exception itself.

## Suggested fix
`catch (Exception ex) when (!cancellationToken.IsCancellationRequested) { error = ex; }`, then
`finally { await writer.CompleteAsync(error); }`. Treat cancellation, EOF and `ObjectDisposedException` during close
as a normal completion.

## Tests to add
`StreamToPipePump` has no tests. Add one where the stream throws `IOException` and `Session.Disconnected.Error` carries it.

## Resolution
Fixed in `dev/fix-bugs` on 2026-09-26: `StreamToPipePump.RunAsync`
(`src/DevTerm.Core/Transports/StreamToPipePump.cs`) now captures any exception from `source.ReadAsync` into a local
`error` and completes the writer with it (`writer.CompleteAsync(error)`), unless `cancellationToken.IsCancellationRequested`
(a deliberate close), which still completes with no error. Previously it caught `IOException`, `ObjectDisposedException`,
and `OperationCanceledException` unconditionally and always completed clean, so `Session.PumpAsync`'s
`reader.ReadAsync` saw `IsCompleted` with no exception and reported a real read failure (TCP reset, USB-serial
unplug) the same as a clean hang-up. Regression test:
`DevTerm.Core.Tests.Transports.StreamToPipePumpTests.RunAsync_WhenTheStreamThrowsDuringARead_CompletesTheWriterWithThatExceptionInsteadOfACleanHangUp`
(plus a companion `RunAsync_WhenCancelledDuringARead_CompletesTheWriterWithNoErrorAsACleanClose` covering the
deliberate-close path), which fails without the fix (no exception observed on the reader side) and passes with it.
