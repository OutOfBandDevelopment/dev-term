# TUI Main Screen

## Purpose

`DevTerm.Console.TuiMode` — the console app's default full-screen mode (no flags needed;
`--cli true`/`--tui false` gets the CLI instead). The window you land in once a connection is
established, whether from CLI flags, a saved profile, or the Connection Editor. One screen for the
life of the process: a tab strip (one tab per open session), each with its own scrolling output pane
and send line, sharing one `File` menu and one status line for whichever tab is active.

## Fields

| Field | Type | Notes |
|---|---|---|
| Tabs strip | `Terminal.Gui.Views.Tabs`, fills the window below the menu bar, above the send line | One tab per open session (`TuiWindowTab`), titled from `ConnectionDescription.Definition` (or the saved profile's name, same rule as the title bar — see **States**). Starts with exactly one tab (the startup connection). Switching tabs (click, or the tab strip's own keys) swaps which tab's output pane and `Send:` field are visible and makes that tab's session the one every shared chrome element — status line, title, Device menu, Connect/Disconnect — reflects, via the same `RefreshConnectionUi` used for connect/disconnect/profile-switch (see **States**) |
| Output pane | read-only `Terminal.Gui.Editor`, one per tab, fills that tab's content area above the send line | Every incoming decoded message is appended as `[{presenterName}] {text}`. App status lines are tagged `[dev-term] …` (connected, disconnected, switched, auto-detect progress) and errors `[error] …` (failed connects, lost connections, rejected input), so they can't be mistaken for device output. The lines are also colored by their tag (`OutputHighlighting`, a small XSHD definition on the Editor's AvalonEdit-derived highlighting engine): `[error]` lines in the theme's `outputError` and bold, `[dev-term]` lines in its `outputStatus` and italic, device output in the default color (the XSHD is generated per theme, so View > Theme recolors it live). The tags stay in the text, so the distinction survives a terminal without color. Long lines soft-wrap to the pane's width (`Editor.WordWrap`), so a long reply or error reads from its start instead of running off the right edge. It auto-scrolls to the newest line and keeps the last 300. Each tab's incoming bytes route only to that tab's own pane — a background tab's output keeps accumulating while another tab is active |
| Status line | full-width `Label` under the send line, shared by every tab | ` ● Connected — tcp://192.168.0.107:23` on green, ` ● Connecting — …` on amber, ` ● Disconnected — …` on red (`ConnectionDescription.StatusText`; the colors are the theme's `statusConnected`/`statusConnecting`/`statusDisconnected` roles with their `…Text` partners). While logging, `   ● REC {log file name}` is appended. A name longer than 28 characters keeps its end (`…tcp_192.168.0.107_23.jsonl`), because a Terminal.Gui label word-wraps an over-long line and the overflow is never shown. Always describes the **active tab's** session — switching tabs refreshes it |
| `Send:` | `TextField`, one per tab, fills the remaining width next to the `Send:` label | Disabled whenever that tab's session isn't open; cleared immediately on Enter, before the send even completes; Up/Down recall prior sent lines (a per-tab `SendHistory`, 100 entries, in-memory only, not shared across tabs; a line identical to the one just before it isn't recorded again) — no visible drop-down, since Terminal.Gui 2.5.0 has no combo box |

## Actions

| Action | Behavior | Preconditions | On failure |
|---|---|---|---|
| **Type + Enter** in `Send:` | Encodes the line with the current send format (the parser — see **Send as** below), appends the configured `LineEnding`, and sends; the field clears immediately; the (non-empty) line is recorded in `SendHistory` regardless of what happens next | Line non-empty; session open | "Not connected — use File > Connect."; a line the parser can't encode (e.g. non-hex text with **Send as** hex) is rejected with "Not sent: … isn't valid hex input (…)" and the connection is left alone; a device-side failure disconnects (see **Errors** below). None of these throw |
| **Cursor Up / Cursor Down** in `Send:` | Recalls the previously sent line (Up, repeatable toward older entries) or steps back toward the newest (Down) — see `docs/user-guide/sending-and-receiving.md` | None | n/a |
| **Send as** menu (menu bar) | Picks the parser (send format) for every line typed afterward — one item per presenter that can encode typed text (ascii, utf8, hex, decimal, octal, binary); starts as the profile's `Parser`, or its first presenter if none is saved. Updates the title bar; does not reconnect or touch the display presenters | None | n/a |
| **File > Connect/Disconnect** | A single menu item whose label flips; toggles the same `Session`/transport open or closed without touching which profile is loaded | None | `ConnectionErrorMessages.For` text in the output pane; stays disconnected, ready to retry |
| **File > Device Profiles...** | Opens `ConfigureMode` as a nested modal (`Application.Run` on top of the current window) | None | n/a |
| **File > New Session...** | Opens `ConfigureMode` as a nested modal; picking a connection there (rather than cancelling) adds it as a **new tab** (`SessionTab.Build`) after the current one, switches the tab strip to it, and connects it — the active tab at the time this was chosen is untouched | None | A connection that fails to build (bad options) is reported with `Could not open a new session: …` on the **previously active** tab's output pane, and no tab is added; a connect failure after the tab is added is reported on the new tab's own pane, same as a normal failed connect |
| **File > Close Session** | Closes the **active** tab: closes its session, removes its output pane and tab header, and switches the tab strip to a remaining tab — or, if it was the last tab, leaves the window open with zero tabs (see **States**) | At least one tab open (always true once the window exists) | n/a — closing a session's transport itself never throws out to this action |
| **File > Start Logging...** / **Stop Logging** | One item whose title flips, reflecting the **active tab's own** logger (`TuiWindowTab.Logger`) — logging is per-tab, so two tabs can log to two different files at once, and closing one tab's log never touches another's. Start prompts for a path (default `~/.dev-term/logs/{yyyyMMdd-HHmmss}_{profile or connection}.jsonl`) and records that tab's session there (`TuiLogging`, `SessionLogger`): a `session` record first (with the connection and whether it's already open), then every `tx`/`rx`/`open`/`close`/`disconnect`. It adds `[dev-term] Logging to ~\….` and the status line's `● REC` (shown only while that tab is active). Stop closes the file (`[dev-term] Stopped logging to ….`). The log **follows a live profile switch on that tab**: a new `session` record, same file. Closing a tab stops its own log, if running; quitting ends every tab's log. `--log <path>` (or `--log true` for the default path) starts logging on the startup tab when the window opens. See [`docs/design/session-logging.md`](../design/session-logging.md) | None (logging a closed connection records its later connect) | `[error] Could not start logging to '…': …`, and it stays stopped |
| **File > Open Log for Playback...** | Prompts for a log path (default: the newest log in `~/.dev-term/logs`) and opens the [Playback window](playback-window.md) as a nested modal. Never touches this window's connection | None | An error dialog for a file that isn't a session log |
| **File > Quit** / **Ctrl+Q** | Stops the application loop | None | n/a |
| **Device > K8055/Busylight/SCPI Instrument/Device Manifest...** | Opens a generic control-panel screen for that device — see [`docs/specs/device-control-panel.md`](device-control-panel.md), a separate spec since it's shared with WPF and data-driven rather than a fixed set of fields | None checked | n/a |
| **Device > Edit Device Manifest...** | Opens the manifest editor (a nested screen) to create, open, edit and save a device manifest with a live panel preview — see [`docs/specs/manifest-editor.md`](manifest-editor.md). Always enabled: it needs no connection | None | n/a |
| **Device > Stream Monitor...** | Starts watching the **active tab's** session for images/HP-GL/PostScript/PCL (auto-saving each capture) and opens its modal window; the monitor belongs to that tab (`TuiWindowTab.Monitor`) — monitoring continues after the window closes and after switching to another tab, each capture adding a `[dev-term] Captured …` status line to the tab it belongs to, and follows a profile switch on that same tab. Closing the tab disposes its monitor. Opening it again from a different tab starts a separate monitor for that tab — see [`docs/specs/stream-monitor.md`](stream-monitor.md) | None (always enabled with a tab active) | A failed save is reported on the capture/status line, never thrown |
| **View > Theme** | A submenu: **Light**, **Dark**, **System (follow the OS**, re-checked every 2 seconds**)**, then one item per valid user theme file in `~/.dev-term/themes` (by its `name`), then **Terminal (keep the terminal's colors)**, which applies no Terminal.Gui colors. The current selection is marked `●`. Picking one switches this window (and any window opened afterwards) live, with no restart: Terminal.Gui's schemes, the output highlighting, the status line and chart colors all follow. It's saved as the app preference in `~/.dev-term/preferences.json`, never in a connection profile. `--theme <light|dark|system|terminal|name>` (or `DEVTERM_THEME`) overrides it for one run without saving. See [`docs/design/theming.md`](../design/theming.md) | None | Problems loading theme files or the preferences file, an unknown `--theme`, or a preference that couldn't be saved: a `[dev-term] …` status line; the theme falls back to `system` and nothing throws |
| **View > Echo Sent Commands** | Toggles (`●`/blank marker, same convention as Theme) whether a line you send is also appended to the output pane as `Out> {line}`, right after it's successfully encoded and before the actual device write — a window-level preference, not per-tab, since it's a display choice rather than a connection property. `{line}` is the typed text plus the connection's line ending's literal characters, with every non-printable character escaped for display (`TypedInput.FormatForEcho`): CR/LF/TAB/NUL show as `\r`/`\n`/`\t`/`\0`, any other control character as `\xHH`, and a literal backslash already in the line is doubled so it's never ambiguous with an escape — e.g. a CRLF-terminated `AT` echoes as `Out> AT\r\n` | None | n/a |
| **View > Software Flow Control (XON/XOFF)** | Only meaningful for a TCP session; toggles `TcpTransport.SoftwareFlowControl` on the **active tab's** transport live, no reconnect needed. Enabled and marked (`●`/blank) only when that tab's `Session.Transport` is a `TcpTransport` — disabled and unmarked otherwise. Re-synced by `RefreshConnectionUi` on every tab switch, profile switch, and connect/disconnect, so switching tabs shows *that* tab's own transport's current setting (the gate lives per-transport-instance). See [`docs/design/transports.md`](../design/transports.md)'s TCP section | None | n/a |
| **View > Clear Output** | Clears the **active tab's** output pane (both its `OutputLines` buffer and the `Editor`'s text) — other tabs' output is untouched | None | n/a |

## States

- **`Send:` enabled/disabled** tracks `Session.State`: enabled only when `Open`. Toggled by
  Connect/Disconnect, and by the session disconnecting on its own (see **Errors**).
- **Title bar** is `dev-term — {subject} ({presenters}; send as {parser})`, e.g.
  `dev-term — tek2230 (ascii, hex; send as hex)`. The subject is the **saved profile's name** when the running connection is exactly a saved
  profile (`ConnectionProfileStore.FindName` — compares the connection-relevant subset, so run-mode
  flags don't matter; first alphabetically if two profiles are identical), otherwise the
  **connection definition** (`ConnectionDescription.Definition`): `tcp://192.168.0.110:23`,
  `tcp://*:9000 (listening)`, `serial://COM3:4800,8,n,1` (data bits, lowercase parity letter, stop
  bits), `hid://{vendor}.{product}[.{serial number}]` (hex ids).
  While the connection is closed, ` — disconnected` is appended, e.g.
  `dev-term — tek2230 (ascii; send as ascii) — disconnected`. The title is recomputed whenever the
  **Send as** parser changes or the connection state changes: connect, disconnect, self-disconnect and
  profile switch.
  A saved profile is recognised at startup too — the untracked default profile a run starts from
  counts if it matches a saved one.
- **Device > (plugin panels)**: one item per `IDevicePanelContribution` a plugin registers, listed just above Plugins...; enabled when connected and the contribution's `IsAvailable(transport, vendorId, productId)` accepts the active tab's connection, and it opens the generic control panel for it.
- **Device > Plugins...** is always enabled (it needs no connection) and shows the same text as `--listplugins true` (`PluginReport`): one line per plugin folder found, loaded or skipped with why, or "No plugins found." It is a message box; plugins are loaded once at startup, so it does not rescan.
- **One refresh for everything connection-dependent** (`RefreshConnectionUi(TuiWindowTab)`, inside
  `BuildWindow`): the File menu label, `Send:`, the title, the status line, and which **Device** menu
  items are enabled (see [`device-control-panel.md`](device-control-panel.md)) are all derived from
  the **active tab's** `Session.State` in one pass (`ActiveTab()` resolves which tab that is). It
  runs at startup and after every connect, disconnect, self-disconnect, profile switch, and **tab
  switch**, so they can't drift apart. File > Connect/Disconnect runs it too;
  `TuiWindowParts.ToggleConnectionAsync` is that exact action for tests.
- **File > Close Session is enabled with any tab open (including the last one)**, refreshed after
  every New Session/Close Session. Closing the last tab reaches the **zero-tab state** below rather
  than being blocked.
- **Zero-tab state**: once the last tab closes, the window stays open rather than exiting. The File >
  Device Profiles/Stream Monitor/Send as menu items and every Device menu item disable; `Send:` is
  cleared and disabled; the window title resets to `dev-term`; the status line reads
  ` ○ No sessions open — use File > New Session... to start one.` File > New Session... (and Quit)
  remain available — New Session is the only way back to one tab, seeded from whichever tab's
  `CliOptions` were last active (`lastCliOptions`) rather than a blank form. Reaching a tab again
  (New Session, or reopening a second tab) re-enables everything via the normal
  `RefreshConnectionUi` pass.
- Each tab's incoming bytes arrive via **that tab's own** `Session.Output`, marshaled onto the UI
  thread with `Application.Invoke` — this only works because a real `Application.Run()` loop is
  actively pumping; see `CLAUDE.md`'s constraint on `Application.Invoke` silently queuing forever
  otherwise.

## Errors

Nothing the user or the device does ends the TUI. Every error is shown in the output pane (or, over a modal
control panel, in an error dialog) and the window stays usable:

- **Startup connect failure**: the TUI opens anyway, **disconnected**, with
  `Could not open the connection: … Use File > Connect to retry, or File > Device Profiles... to choose another
  connection.` in the output pane (it used to print the error and exit to the shell).
- **Lost connection**: a read failure (unplugged cable, reset socket), the device closing the connection, or a
  failed send makes the `Session` close itself and raise `Session.Disconnected`. The output pane shows
  `Connection lost: {reason} Use File > Connect to reconnect.` (or `The device closed the connection. …`; a serial
  timeout adds the CTS/`--handshake` hint), `Send:` is disabled, and the menu item flips to `_Connect`. Reported
  once — a failed send isn't reported a second time by the send path.
- **Invalid typed input** is rejected with a message and never sent; the connection is left alone.
- **Menu actions** that throw show an error dialog instead of escaping into `Application.Run` (which only
  swallows them in RELEASE builds). Fire-and-forget work (connect, send, profile switch, SCPI auto-detect) is
  observed, so an unexpected failure is shown as `Unexpected error: …` rather than silently lost.

## Per-front-end notes

This screen has no WPF equivalent-by-name, but the same flow (connected session, type-and-send,
decoded output) is `DevTerm.Wpf.MainWindow` — see the differences called out there. Key TUI-specific
points:

- **Ctrl+Q is wired via the global `Application.KeyDown` event, not a per-view `Window.KeyDown`
  handler** — a per-view handler doesn't reliably see a key already routed to a focused child first
  (`sendField` normally has focus). The `Quit` `MenuItem`'s own `Key` argument only labels the
  shortcut for display; it doesn't register a live binding by itself (see `CLAUDE.md`).
- **`MenuItem.Title` is mutable at runtime** (unlike its `Key` shortcut argument) — the
  Connect/Disconnect item flips in place rather than needing two separate items shown/hidden.
- **Device Profiles reuses `ConfigureMode.BuildWindow` as a nested `Application.Run` modal**, not a
  separate dialog type — picking a profile there saves it as the untracked default *and*
  live-switches this window's own session to it immediately (`DevTermSessionBuilder`, a
  `SwitchProfileAsync` local function inside `BuildWindow` exposed via `TuiWindowParts`), no
  restart — see [`docs/design/connection-profiles.md`](../design/connection-profiles.md).
- **Session-tab keyboard shortcuts** — Ctrl+T (File > New Session...), Ctrl+W (File > Close Session,
  the active tab), Ctrl+Tab / Ctrl+Shift+Tab, or Alt+Right / Alt+Left (next/previous tab, wrapping; a no-op below two tabs) —
  wired via a dedicated handler on the global `Application.KeyDown` event (same reasoning as Ctrl+Q
  above), gated on `app.TopRunnableView == window` so a nested dialog (New Session's own
  `ConfigureMode`, a control panel) gets the keys instead while one is open.

## Open items

- **The "Send as" item set is built once, from the startup tab, not rebuilt per tab.** Since 2026-10-03 the
  items follow the active tab: `●` marks its chosen parser and formats its presenters can't encode are
  greyed out. A tab whose catalog offers a format the startup tab lacked has no item for it yet (Terminal.Gui
  has no live item replacement for a `MenuBarItem`). In practice every tab uses the same installed set.
