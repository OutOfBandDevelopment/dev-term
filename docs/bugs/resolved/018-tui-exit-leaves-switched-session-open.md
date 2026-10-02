# 018: Quitting the TUI after a profile switch never closes the live session

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Fixed |
| **Confidence** | Confirmed |
| **Area** | TUI (TuiMode) |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

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

## Resolution
Fixed in `dev/fix-bugs` on 2026-09-26: `TuiWindowParts` gained a `Func<Session> CurrentSession`
(`src/DevTerm.Console/TuiMode.cs`), returning `BuildWindow`'s own `session` local (the same one
`SwitchProfileAsync` reassigns on a switch). `RunAsync` now closes/disposes whatever
`parts.CurrentSession()` returns once the loop ends, disposing it only when it differs from
`RunAsync`'s own `session` parameter (which, unswitched, is left to `Program`'s `await using`
to dispose as before). Previously `RunAsync` closed its own `session` parameter unconditionally -
already closed/disposed by the switch as the *old* session - so whichever session was actually
current after a switch was never closed or disposed on exit.

Regression test: `TuiModeSwitchProfileTests.CurrentSession_AfterASwitch_IsTheSwitchedToSessionNotTheOriginal`.
