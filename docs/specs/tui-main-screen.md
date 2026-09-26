# TUI Main Screen

## Purpose

`DevTerm.Console.TuiMode` — the console app's default full-screen mode (no flags needed;
`--cli true`/`--tui false` gets the CLI instead). The window you land in once a connection is
established, whether from CLI flags, a saved profile, or the Connection Editor. One screen for the
life of the process: a scrolling output pane, a send line, and a `File` menu.

## Fields

| Field | Type | Notes |
|---|---|---|
| Output pane | read-only `Terminal.Gui.Editor`, fills the window above the send line | Every incoming decoded message is appended as `[{presenterName}] {text}`. App status lines are tagged `[dev-term] …` (connected, disconnected, switched, auto-detect progress) and errors `[error] …` (failed connects, lost connections, rejected input), so they can't be mistaken for device output. The lines are also colored by their tag (`OutputHighlighting`, a small XSHD definition on the Editor's AvalonEdit-derived highlighting engine): `[error]` lines in the theme's `outputError` and bold, `[dev-term]` lines in its `outputStatus` and italic, device output in the default color (the XSHD is generated per theme, so View > Theme recolors it live). The tags stay in the text, so the distinction survives a terminal without color. It auto-scrolls to the newest line and keeps the last 300 |
| Status line | full-width `Label` under the send line | ` ● Connected — tcp://192.168.0.107:23` on green, ` ● Connecting — …` on amber, ` ● Disconnected — …` on red (`ConnectionDescription.StatusText`; the colors are the theme's `statusConnected`/`statusConnecting`/`statusDisconnected` roles with their `…Text` partners). While logging, `   ● REC {log file name}` is appended. A name longer than 28 characters keeps its end (`…tcp_192.168.0.107_23.jsonl`), because a Terminal.Gui label word-wraps an over-long line and the overflow is never shown |
| `Send:` | `TextField`, fills the remaining width next to the `Send:` label | Disabled whenever the session isn't open; cleared immediately on Enter, before the send even completes; Up/Down recall prior sent lines (a shared `SendHistory`, 100 entries, in-memory only; a line identical to the one just before it isn't recorded again) — no visible drop-down, since Terminal.Gui 2.5.0 has no combo box |

## Actions

| Action | Behavior | Preconditions | On failure |
|---|---|---|---|
| **Type + Enter** in `Send:` | Encodes the line with the current send format (the parser — see **Send as** below), appends the configured `LineEnding`, and sends; the field clears immediately; the (non-empty) line is recorded in `SendHistory` regardless of what happens next | Line non-empty; session open | "Not connected — use File > Connect."; a line the parser can't encode (e.g. non-hex text with **Send as** hex) is rejected with "Not sent: … isn't valid hex input (…)" and the connection is left alone; a device-side failure disconnects (see **Errors** below). None of these throw |
| **Cursor Up / Cursor Down** in `Send:` | Recalls the previously sent line (Up, repeatable toward older entries) or steps back toward the newest (Down) — see `docs/user-guide/sending-and-receiving.md` | None | n/a |
| **Send as** menu (menu bar) | Picks the parser (send format) for every line typed afterward — one item per presenter that can encode typed text (ascii, utf8, hex, decimal, octal, binary); starts as the profile's `Parser`, or its first presenter if none is saved. Updates the title bar; does not reconnect or touch the display presenters | None | n/a |
| **File > Connect/Disconnect** | A single menu item whose label flips; toggles the same `Session`/transport open or closed without touching which profile is loaded | None | `ConnectionErrorMessages.For` text in the output pane; stays disconnected, ready to retry |
| **File > Device Profiles...** | Opens `ConfigureMode` as a nested modal (`Application.Run` on top of the current window) | None | n/a |
| **File > Start Logging...** / **Stop Logging** | One item whose title flips. Start prompts for a path (default `~/.dev-term/logs/{yyyyMMdd-HHmmss}_{profile or connection}.jsonl`) and records the session there (`TuiLogging`, `SessionLogger`): a `session` record first (with the connection and whether it's already open), then every `tx`/`rx`/`open`/`close`/`disconnect`. It adds `[dev-term] Logging to ~\….` and the status line's `● REC`. Stop closes the file (`[dev-term] Stopped logging to ….`). The log **follows a live profile switch**: a new `session` record, same file. Quitting ends the log. `--log <path>` (or `--log true` for the default path) starts it when the window opens. See [`docs/design/session-logging.md`](../design/session-logging.md) | None (logging a closed connection records its later connect) | `[error] Could not start logging to '…': …`, and it stays stopped |
| **File > Open Log for Playback...** | Prompts for a log path (default: the newest log in `~/.dev-term/logs`) and opens the [Playback window](playback-window.md) as a nested modal. Never touches this window's connection | None | An error dialog for a file that isn't a session log |
| **File > Quit** / **Ctrl+Q** | Stops the application loop | None | n/a |
| **Device > K8055/Busylight/SCPI Instrument/Device Manifest...** | Opens a generic control-panel screen for that device — see [`docs/specs/device-control-panel.md`](device-control-panel.md), a separate spec since it's shared with WPF and data-driven rather than a fixed set of fields | None checked | n/a |
| **Device > Stream Monitor...** | Starts watching the current session for images/HP-GL/PostScript/PCL (auto-saving each capture) and opens its modal window; monitoring continues after the window closes, each capture adding a `[dev-term] Captured …` status line here, and follows a profile switch — see [`docs/specs/stream-monitor.md`](stream-monitor.md) | None (always enabled) | A failed save is reported on the capture/status line, never thrown |
| **View > Theme** | A submenu: **Light**, **Dark**, **System (follow the OS)**, then one item per valid user theme file in `~/.dev-term/themes` (by its `name`). The current selection is marked `●`. Picking one switches this window (and any window opened afterwards) live, with no restart: Terminal.Gui's schemes, the output highlighting, the status line and chart colors all follow. It's saved as the app preference in `~/.dev-term/preferences.json`, never in a connection profile. `--theme <light|dark|system|name>` (or `DEVTERM_THEME`) overrides it for one run without saving. See [`docs/design/theming.md`](../design/theming.md) | None | Problems loading theme files or the preferences file, an unknown `--theme`, or a preference that couldn't be saved: a `[dev-term] …` status line; the theme falls back to `system` and nothing throws |

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
- **One refresh for everything connection-dependent** (`RefreshConnectionUi`, inside `BuildWindow`): the
  File menu label, `Send:`, the title, the status line, and which **Device** menu items are enabled (see
  [`device-control-panel.md`](device-control-panel.md)) are all derived from `Session.State` in one pass.
  It runs at startup and after every connect, disconnect, self-disconnect and profile switch, so they
  can't drift apart. File > Connect/Disconnect runs it too; `TuiWindowParts.ToggleConnectionAsync` is
  that exact action for tests.
- Incoming bytes arrive via `Session.Output`, marshaled onto the UI thread with
  `Application.Invoke` — this only works because a real `Application.Run()` loop is actively
  pumping; see `CLAUDE.md`'s constraint on `Application.Invoke` silently queuing forever otherwise.

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

## Open items

- **Only one session per window.** Presenters are no longer the blocker: they've been per-session since 2026-09-25
  (see `docs/design/presenters.md`). Nothing builds a multi-session UI yet.
