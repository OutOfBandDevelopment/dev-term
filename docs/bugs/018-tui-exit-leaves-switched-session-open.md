# 018: Quitting the TUI after a profile switch never closes the live session

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Open |
| **Confidence** | Confirmed |
| **Area** | TUI (TuiMode) |
| **Found** | 2026-09-26 code review of `main` @ `42758db` (branch `dev/review-code`); static review, not yet reproduced |

## Where
`src/DevTerm.Console/TuiMode.cs:77` compared with `:575`

## What happens
`RunAsync` ends with `await session.CloseAsync();` on its own parameter. `BuildWindow` reassigned only its own copy
(`session = mySession;`). `Program`'s `await using var session` disposes only the original, which the switch already
disposed.

## Failure scenario
Switch profile, then quit. The switched-to transport (serial, USBTMC, BLE) is never closed or disposed, and observers
(the session logger) never get `OnClosed`.

## Suggested fix
Expose the current session on `TuiWindowParts` (a `Func<Session>`) and close and dispose that one on exit.

## Tests to add
TUI exit after a switch closes the switched-to session.
