# 019: A Stream Monitor capture in progress is lost when the TUI quits

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Open |
| **Confidence** | Confirmed |
| **Area** | TUI (TuiMode, Stream Monitor) |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Console/TuiMode.cs:649`: `window.Disposing += (_, _) => monitor.Dispose();`

## What happens
The main window is never disposed, so the handler never runs (CLAUDE.md: `Disposing` doesn't fire after `Run`
returns). WPF disposes the monitor in `OnClosing`.

## Failure scenario
A capture is still in progress at Ctrl+Q; it's never flushed or saved.

## Suggested fix
Dispose the monitor explicitly after `app.Run` in `RunAsync`, via the window parts.

## Tests to add
Quitting with a capture in progress saves it.
