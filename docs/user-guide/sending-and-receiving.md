# Sending commands and viewing replies

Once connected (see [Connecting to a device](connecting.md)), all three front ends do the same
thing: type a line, press Enter (or Send), see the decoded reply appear. Screenshots/transcripts
below are real captured output from the actual built app — see this folder's [README](README.md).

## CLI

```
$ dotnet DevTerm.Console.dll --transport tcp --host 192.168.0.107 --tcpport 23 --presenter ascii --lineending Cr --cli true
Connected to TCP 192.168.0.107:23 using 'ascii'.
Type a line and press Enter to send; Ctrl+C to exit.
ID?
[ascii] ID TEK/2230,V81.1,VERS:14
```

The banner and the `[ascii] ID TEK/2230,V81.1,VERS:14` line are real captured stdout (against a
small throwaway TCP stand-in that answers `ID?` exactly like the project's real Tektronix 2230 test
device does); `ID?` is what you'd type — a real terminal echoes your own keystrokes, which a piped
capture doesn't show. Every line gets the configured line ending appended (`--lineending Cr` above)
before it's sent. Ctrl+C exits; the session closes cleanly.

## TUI

Connected, nothing sent yet:

![TUI main screen, connected, empty](images/tui-main-connected.png)

Typed text lands in `Send:` as you type; nothing is sent until Enter:

![TUI main screen, typing a command](images/tui-main-typing.png)

Pressing Enter sends the line (with the configured line ending appended) and clears `Send:`
immediately; the reply appears in the output pane as soon as the device answers — no polling:

![TUI main screen, after a reply arrives](images/tui-main-after-reply.png)

Ctrl+Q quits and closes the session cleanly.

## WPF

The same flow, after a reply has arrived:

![WPF main window, connected, showing a reply](images/wpf-main-window-connected.png)

`ID TEK/2230,V81.1,VERS:14` is the same real Tektronix 2230 reply text used above, captured here
against a `FakeTransport` that answers the same way rather than the real device.

## If a send fails

Same behavior across all three front ends: a device I/O failure (a timeout, a HID report whose
length doesn't match the device's exact frame size, etc.) appears as a line in the output instead of
crashing the app — see `ConnectionErrorMessages`/`CliMode`/`TuiMode.SendAsync`/
`MainWindow.SendCurrentInputAsync` in [`docs/design/platform.md`](../design/platform.md) and
`CLAUDE.md`'s constraints list for why the send path catches more than just `TimeoutException`.
