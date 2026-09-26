# 001: Opening an already-open session starts a second read loop

| | |
|---|---|
| **Severity** | High |
| **Status** | Open |
| **Confidence** | Confirmed (found by two reviewers) |
| **Area** | DevTerm.Core (Session), TUI, WPF |
| **Found** | 2026-09-26 code review of `main` @ `42758db` (branch `dev/review-code`); static review, not yet reproduced |

## Where
- `src/DevTerm.Core/Sessions/Session.cs:114-139` (`OpenAsync`)
- Callers: `src/DevTerm.Wpf/MainWindow.xaml.cs:139, 182-195`; `src/DevTerm.Console/TuiMode.cs:229-240, 737-753` (`ToggleConnectionAsync`)

## What happens
Every transport's `OpenAsync` returns immediately when it's already `Open` or `Opening`. `Session.OpenAsync` has no
matching check. After the transport call it raises `OnOpened`, bumps `_generation`, overwrites `_readLoopCts` (the
old one is never cancelled or disposed) and starts a second `PumpAsync` on the same `PipeReader`.

Both front ends leave **File > Connect** enabled while the state is `Opening` (`RefreshConnectionUi` only changes
the header), and `ToggleConnectionAsync` only checks `State == ConnectionState.Open`.

## Failure scenario
A slow TCP or BLE connect, and the user clicks Connect again (or uses File > Connect while WPF's `Loaded` connect,
or a profile switch, is still opening). The second call waits on `_lifecycleLock`, then double-opens. The second
pump's `ReadAsync` throws "reading is already in progress", the new generation faults, and the healthy connection
closes with a bogus "Connection lost: ...".

This is the double-open hazard CLAUDE.md records for `Show()` + `ConnectAsync`, reached another way; that one was
fixed at the caller rather than in `Session`.

## Suggested fix
- In `Session.OpenAsync`, inside the lock, return early when `_readLoopTask is not null`.
- In both front ends, disable Connect while `Opening` (or keep an in-flight flag).

## Tests to add
- `Session.OpenAsync` twice: data still arrives and no `Disconnected` is raised.
- Each front end: Connect invoked twice during `Opening`.
