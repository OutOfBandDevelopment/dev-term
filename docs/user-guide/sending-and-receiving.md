# Sending commands and viewing replies

Once connected (see [Connecting to a device](connecting.md)), all three front ends do the same
thing: type a line, press Enter (or Send), see the decoded reply appear. Screenshots/transcripts
below are real captured output from the actual built app — see this folder's [README](README.md).

## CLI

```
$ dotnet DevTerm.Console.dll --transport tcp --host 192.168.0.107 --tcpport 23 --presenter ascii --lineending Cr --cli true
Connected to TCP 192.168.0.107:23 using 'ascii' (send as 'ascii').
Type a line and press Enter to send; Ctrl+C to exit.
ID?
[ascii] ID TEK/2230,V81.1,VERS:14
```

The banner and the `[ascii] ID TEK/2230,V81.1,VERS:14` line are real captured stdout (against a
small throwaway TCP stand-in that answers `ID?` exactly like the project's real Tektronix 2230 test
device does); `ID?` is what you'd type — a real terminal echoes your own keystrokes, which a piped
capture doesn't show. Every line gets the configured line ending appended (`--lineending Cr` above)
before it's sent. Ctrl+C exits; the session closes cleanly.

The banner names two things: the **presenters** (how incoming bytes are shown — one or several, e.g.
`--presenter ascii,hex` prints each reply once per presenter, tagged `[ascii]`/`[hex]`) and the
**send format** (how the line you type is turned into bytes — `--parser`, defaulting to the first
presenter). They're independent: `--presenter ascii,hex --parser hex` shows replies both ways but
sends what you type as hex. The CLI fixes the send format for the whole run; a per-line choice
lives in the TUI and WPF (below).

## TUI

Connected, nothing sent yet:

![TUI main screen, connected, empty](images/tui-main-connected.png)

Typed text lands in `Send:` as you type; nothing is sent until Enter:

![TUI main screen, typing a command](images/tui-main-typing.png)

Pressing Enter sends the line (with the configured line ending appended) and clears `Send:`
immediately; the reply appears in the output pane as soon as the device answers — no polling:

![TUI main screen, after a reply arrives](images/tui-main-after-reply.png)

The **Send as** menu in the menu bar picks how typed text is encoded (ASCII, UTF-8, hex, decimal,
octal, binary) — it starts as the profile's saved send format and can be changed at any time, even
mid-session, without reconnecting. The title bar shows the current one
(`dev-term — tcp://127.0.0.1:52311 (ascii; send as ascii)`) and names what you're connected to: the
**profile's name** if the connection is exactly one of your saved profiles
(`dev-term — tek2230 (ascii; send as ascii)`), otherwise its connection string (`tcp://host:port`,
`serial://COM3:4800,8,n,1`, `hid://vendor.product.serial`). It updates as soon as you switch
profiles from Device Profiles.

Ctrl+Q quits and closes the session cleanly.

## WPF

The same flow, after a reply has arrived. The **Send as:** drop-down beside the Send button is the
WPF equivalent of the TUI's Send as menu — it starts at the profile's saved send format and can be
changed per line:

![WPF main window, connected, showing a reply](images/wpf-main-window-connected.png)

`ID TEK/2230,V81.1,VERS:14` is the same real Tektronix 2230 reply text used above, captured here
against a `FakeTransport` that answers the same way rather than the real device.

## If a send fails

Same behavior across all three front ends: a device I/O failure (a timeout, a HID report whose
length doesn't match the device's exact frame size, etc.) appears as a line in the output instead of
crashing the app — see `ConnectionErrorMessages`/`CliMode`/`TuiMode.SendAsync`/
`MainWindow.SendCurrentInputAsync` in [`docs/design/platform.md`](../design/platform.md) and
`CLAUDE.md`'s constraints list for why the send path catches more than just `TimeoutException`.
