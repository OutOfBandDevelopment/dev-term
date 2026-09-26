# Connecting and disconnecting without restarting

**File > Connect/Disconnect** in the TUI and WPF toggles the *same* session/transport open or
closed, without touching which profile is loaded — different from
[Managing connection profiles](managing-profiles.md)'s Device Profiles menu, which switches to a
*different* saved connection entirely (also live, also no restart, just a different transport/
device instead of the same one). The CLI has no equivalent: it's a single session for the life of
the process, Ctrl+C to end it.

## TUI

Connected — the menu item reads "Disconnect":

![TUI main screen, connected](images/tui-main-connected.png)

The bottom line is the connection-state indicator, green for connected. After **File > Disconnect**:
- the send field is disabled;
- the menu item flips back to "Connect";
- the title gains " — disconnected";
- the status line turns red and reads "Disconnected — …";
- a `[dev-term] Disconnected.` line appears. App messages are tagged `[dev-term]`, and errors
  `[error]`, so neither can be mistaken for device output (tagged `[ascii]` etc.).

![TUI main screen, disconnected](images/tui-main-disconnected.png)

Selecting "Connect" again reopens the same transport with the same settings.

## WPF

The same toggle — connected:

![WPF main window, connected](images/wpf-main-window-connected.png)

Disconnected: the send box disables, the status bar's dot turns red and reads "Disconnected — …", and
a line is added to the output. App messages are shown dimmed and italic, errors in dark red, and
device output in normal text:

![WPF main window, disconnected](images/wpf-main-window.png)

## Trying to send while disconnected

Both front ends guard against sending on a closed session — typing and pressing Enter/Send while
disconnected reports "Not connected" in the output instead of throwing or silently doing nothing.
