# 007: Closed TUI windows are never disposed: Page Up/Down stop working and handlers leak

| | |
|---|---|
| **Severity** | High |
| **Status** | Open |
| **Confidence** | Confirmed (found by two reviewers) |
| **Area** | TUI (DevTerm.Console) |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
- Opened without a dispose: `src/DevTerm.Console/TuiMode.cs:323-324` (Device Profiles), `:349-399` and `:924-930`
  (every control panel), `ManifestPanelMode.cs:35-37`, `ConfigureMode.Run` (`ConfigureMode.cs:49-55`).
- Handlers removed only on `Disposing`: `ControlPanelMode.cs:235-236, 281-282`; `ConfigureMode.cs:305-309, 527-528, 624-625`.

## What happens
Each window removes its handlers only in `window.Disposing`, but callers just `app.Run(parts.Window)` and never
dispose it. CLAUDE.md already notes that `Disposing` doesn't fire on its own. `OpenStreamMonitor` and
`ManifestEditorMode` do dispose explicitly; these paths don't.

## Failure scenario
- After opening and closing any control panel or Device Profiles, the dead window's `scrollOnKey` stays on the global
  `app.Keyboard.KeyDown` and marks PageUp/PageDown handled, so the main output pane can never page again.
- Each K8055 panel open leaves a `structuredPresenter.ValuesChanged` handler doing an `app.Invoke`; after N opens
  every report (hundreds a second) queues N UI-thread invokes.
- The Connection Editor's view model is never disposed, so its `FileSystemWatcher` keeps refreshing a dead window, one
  more watcher per open. The startup editor's watcher outlives its disposed app; only `NotInitializedException` is
  caught there, so another exception type would crash the process on a thread-pool thread.

## Suggested fix
Wrap each `app.Run(x.Window)` in `try { ... } finally { x.Window.Dispose(); }`, ideally through one `RunModal`
helper.

## Tests to add
After a panel or Device Profiles closes, PageDown still reaches the main window and the panel's `ValuesChanged` and
watcher subscriptions are gone.
