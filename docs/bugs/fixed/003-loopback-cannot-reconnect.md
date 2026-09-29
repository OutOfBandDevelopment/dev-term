# 003: Loopback transport can't reconnect after Disconnect

| | |
|---|---|
| **Severity** | High |
| **Status** | Fixed |
| **Confidence** | Confirmed |
| **Area** | DevTerm.Transports.Loopback |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Transports.Loopback/LoopbackTransport.cs:18, 51-53, 57-60`

## What happens
One `Pipe` lives for the transport's whole lifetime (`private readonly Pipe _pipe = new();`). `CloseAsync`
completes its writer, and `OpenAsync` only sets `State = Open`.

## Failure scenario
TUI or WPF with the loopback transport: File > Disconnect, then File > Connect (same `Session`, same transport). The
new read loop sees the reader already completed and reports a disconnect. Typing then throws "Writing is not
allowed after writer was completed".

## Suggested fix
Create a new `Pipe` in `OpenAsync`, as the other transports do.

## Tests to add
Open, Close, Open, send `hello`, and the reply arrives. No transport has a reopen test today; add one for each.

## Resolution
Fixed on 2026-09-26 (branch `dev/fix-bugs`): `LoopbackTransport` now holds `_pipe` as a mutable field and
creates a fresh `Pipe` in `OpenAsync`, matching the other transports (e.g. `TcpTransport`), instead of
reusing one `Pipe` for the transport's whole lifetime. `CloseAsync` still completes that pipe's writer, but
a later `OpenAsync` now gets a new, writable one rather than reopening over an already-completed writer.
Regression test:
`DevTerm.Transports.Loopback.Tests.LoopbackTransportTests.OpenAsync_AfterClose_CanReconnectAndExchangeData`.
