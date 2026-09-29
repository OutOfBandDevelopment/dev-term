# 058: Two close requests during a slow cleanup run OnClosing twice

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
| **Confidence** | Plausible |
| **Area** | WPF (MainWindow) |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Wpf/MainWindow.xaml.cs:672-706`

## What happens
`_closeConfirmed` is set only after the awaits. A second Ctrl+Q or Alt+F4 while `CloseAsync` is pending re-enters:
the monitor is disposed twice, the session closed twice, and `Close()` called twice; the second may throw inside an
`async void` handler.

## Suggested fix
A `_closing` flag that sets `e.Cancel = true` and returns while cleanup is running.
