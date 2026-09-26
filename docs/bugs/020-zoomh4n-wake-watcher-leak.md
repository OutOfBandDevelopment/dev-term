# 020: Each Zoom H4n panel open adds a pipeline presenter that is never removed

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Open |
| **Confidence** | Confirmed (found by two reviewers) |
| **Area** | DevTerm.Devices.ZoomH4n, TUI, WPF |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Devices.ZoomH4n/ZoomH4nControlSurface.cs:64`: `_session.AddPresenter(_wakeWatcher);`
Built per open at `src/DevTerm.Console/TuiMode.cs:385` and `src/DevTerm.Wpf/MainWindow.xaml.cs:396`.

## What happens
Nothing calls `RemovePresenter` for the watcher, and the surface has no `Dispose`. Its `SemaphoreSlim` is never
disposed either. (`ManifestPanel` handles the same pattern correctly.)

## Failure scenario
Open the remote panel N times; N watchers scan every received byte for the rest of the session.

## Suggested fix
Make the surface `IDisposable` and remove the watcher when the panel closes; better, attach it only for the duration
of the handshake.

## Tests to add
Opening and closing the panel twice leaves no watcher in the pipeline.
