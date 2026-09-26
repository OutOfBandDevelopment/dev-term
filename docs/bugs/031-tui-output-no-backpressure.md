# 031: The TUI output pane has no backpressure

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Open |
| **Confidence** | Plausible |
| **Area** | TUI (TuiMode) |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Console/TuiMode.cs:207-219, 528`

## What happens
Every `Session.Output` does an `app.Invoke(...)`, and each invoke rebuilds `string.Join('\n', outputLines)` and resets
`Editor.Text` (wrap plus highlighting). WPF's synchronous `Dispatcher.Invoke` at least applies backpressure.

## Failure scenario
A streaming device queues invokes faster than they drain; memory and lag grow without bound.

## Suggested fix
Queue lines in a concurrent queue and schedule a single pending `Invoke` that drains it in batches.

## Tests to add
Many outputs in a burst produce a bounded number of UI updates.
