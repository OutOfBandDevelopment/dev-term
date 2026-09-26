# 029: TCP writes block with no timeout and ignore cancellation

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Open |
| **Confidence** | Confirmed |
| **Area** | DevTerm.Transports.Tcp |
| **Found** | 2026-09-26 code review of `main` @ `42758db` (branch `dev/review-code`); static review, not yet reproduced |

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
