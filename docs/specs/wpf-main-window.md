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
| `SendBox` | `TextBox`, fills the remaining width next to the Send button | `IsEnabled` only when the session is open (every text presenter can encode input, so there's no per-presenter check any more) |
| `ParserBox` | `ComboBox` labelled "Send as:", docked right of the Send button | One item per presenter that can encode typed text; starts as the profile's `Parser` (its first presenter if none is saved); selecting one changes the send format for every line typed afterward and refreshes the title |

## Actions

| Action | Behavior | Preconditions | On failure |
|---|---|---|---|
| **Type + Enter, or click Send** | Encodes the line with the `Send as:` format, appends the configured `LineEnding`, and sends; `SendBox` clears immediately | Line non-empty | "Not connected — use File > Connect." (session closed) / "Send timed out — ..." / "Send failed: {message}" — appended to `OutputList`, never thrown |
| **File > Connect/Disconnect** | A single menu item whose header flips (`_Connect`/`_Disconnect`); toggles the same `Session`/transport without touching the loaded profile | None | `MessageBox.Show` with `ConnectionErrorMessages.For` text; session stays closed |
| **File > Device Profiles...** | Opens `DeviceProfilesWindow` as a modal (`ShowDialog`) | None | n/a |
| **File > Exit** / **Ctrl+Q** | Closes the window | None | n/a |
| **Device > K8055/Busylight/SCPI Instrument...** | Opens a generic, non-modal control-panel window for that device — see [`docs/specs/device-control-panel.md`](device-control-panel.md), a separate spec since it's shared with the TUI and data-driven rather than a fixed set of fields | None checked | n/a |
| **Closing** (any way — Exit, Ctrl+Q, the window's own X button) | Cancels the first close request, awaits `Session.CloseAsync`/`DisposeAsync`, then closes for real | None | n/a — but see Open items about test automation and this specific handler |

## States

- **`SendBox.IsEnabled`** is set on load and on every Connect/Disconnect — `true` only while the
  session is open.
- **`ConnectMenuItem.Header`** mirrors `Session.State` — `_Disconnect` when open, `_Connect` when
  closed.
- **Title bar**, like the TUI's, is `dev-term — {subject} ({presenters}; send as {parser})` — the
  saved profile's name when the running connection is exactly a saved profile, otherwise its
  connection definition (`tcp://192.168.0.110:23`, `serial://COM3:4800,8,n,1`,
  `hid://1915.AFDA.{serial}`); see the TUI spec for the exact matching rule. It's refreshed on
  connect, profile switch, and a `Send as:` change (only while the session is open), but does not
  update on Disconnect — see Open items.

## Per-front-end notes

- **Auto-connects on `Loaded`**, not on construction — `MainWindow(session, catalog, cliOptions)`
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
