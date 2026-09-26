# 017: A WPF profile switch can't be superseded by a second switch

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Open |
| **Confidence** | Confirmed |
| **Area** | WPF (MainWindow) |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Wpf/MainWindow.xaml.cs:618-667`; compare the TUI's `switchCts` at `src/DevTerm.Console/TuiMode.cs:551-637`.

## What happens
`_session.OpenAsync()` is called with no cancellation token. A second switch's `await _session.CloseAsync()` waits on
`_lifecycleLock`, which the pending `transport.OpenAsync` holds (`Session.cs:116-119, 154`).

## Failure scenario
The TUI comment's ".108 then .107" case: switch to an unreachable host, then immediately to a good one. In WPF the
second switch waits out the first's OS connect timeout (about 20 s), showing "Connecting".

## Suggested fix
Port the TUI's `CancellationTokenSource` plus `ReferenceEquals(switchCts, cts)` guards.

## Tests to add
WPF: a switch superseded by another before it resolves (the TUI already has this test).
