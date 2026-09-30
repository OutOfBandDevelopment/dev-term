# Running multiple sessions at once

**File > New Session...** opens a second (or third, ...) connection alongside the ones already
open, each in its own tab — so you can, say, watch a serial device and a TCP instrument side by
side without running two copies of dev-term. **File > Close Session** closes whichever tab is
active. This is distinct from [Connecting and disconnecting without restarting](connect-disconnect.md)
(which toggles one tab's own connection) and [Managing connection profiles](managing-profiles.md)
(which switches one tab to a *different* saved connection) — those both still act on a single tab;
this is about how many tabs exist. The CLI has no equivalent: it's always a single session for the
life of the process.

## TUI

Starting from one connected tab, **File > New Session...** opens the same Connection Editor used for
[Connecting to a device](connecting.md) and [Managing connection profiles](managing-profiles.md).
Picking a connection there (rather than cancelling) adds it as a new tab, switches to it, and
connects it — the tab you started from is left exactly as it was:

![TUI main screen, two tabs open](images/tui-main-multi-session.png)

Each tab keeps its own output pane, its own `Send:` field and history, and its own connection state
— the shared status line, title bar, and Device menu always describe whichever tab is currently
active, switching the instant you click a different tab or move focus to it.

**File > Close Session** closes the active tab: its connection closes, its output pane and tab
header disappear, and the tab strip switches to a remaining tab. It's disabled (and does nothing if
invoked anyway) when only one tab is open — there's always at least one session while the window is
open.

## WPF

`DevTerm.Wpf.MainWindow` has the same **File > New Session...**/**File > Close Session** menu items
over a `TabControl`, added alongside the TUI's in the same feature (see
[`docs/design/multi-session-ui.md`](../design/multi-session-ui.md)): each tab is its own connection
with its own output list, send box and history, and the window's shared chrome follows whichever tab
is active, the same way.

## What doesn't (yet) follow the active tab

Two things are deliberately window-level rather than per-tab for now (see
[`docs/design/multi-session-ui.md`](../design/multi-session-ui.md)'s Step 4):

- **Session logging** ([Logging and playing back a session](logging-and-playback.md)) and the
  **[Stream Monitor](stream-monitor.md)** both keep following whichever tab was active when they
  were started or opened, not whichever tab is active right now.
- The **Send as** parser choice is set on whichever tab is active at the time you pick it, but the
  menu itself isn't rebuilt per tab — switching tabs doesn't show you the newly active tab's own
  parser selection.
