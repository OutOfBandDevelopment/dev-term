# 029: TCP writes block with no timeout and ignore cancellation

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Fixed |
| **Confidence** | Confirmed |
| **Area** | DevTerm.Transports.Tcp |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Transports.Tcp/TcpTransport.cs:130`, `SystemTcpConnection.cs:24`

## What happens
`WriteAsync` calls the synchronous `NetworkStream.Write` (its `WriteTimeout` defaults to infinite) and ignores
`cancellationToken`.

## Failure scenario
A peer that stops reading (a full TCP window) freezes whichever thread called `Session.SendAsync`, often the UI
thread, forever.

## Suggested fix
Use `Stream.WriteAsync(data, token)` with a timeout (a linked `CancelAfter` from `WriteTimeoutMs`).

## Tests to add
A write to a connection that never drains times out.

## Resolution
Fixed in `dev/fix-bugs` on 2026-09-26: `ITcpConnection.Write(byte[], int, int)` is now
`Task WriteAsync(ReadOnlyMemory<byte>, CancellationToken)`, implemented in `SystemTcpConnection` via
`Stream.WriteAsync` (a real async socket op on `NetworkStream`, unlike `SerialPort`/HID's sync-wrapped streams, so
it genuinely honors cancellation). `TcpTransport.WriteAsync` (`src/DevTerm.Transports.Tcp/TcpTransport.cs`) now
awaits it under a linked `CancellationTokenSource` cancelled after a new `TcpTransportOptions.WriteTimeoutMs`
(default 5000), converting `OperationCanceledException` into `TimeoutException` only when the timeout (not the
caller's own token) fired. `AddDevTermFrontEnd`'s `tcp` branch
(`src/DevTerm.Configuration/ServiceCollectionExtensions.cs`) now binds `WriteTimeoutMs` from the existing generic
`CliOptions.WriteTimeoutMs`, matching Serial/Usbtmc/BLE (see [027](027-ble-timeouts-unused.md)). Confirmed with a
new regression test in `TcpTransportTests`
(`WriteAsync_ConnectionNeverDrains_ThrowsTimeoutExceptionAfterWriteTimeoutMs`, tagged `BugRegression`), using a
mocked `ITcpConnection.WriteAsync` that never completes; the interface signature change alone made the test project
fail to compile against the pre-fix code (`ITcpConnection` had no `WriteAsync`), confirmed directly before applying
the fix.

Resolution recorded in commit `babdf36` (backfilled 2026-10-09 from git history).
