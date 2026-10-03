# WPF Main Window

## Purpose

`DevTerm.Wpf.MainWindow` — the GUI front end's only window today. Shown once a connection is
established (from CLI flags/environment, a saved profile, or the Connection Editor at startup);
connects automatically on its `Loaded` event. A tab strip (one tab per open session), each with its
own scrolling output list, sharing one send box/parser box/status bar/`File` menu for whichever tab
is active — the WPF equivalent of the TUI's tab strip (`docs/specs/tui-main-screen.md`). See
[`docs/design/multi-session-ui.md`](../design/multi-session-ui.md) for the design this implements.

## Fields

| Field | Type | Notes |
|---|---|---|
| `SessionTabs` | `TabControl`, fills the window below the send row | One `TabItem` per open session (`WindowTab` pairs a framework-agnostic `SessionTab` with the WPF chrome built for it), built entirely in code-behind (`AddTab`), not XAML. Starts with exactly one tab (the startup connection). Each tab's header is a `StackPanel` of a `TextBlock` (`ConnectionDescription.Subject` — the saved profile's name, or the connection definition, same rule as the title bar) and a small "✕" close button. Switching tabs (`SessionTabs_SelectionChanged`) rebinds `ParserBox`/`SendBox`/its history to that tab and calls `RefreshConnectionUi` for it — the same one-pass refresh used for connect/disconnect/profile-switch (see **States**) |
| Output list | `ListBox` of `OutputLine(Text, Kind)` records, one per tab, is that `TabItem`'s `Content` | Every incoming decoded message is appended as `[{presenterName}] {text}` (kind `Device`, normal text) via that tab's own `Session.Output`/`Session.Disconnected` handlers — a background tab's output keeps accumulating while another tab is active, and each tab's events always reach its own list, never whichever tab happens to be showing. App status lines (connected, disconnected, switched, auto-detect progress) are kind `Status`, shown italic in the theme's `outputStatus`; errors (failed connects, lost connections, rejected input) are kind `Error`, semibold in its `outputError` (dark red in Light), styled by a `DataTemplate` trigger bound to the theme brushes (`{DynamicResource DevTerm.OutputStatus}` etc.), so View > Theme recolors existing lines live. `OutputLine.ToString()` is the text, so copying or reading the list as strings is unaffected. Capped at 1000 lines per tab (oldest dropped first) |
| Status bar | `StatusBar` docked at the bottom: a colored dot (`ConnectionStatusDot`) and text (`ConnectionStatusText`), then `LoggingStatusText` | `Connected — tcp://192.168.0.107:23` with a green dot, `Connecting — …` amber, `Disconnected — …` red (`ConnectionDescription.StatusText`; the dot is the theme's `statusConnected`/`statusConnecting`/`statusDisconnected`). While logging, `● REC {log file name}` in the theme's `recording` color, semibold, follows it (trimmed with "…" when the bar is too narrow for it, the full path in its tooltip); it's empty otherwise. The window's minimum size is 480x300. Always describes the **active tab's** session — switching tabs refreshes it |
| `SendBox` | Editable `ComboBox` (`IsEditable`, `IsTextSearchEnabled="False"`), shared, fills the remaining width next to the Send button | `IsEnabled` only when the active tab's session is open (every text presenter can encode input, so there's no per-presenter check any more); `ItemsSource` is rebound on every tab switch to that tab's own `SendHistory.Items` (100 entries, in-memory only, not shared across tabs), so its drop-down doubles as that tab's history list; Up/Down also recall without opening the drop-down; a line identical to the one just before it isn't recorded again |
| `ParserBox` | `ComboBox` labelled "Send as:", shared, docked right of the Send button | One item per presenter that can encode typed text, rebound on every tab switch to that tab's own catalog; starts as the profile's `Parser` (its first presenter if none is saved); selecting one changes the send format for the **active tab** for every line typed afterward and refreshes the title |

## Actions

| Action | Behavior | Preconditions | On failure |
|---|---|---|---|
| **Type + Enter, or click Send** | Encodes the line with the active tab's `Send as:` format, appends the configured `LineEnding`, and sends on that tab's session; `SendBox` clears immediately; the (non-empty) line is recorded in that tab's `SendHistory` regardless of what happens next | Line non-empty | "Not connected — use File > Connect." (session closed); a line the `Send as:` parser can't encode is rejected with "Not sent: … isn't valid {parser} input (…)" and the connection is left alone; a device-side failure disconnects (see **Errors**). Appended to that tab's output list, never thrown |
| **Up / Down in `SendBox`** | Recalls the active tab's previously sent line (Up, repeatable toward older entries) or steps back toward the newest (Down); handled on `PreviewKeyDown` so the `ComboBox`'s own native key handling never sees it first | None | n/a |
| **Click a tab** | Switches the active tab (`SessionTabs_SelectionChanged`): rebinds `ParserBox`/`SendBox`/its history to the newly active tab and refreshes every connection-dependent chrome element for it | None | n/a |
| **Click a tab's own "✕"** | Closes **that** tab specifically, whether or not it's the active one — the tab header's close button calls `CloseTabAsync` against its own `WindowTab` directly, not the active one (unlike File > Close Session below) | At least one tab open (always true once the window exists); closing the last one reaches the zero-tab state (see **States**) | n/a — closing a session's transport itself never throws out to this action |
| **File > Connect/Disconnect** | A single menu item whose header flips (`_Connect`/`_Disconnect`); toggles the **active tab's** `Session`/transport without touching the loaded profile | None | `ConnectionErrorMessages.For` text appended to that tab's output list (no modal); session stays closed, ready to retry |
| **File > Device Profiles...** | Opens `DeviceProfilesWindow` as a modal (`ShowDialog`); picking a connection there live-switches the **active tab's** session to it (`SwitchProfileAsync`), no restart | None | n/a |
| **File > New Session...** | Opens `DeviceProfilesWindow` as a modal, pre-filled from the active tab's own `CliOptions`; picking a connection there (rather than cancelling) adds it as a **new tab** (`AddTab`) after the current one, switches `SessionTabs` to it, and connects it — the previously active tab is untouched | None | A connection that fails to build (bad options) is reported with `Could not open a new session: …` on the **previously active** tab's output list, and no tab is added; a connect failure after the tab is added is reported on the new tab's own list, same as a normal failed connect |
| **File > Close Session** | Closes the **active** tab (`CloseSession_Click` → `CloseTabAsync(ActiveWindowTab)`): closes its session, stops its own logging/Stream Monitor, closes any control panels opened from it, removes its `TabItem`, and switches to a remaining tab — or, if it was the last tab, leaves the window open with zero tabs (see **States**) | At least one tab open (always true once the window exists) | n/a — closing a session's transport itself never throws out to this action |
| **File > Start Logging...** / **Stop Logging** (`LoggingMenuItem`) | One item whose header flips, reflecting the **active tab's own** logger (`WindowTab.Logger`) — logging is per-tab, so two tabs can log to two different files at once, and closing one tab's log never touches another's. Start shows a Save File dialog, defaulting to `~/.dev-term/logs/{yyyyMMdd-HHmmss}_{profile or connection}.jsonl`, and records that tab's session there (`MainWindow.Logging.cs`, `SessionLogger`), the same records as the TUI. It adds a `Status` line `Logging to ~\….` on that tab and the status bar's `● REC` (shown only while that tab is active). Stop closes the file. The log **follows a live profile switch on that same tab**. Closing a tab stops its own log, if running; closing the window records the final `close` on every tab's log and ends them. `--log <path>`/`--log true` starts it on the startup tab at construction, before the `Loaded`-triggered connect, so the connect is in the log. See [`docs/design/session-logging.md`](../design/session-logging.md) | None | An `Error` line `Could not start logging to '…': …`, and it stays stopped |
| **File > Open Log for Playback...** | An Open File dialog (starting in `~/.dev-term/logs`), then a non-modal [Playback window](playback-window.md) owned by this one. Never touches any tab's connection | None | An `Error` line `Could not open '…' for playback: …` |
| **File > Exit** / **Ctrl+Q** | Closes the window | None | n/a |
| **Device > K8055/Busylight/SCPI Instrument/Device Manifest...** | Opens a generic, non-modal control-panel window bound to the **active tab's** session/catalog — see [`docs/specs/device-control-panel.md`](device-control-panel.md), a separate spec since it's shared with the TUI and data-driven rather than a fixed set of fields. The panel is tracked against the tab it was opened from and closed automatically if that tab closes or its profile switches (`docs/bugs/resolved/016-wpf-panels-bound-to-old-session.md`) | None checked | n/a |
| **Device > Edit Device Manifest...** | Opens the manifest editor (a non-modal window) to create, open, edit and save a device manifest with a live panel preview — see [`docs/specs/manifest-editor.md`](manifest-editor.md). Always enabled: it needs no connection, and isn't tied to any tab | None | n/a |
| **Device > Stream Monitor...** (below a separator) | Starts watching the **active tab's** session for images/HP-GL/PostScript/PCL (auto-saving each capture) and opens the non-modal `StreamMonitorWindow`, or brings an open one forward; the monitor belongs to that tab (`WindowTab.Monitor`) — monitoring continues after that window closes and after switching to another tab, each capture adding a status line to the tab it belongs to, and follows a profile switch on that same tab. Closing the tab disposes its monitor. Opening it again from a different tab starts a separate monitor for that tab — see [`docs/specs/stream-monitor.md`](stream-monitor.md) | None (always enabled with a tab active) | A failed save is reported on the capture/status line, never thrown |
| **View > Theme** | A submenu: **Light**, **Dark**, **System (follow Windows)**, then one item per valid user theme file in `~/.dev-term/themes` (by its `name`); the current selection is checked. Picking one re-themes every open dev-term window live, with no restart (`WpfTheme`: role brushes, `SystemColors` overrides, and dark-capable control templates for a dark theme). It's saved as the app preference in `~/.dev-term/preferences.json`, never in a connection profile. **System** also follows a Windows app-mode change while running. `--theme <light|dark|system|name>` (or `DEVTERM_THEME`) overrides it for one run without saving. See [`docs/design/theming.md`](../design/theming.md) | None | Problems loading theme files or the preferences file, an unknown `--theme`, or a preference that couldn't be saved: a status line in the active tab's output list; the theme falls back to `system` and nothing throws |
| **View > Echo Sent Commands** (`IsCheckable`) | Toggles whether a line you send is also appended to the **active tab's** output list as kind `Sent` (`Out> {line}`, shown in the theme's accent color), right after it's successfully encoded and before the actual device write — a window-level preference, not per-tab, since it's a display choice rather than a connection property. `{line}` is the typed text plus the connection's line ending's literal characters, with every non-printable character escaped for display (`TypedInput.FormatForEcho`): CR/LF/TAB/NUL show as `\r`/`\n`/`\t`/`\0`, any other control character as `\xHH`, and a literal backslash already in the line is doubled so it's never ambiguous with an escape — e.g. a CRLF-terminated `AT` echoes as `Out> AT\r\n` | None | n/a |
| **View > Software Flow Control (XON/XOFF)** (`IsCheckable`) | Only meaningful for a TCP session; toggles `TcpTransport.SoftwareFlowControl` on the **active tab's** transport live, no reconnect needed. Enabled and checked only when that tab's `Session.Transport` is a `TcpTransport` — disabled and unchecked otherwise (`MainWindow.FlowControl.cs`). Re-synced by `RefreshConnectionUi` on every tab switch, profile switch, and connect/disconnect, so switching tabs shows *that* tab's own transport's current setting (the gate lives per-transport-instance). See [`docs/design/transports.md`](../design/transports.md)'s TCP section | None | n/a |
| **View > Clear Output** | Clears the **active tab's** output list — other tabs' output is untouched | None | n/a |
| **Closing** (any way — Exit, Ctrl+Q, the window's own X button) | Cancels the first close request, closes and disposes **every tab's** session, stops logging, then closes for real | None | n/a — but see Open items about test automation and this specific handler |

## States

- **`ActiveWindowTab`** resolves the `WindowTab` behind `SessionTabs.SelectedItem` (its `TabItem.Tag`)
  — every shared-chrome accessor (`OutputList`, `CurrentParser`, `TitleText`, the connect/send/profile
  actions below) reads through it, so it's always "whichever tab is showing", never a cached reference
  that could go stale across a tab switch.
- **`SendBox.IsEnabled`** is set on load, on every Connect/Disconnect, on every tab switch, and when a
  tab's session disconnects on its own while it's the active one (see **Errors**). It's `true` only
  while the active tab's session is open.
- **`ConnectMenuItem.Header`** mirrors the active tab's `Session.State` — `_Disconnect` when open,
  `_Connect` when closed.
- **Title bar**, like the TUI's, is `dev-term — {subject} ({presenters}; send as {parser})` — the
  saved profile's name when the active tab's connection is exactly a saved profile, otherwise its
  connection definition (`tcp://192.168.0.110:23`, `serial://COM3:4800,8,n,1`,
  `hid://1915.AFDA.{serial}`); see the TUI spec for the exact matching rule. ` — disconnected` is
  appended while that tab's connection is closed. It's refreshed on every `Send as:` change,
  connection-state change, and tab switch.
- **Each tab's header text** (`WindowTab.HeaderText`) is refreshed independently of whether that tab
  is active — `RefreshConnectionUi(WindowTab, …)` updates it for whichever tab it's called for, then
  returns early (skipping the title/status bar/menu refresh) unless that tab is also the active one.
- **Device > Plugins...** is always enabled (it needs no connection) and shows the same text as `--listplugins true` (`PluginReport`): one line per plugin folder found, loaded or skipped with why, or "No plugins found." It is a `MessageBox` (`MainWindow.Plugins`, set by `App`); plugins are loaded once at startup, so it does not rescan.
- **`RefreshConnectionUi(WindowTab, ConnectionState?)`** is the one place everything connection-dependent
  for **one tab** is derived from its `Session.State`: that tab's header text always, and — only when
  it's also the active tab — the File menu header, `SendBox.IsEnabled`, the title, the status bar, and
  the **Device** menu items' `IsEnabled` (see [`device-control-panel.md`](device-control-panel.md)).
  It runs from the constructor, after every connect, disconnect, self-disconnect and profile switch on
  that tab, and after every tab switch (a no-arg overload runs it for `ActiveWindowTab`). While a
  connect is in flight it's called with `ConnectionState.Opening` to show "Connecting", which the
  transport never announces to the window.
- **`CloseSessionMenuItem.IsEnabled`** is `_tabs.Count > 0`, refreshed after every tab add/remove —
  Close Session stays enabled at one tab so the window can reach zero tabs.
- **Zero-tab state** (`HandleZeroTabs`): once the last tab closes, the window stays open rather than
  closing. File > New Session... is the only menu item left enabled (it's the only way back to one
  tab, seeded from whichever tab's `CliOptions` were last active, `_lastCliOptions`) — Connect/
  Disconnect, Device Profiles..., Close Session, every Device menu item, and Stream Monitor all
  disable; `SendBox`/`ParserBox` clear, lose their `ItemsSource`, and disable; the title resets to
  `dev-term`; the status bar reads `No sessions open — File > New Session... to start one.` with the
  disconnected-color dot. `ActiveWindowTabOrNull` is `null` in this state — every accessor that needs
  an active tab (`ActiveWindowTab`) throws if called, matching the TUI's identical
  `ActiveTabOrNull()`/`ActiveTab()` split. Reaching a tab again re-enables everything via the normal
  `RefreshConnectionUi`/`RefreshLoggingUiForActiveTab` passes.
- **Control panels are tracked per tab** (`_openControlPanels`, each window's `Tag` is its owning
  `WindowTab`) so a tab close or profile switch can close exactly the panels opened from it, rather
  than leaving one open against a disposed session/catalog (`docs/bugs/resolved/016-wpf-panels-bound-to-old-session.md`).
- **A profile switch in flight is superseded per tab**, not window-wide — each `WindowTab` has its own
  `SwitchCts`, so a slow-to-fail connect on one tab can't stomp a newer switch's UI on that same tab,
  and switching a *different* tab's profile doesn't touch it at all (`docs/bugs/resolved/017-wpf-profile-switch-no-supersede.md`).

## Errors

The window never closes itself or crashes over an error. Every error is appended to the relevant tab's
output list, and the window stays usable:

- **Startup connect failure**: the window stays open and that tab is **disconnected** (`SendBox`
  disabled if it's the active tab, the menu shows `_Connect`), with `Could not open the connection: …
  Use File > Connect to retry, or File > Device Profiles... to choose another connection.` It used to
  show a modal and then close the whole app. A failed **profile switch** or **New Session** connect
  behaves the same way, reported on the relevant tab.
- **Lost connection** (a read failure, the device closing the connection, or a failed send) on any tab:
  that tab's `Session` closes itself and raises `Session.Disconnected`. Its output list gets
  `Connection lost: {reason} Use File > Connect to reconnect.`, and — only if it's the active tab — the
  window switches to showing disconnected. It's reported once per tab.
- **Invalid typed input** is rejected with a message on the active tab and never sent. The connection is
  left alone.
- **Fire-and-forget work** (connect, send, new session, close session, profile switch, SCPI auto-detect)
  is observed, so an unexpected failure shows as `Unexpected error: …` on the active tab. Anything else
  on the UI thread still goes to `App`'s `DispatcherUnhandledException` handler, which reports it and
  keeps the app running.

## Per-front-end notes

- **Auto-connects on `Loaded`**, not on construction — `MainWindow(session, catalog, cliOptions)`
  wires everything but doesn't open the session; showing the window (real `Show()`, which fires
  `Loaded`) is what triggers `ConnectAsync`. Calling `ConnectAsync` directly *and* also calling
  `Show()` opens the session twice concurrently and corrupts the single-reader `PipeReader` (see
  `CLAUDE.md`) — exactly one of the two, never both. `ConnectAsync` acts on `ActiveWindowTab`, which
  at `Loaded` time is always the single startup tab added in the constructor — a tab added later via
  File > New Session... is connected explicitly by `NewSession_Click` instead, not by `Loaded` firing
  again.
- **Ctrl+Q needs an explicit `PreviewKeyDown` handler** — `MenuItem.InputGestureText` only labels
  the shortcut in the menu, the same "display-only" gap as Terminal.Gui's `MenuItem.Key` (see
  `CLAUDE.md`). The same handler (`HandleGlobalKeyDown`, split out for direct testing) also covers
  Ctrl+T (File > New Session...), Ctrl+W (File > Close Session, the active tab), and Ctrl+Tab /
  Ctrl+Shift+Tab, or Alt+Right / Alt+Left (next/previous tab, wrapping; a no-op below two tabs) — the same four bindings the
  TUI uses.
- **`Closing` cancels the first close, awaits the session's cleanup, `await Dispatcher.Yield()`s, then
  closes for real.** Without the yield, a session that was already closed made the awaits complete
  synchronously, so the second `Close()` ran inside the first one's `Closing` and threw "Cannot ... Close
  ... while a Window is closing". That was once blamed on test automation, but it was the app's own
  reentrancy bug. It's fixed and covered for constructed windows (`MainWindowTests.Close_*`) and, since
  2026-09-25, for a really-shown, auto-connected one
  (`MainWindowConnectionStateTests.AReallyShownWindow_ClosesCleanly`).

- **The Playback window's `Owner` is set only when it's really shown.** WPF throws
  `InvalidOperationException` ("Cannot set Owner property to a Window that has not been shown
  previously") when the owner itself was never shown, which is the case for a `MainWindow` under
  test. `OpenPlayback(path, clock, show: false)` is the seam tests use.
- **`TabControl` is built entirely in code-behind, not XAML** — `MainWindow.xaml` declares only the
  empty `SessionTabs` control; `AddTab` constructs each `TabItem`, its header `StackPanel` (name +
  close button) and its output `ListBox` at runtime. Unlike the TUI's `Terminal.Gui.Views.Tabs`
  (a single widget Terminal.Gui itself draws), WPF's `TabControl` is a real, inspectable visual tree,
  so each tab's close button is an ordinary `Button.Click` handler rather than needing a keyboard/menu
  path the way the TUI's tab strip does.

