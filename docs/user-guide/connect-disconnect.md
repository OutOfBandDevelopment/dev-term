# Connecting and disconnecting without restarting

**File > Connect/Disconnect** in the TUI and WPF toggles the *same* session/transport open or
closed, without restarting the app or touching which profile is loaded — different from
[Managing connection profiles](managing-profiles.md)'s Device Profiles menu, which switches to a
different saved connection (and does need a restart). The CLI has no equivalent: it's a single
session for the life of the process, Ctrl+C to end it.

## TUI

Connected — the menu item reads "Disconnect":

![TUI main screen, connected](images/tui-main-connected.png)

After **File > Disconnect**: the send field is disabled, and the menu item flips back to "Connect":

![TUI main screen, disconnected](images/tui-main-disconnected.png)

Selecting "Connect" again reopens the same transport with the same settings.

## WPF

The same toggle — connected:

![WPF main window, connected](images/wpf-main-window-connected.png)

Disconnected — the send box disables and a line is appended to the output list:

![WPF main window, disconnected](images/wpf-main-window.png)

## Trying to send while disconnected

Both front ends guard against sending on a closed session — typing and pressing Enter/Send while
disconnected reports "Not connected" in the output instead of throwing or silently doing nothing.
