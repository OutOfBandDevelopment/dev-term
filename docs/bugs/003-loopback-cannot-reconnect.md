# 003: Loopback transport can't reconnect after Disconnect

| | |
|---|---|
| **Severity** | High |
| **Status** | Open |
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
