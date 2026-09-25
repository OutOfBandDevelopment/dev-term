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
| `SendBox` | Editable `ComboBox` (`IsEditable`, `IsTextSearchEnabled="False"`), fills the remaining width next to the Send button | `IsEnabled` only when the session is open (every text presenter can encode input, so there's no per-presenter check any more); `ItemsSource` is bound directly to a shared `SendHistory.Items` (100 entries, in-memory only), so its drop-down doubles as the history list; Up/Down also recall without opening the drop-down; a line identical to the one just before it isn't recorded again |
| `ParserBox` | `ComboBox` labelled "Send as:", docked right of the Send button | One item per presenter that can encode typed text; starts as the profile's `Parser` (its first presenter if none is saved); selecting one changes the send format for every line typed afterward and refreshes the title |

## Actions

| Action | Behavior | Preconditions | On failure |
|---|---|---|---|
| **Type + Enter, or click Send** | Encodes the line with the `Send as:` format, appends the configured `LineEnding`, and sends; `SendBox` clears immediately; the (non-empty) line is recorded in `SendHistory` regardless of what happens next | Line non-empty | "Not connected — use File > Connect." (session closed); a line the `Send as:` parser can't encode is rejected with "Not sent: … isn't valid {parser} input (…)" and the connection is left alone; a device-side failure disconnects (see **Errors**). Appended to `OutputList`, never thrown |
| **Up / Down in `SendBox`** | Recalls the previously sent line (Up, repeatable toward older entries) or steps back toward the newest (Down); handled on `PreviewKeyDown` so the `ComboBox`'s own native key handling never sees it first | None | n/a |
| **File > Connect/Disconnect** | A single menu item whose header flips (`_Connect`/`_Disconnect`); toggles the same `Session`/transport without touching the loaded profile | None | `ConnectionErrorMessages.For` text appended to `OutputList` (no modal); session stays closed, ready to retry |
| **File > Device Profiles...** | Opens `DeviceProfilesWindow` as a modal (`ShowDialog`) | None | n/a |
| **File > Exit** / **Ctrl+Q** | Closes the window | None | n/a |
| **Device > K8055/Busylight/SCPI Instrument...** | Opens a generic, non-modal control-panel window for that device — see [`docs/specs/device-control-panel.md`](device-control-panel.md), a separate spec since it's shared with the TUI and data-driven rather than a fixed set of fields | None checked | n/a |
| **Closing** (any way — Exit, Ctrl+Q, the window's own X button) | Cancels the first close request, awaits `Session.CloseAsync`/`DisposeAsync`, then closes for real | None | n/a — but see Open items about test automation and this specific handler |

## States

- **`SendBox.IsEnabled`** is set on load, on every Connect/Disconnect, and when the session disconnects
  on its own (see **Errors**). It's `true` only while the session is open.
- **`ConnectMenuItem.Header`** mirrors `Session.State` — `_Disconnect` when open, `_Connect` when
  closed.
- **Title bar**, like the TUI's, is `dev-term — {subject} ({presenters}; send as {parser})` — the
  saved profile's name when the running connection is exactly a saved profile, otherwise its
  connection definition (`tcp://192.168.0.110:23`, `serial://COM3:4800,8,n,1`,
  `hid://1915.AFDA.{serial}`); see the TUI spec for the exact matching rule. It's refreshed on
  connect, profile switch, and a `Send as:` change (only while the session is open), but does not
  update on Disconnect — see Open items.

## Errors

The window never closes itself or crashes over an error. Every error is appended to `OutputList`, and the
window stays usable:

- **Startup connect failure**: the window stays open and **disconnected** (`SendBox` disabled, the menu shows
  `_Connect`), with `Could not open the connection: … Use File > Connect to retry, or File > Device Profiles... to
  choose another connection.` It used to show a modal and then close the whole app. A failed **profile switch**
  behaves the same way.
- **Lost connection** (a read failure, the device closing the connection, or a failed send): the `Session` closes
  itself and raises `Session.Disconnected`. The window appends `Connection lost: {reason} Use File > Connect to
  reconnect.` and switches to disconnected. It's reported once.
- **Invalid typed input** is rejected with a message and never sent. The connection is left alone.
- **Fire-and-forget work** (connect, send, profile switch, SCPI auto-detect) is observed, so an unexpected failure
  shows as `Unexpected error: …`. Anything else on the UI thread still goes to `App`'s
  `DispatcherUnhandledException` handler, which reports it and keeps the app running.

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
- **Only one session per window.** Presenters are no longer the blocker: they've been per-session since 2026-09-25
  (see `docs/design/presenters.md`). Nothing builds a multi-session UI yet.
- **The `Closing` reentrancy edge case above** hasn't been root-caused or fixed — only worked around
  in test automation by not exercising it.
