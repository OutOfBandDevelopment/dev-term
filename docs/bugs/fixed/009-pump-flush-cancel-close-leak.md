# 009: Closing while the pipe is full throws part-way and leaks the port or socket

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Fixed |
| **Confidence** | Code confirmed; timing plausible, not reproduced (found by two reviewers) |
| **Area** | DevTerm.Core (StreamToPipePump, Session), Serial/TCP/HID |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
- `src/DevTerm.Core/Transports/StreamToPipePump.cs:38, 50`
- `SerialTransport.cs:105-117`, `TcpTransport.cs:106-117`, `HidTransport.cs:105-117`
- `src/DevTerm.Core/Sessions/Session.cs:303` (`StopAsync`)

## What happens
`writer.FlushAsync(cancellationToken)` sits outside the pump's try. When the pipe is paused (more than 64 KB unread,
which happens while `StopAsync` waits for the read loop against a fast streaming device), cancelling the pump throws
`OperationCanceledException`, and the pump task ends Canceled. Read exceptions outside the pump's filter also escape.

Each transport's `CloseAsync` awaits `_pumpTask` unprotected, before `_port.Close()`/`Dispose()`. `Session.StopAsync`
filters `when (ex is not OperationCanceledException)`, so the cancellation escapes even though its own token was
`CancellationToken.None`, breaking `CloseAsync`'s "never throws" contract. On the fault path, `FaultAsync` then
throws inside a discarded `Task.Run`, so `Disconnected` and `OnClosed` are never raised.

## Failure scenario
Disconnect while a fast device is streaming. `CloseAsync` throws, `State` stays `Closing`, and the port, socket or
HID handle is never closed. A later `OpenAsync` on the same COM port fails with access denied.

## Suggested fix
- Pump: treat an `OperationCanceledException` from `FlushAsync` as `break`.
- Transports: wrap the pump await in try/catch and tear down in `finally`.
- Session: `catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))`.

## Tests to add
Close with a full, unread pipe completes, releases the resource, and a reopen succeeds.

## Resolution
Fixed in `dev/fix-bugs` on 2026-09-26, all three suggested layers:
- `StreamToPipePump.RunAsync` (`src/DevTerm.Core/Transports/StreamToPipePump.cs`) now wraps
  `writer.FlushAsync(cancellationToken)` in its own try/catch and treats a cancellation (a paused pipe unblocked by
  the transport deliberately closing) as a normal `break`, instead of letting `OperationCanceledException` escape
  and turn the pump's own `Task` `Canceled`.
- `SerialTransport.CloseAsync`, `TcpTransport.CloseAsync`, and `HidTransport.CloseAsync` now await `_pumpTask`
  inside a `try/catch (OperationCanceledException)`, with the port/socket/device teardown and `State =
  ConnectionState.Closed` moved into a `finally`, so a still-Canceled pump task (belt-and-suspenders, since the pump
  fix above should prevent one) can no longer stop the transport from actually closing.
- `Session.StopAsync` (`src/DevTerm.Core/Sessions/Session.cs`) now swallows an `OperationCanceledException` from
  `_transport.CloseAsync` too, unless it's the caller's own `cancellationToken` that's cancelled — a transport-internal
  cancellation must not escape `CloseAsync`'s "never throws" contract.

Regression test: `DevTerm.Core.Tests.Transports.StreamToPipePumpTests.RunAsync_WhenTheTokenIsCancelledBeforeTheFlushThatFollowsARead_CompletesWithoutThrowing`,
which fails without the pump fix (`TaskCanceledException` escapes `RunAsync`) and passes with it.
