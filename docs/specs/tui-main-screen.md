# TUI Main Screen

## Purpose

`DevTerm.Console.TuiMode` — the console app's default full-screen mode (no flags needed;
`--cli true`/`--tui false` gets the CLI instead). The window you land in once a connection is
established, whether from CLI flags, a saved profile, or the Connection Editor. One screen for the
life of the process: a scrolling output pane, a send line, and a `File` menu.

## Fields

| Field | Type | Notes |
|---|---|---|
| Output pane | read-only `TextView`, fills the window above the send line | Every incoming decoded message is appended as `[{presenterName}] {text}`; auto-scrolls to the newest line (`MoveEnd()`) |
| `Send:` | `TextField`, fills the remaining width next to the `Send:` label | Disabled whenever the session isn't open; cleared immediately on Enter, before the send even completes |

## Actions

| Action | Behavior | Preconditions | On failure |
|---|---|---|---|
| **Type + Enter** in `Send:` | Encodes the line with the current send format (the parser — see **Send as** below), appends the configured `LineEnding`, and sends; the field clears immediately | Line non-empty; session open | "Not connected — use File > Connect." / a send failure message (see below) — none of these throw |
| **Send as** menu (menu bar) | Picks the parser (send format) for every line typed afterward — one item per presenter that can encode typed text (ascii, utf8, hex, decimal, octal, binary); starts as the profile's `Parser`, or its first presenter if none is saved. Updates the title bar; does not reconnect or touch the display presenters | None | n/a |
| **File > Connect/Disconnect** | A single menu item whose label flips; toggles the same `Session`/transport open or closed without touching which profile is loaded | None | Same connection-failure handling as startup (`ConnectionErrorMessages.For`) |
| **File > Device Profiles...** | Opens `ConfigureMode` as a nested modal (`Application.Run` on top of the current window) | None | n/a |
| **File > Quit** / **Ctrl+Q** | Stops the application loop | None | n/a |

## States

- **`Send:` enabled/disabled** tracks `Session.State`: enabled only when `Open`. Toggled by
  Connect/Disconnect, not by anything else.
- **Title bar** is `dev-term — {ConnectionDescription} ({presenters}; send as {parser})` (e.g.
  `(ascii, hex; send as hex)`). It updates when the **Send as** parser changes or a profile is
  switched, but not on Connect/Disconnect (see Open items).
- Incoming bytes arrive via `Session.Output`, marshaled onto the UI thread with
  `Application.Invoke` — this only works because a real `Application.Run()` loop is actively
  pumping; see `CLAUDE.md`'s constraint on `Application.Invoke` silently queuing forever otherwise.

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

- **The title bar doesn't reflect Connect/Disconnect state** — it's set once at window construction
  from the connection/presenters/parser and only refreshed by a parser or profile change, so after a
  Disconnect the title still describes the (now closed) connection. `MainWindow`'s WPF title has the same gap.
- **No visual indicator of connection state** beyond the `Send:` field's enabled/disabled look and
  the menu item's label — no status bar, no colored indicator.
- **Only one session per process** — `docs/design/presenters.md`'s stateful-presenter-vs-DI-singleton
  lifetime issue blocks multiple concurrent sessions in one TUI process (see `BACKLOG.md`).
