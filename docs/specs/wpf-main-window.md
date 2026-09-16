# WPF Main Window

## Purpose

`DevTerm.Wpf.MainWindow` — the GUI front end's only window today. Shown once a connection is
established (from CLI flags/environment, a saved profile, or the Connection Editor at startup);
connects automatically on its `Loaded` event. A scrolling output list, a send box, and a `File`
menu — the WPF equivalent of the TUI's main screen.

## Fields

| Field | Type | Notes |
|---|---|---|
| `OutputList` | `ListBox`, fills the window above the send row | Every incoming decoded message is appended as `[{presenterName}] {text}`; status lines (Connected/Disconnected/errors) are appended the same way, indistinguishable from real device output except by text |
| `SendBox` | `TextBox`, fills the remaining width next to the Send button | `IsEnabled` only when the active presenter implements `IPresenterInput` **and** (after Connect/Disconnect landed) the session is open |

## Actions

| Action | Behavior | Preconditions | On failure |
|---|---|---|---|
| **Type + Enter, or click Send** | Appends the configured `LineEnding` to the presenter's parsed bytes and sends; `SendBox` clears immediately | Line non-empty; the active presenter implements `IPresenterInput` | "Not connected — use File > Connect." (session closed) / "Send timed out — ..." / "Send failed: {message}" — appended to `OutputList`, never thrown |
| **File > Connect/Disconnect** | A single menu item whose header flips (`_Connect`/`_Disconnect`); toggles the same `Session`/transport without touching the loaded profile | None | `MessageBox.Show` with `ConnectionErrorMessages.For` text; session stays closed |
| **File > Device Profiles...** | Opens `DeviceProfilesWindow` as a modal (`ShowDialog`) | None | n/a |
| **File > Exit** / **Ctrl+Q** | Closes the window | None | n/a |
| **Closing** (any way — Exit, Ctrl+Q, the window's own X button) | Cancels the first close request, awaits `Session.CloseAsync`/`DisposeAsync`, then closes for real | None | n/a — but see Open items about test automation and this specific handler |

## States

- **`SendBox.IsEnabled`** is set on load and on every Connect/Disconnect — `true` only when the
  presenter supports input, `false` whenever the session isn't open.
- **`ConnectMenuItem.Header`** mirrors `Session.State` — `_Disconnect` when open, `_Connect` when
  closed.
- **Title bar**, like the TUI's, is set once at construction/on initial connect and does not update
  again on Disconnect — see Open items.

## Per-front-end notes

- **Auto-connects on `Loaded`**, not on construction — `MainWindow(session, presenter, cliOptions)`
  wires everything but doesn't open the session; showing the window (real `Show()`, which fires
  `Loaded`) is what triggers `ConnectAsync`. Calling `ConnectAsync` directly *and* also calling
  `Show()` opens the session twice concurrently and corrupts the single-reader `PipeReader` (see
  `CLAUDE.md`) — exactly one of the two, never both.
- **Ctrl+Q needs an explicit `PreviewKeyDown` handler** — `MenuItem.InputGestureText` only labels
  the shortcut in the menu, the same "display-only" gap as Terminal.Gui's `MenuItem.Key` (see
  `CLAUDE.md`).
- **`Closing`'s cancel-then-async-cleanup-then-reclose pattern is real, but fragile under test
  automation**: calling `Window.Close()` on a window driven by a single manually-pumped
  `DispatcherFrame` (as `DevTerm.Wpf.Tests.StaTestRunner` does) can throw "Cannot ... Close ... while
  a Window is closing" — found while building automated screenshot tests, which now deliberately
  never call `Close()` at all (see `WpfScreenshot`'s doc comment). Not confirmed to be a problem in
  the real, interactively-driven app (a real Win32 message loop, not one manually-pumped frame).

## Open items

- **The title bar doesn't reflect Connect/Disconnect state**, same gap as the TUI's main screen.
- **No visual indicator of connection state** beyond `SendBox.IsEnabled` and the menu header.
- **`OutputList` mixes device output and status/error lines** with no visual distinction — same as
  the TUI's output pane.
- **Only one session per window/process** — see `docs/design/presenters.md`'s stateful-presenter
  lifetime issue in `BACKLOG.md`.
- **The `Closing` reentrancy edge case above** hasn't been root-caused or fixed — only worked around
  in test automation by not exercising it.
