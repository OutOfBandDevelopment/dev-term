# 009: Closing while the pipe is full throws part-way and leaks the port or socket

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Open |
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
