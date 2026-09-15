# CLI

The scriptable/interactive command-line mode — connect, type lines to send, see decoded replies
print to stdout. See [`docs/design/frontends.md`](../design/frontends.md) for how this relates to
the TUI and WPF front ends.

**The TUI is the console app's default mode** — add `--cli true` (or `--tui false`) to get the CLI
described here instead.

## Connect and exchange a line

```
$ dotnet DevTerm.Console.dll --transport tcp --host 192.168.0.107 --tcpport 23 --presenter ascii --lineending Cr --cli true
Connected to TCP 192.168.0.107:23 using 'ascii'.
Type a line and press Enter to send; Ctrl+C to exit.
ID?
[ascii] ID TEK/2230,V81.1,VERS:14
```

The banner and the `[ascii] ID TEK/2230,V81.1,VERS:14` line are real captured stdout from the built
app (against a small throwaway TCP stand-in that answers `ID?` exactly like the project's real
Tektronix 2230 test device does); the `ID?` line shown is what you'd type — a real terminal echoes
your own keystrokes, which a piped capture doesn't show.

Every line you type gets the configured line ending appended (`--lineending Cr` above) before it's
sent — see `CliOptions.LineEnding` in [`docs/design/platform.md`](../design/platform.md) for the
other options. Press Ctrl+C to exit; the session closes cleanly.

## Discover connected hardware

`--listports true` lists serial ports; `--listhiddevices true` lists USB HID devices. Both exit
immediately after printing (no connection is made). Real output from a development machine:

```
$ dotnet DevTerm.Console.dll --listports true
(no output — no serial ports currently attached)

$ dotnet DevTerm.Console.dll --listhiddevices true
046D:C08B  G502 HERO Gaming Mouse  SN:0E6A395F3531
0951:16DD  HyperX Alloy Core RGB
10CF:5502
046D:C08B  HID VHF Driver  SN:1.0
1462:7D25  MYSTIC LIGHT   SN:A02021081203
04D8:F848  BLL Lamp
```

The list is whatever's actually plugged in — keyboards, mice, and RGB controllers show up
alongside dev-term's actual target devices (`10CF:5502` is a Velleman K8055 I/O board;
`04D8:F848` is a Kuando Busylight, sold under the generic "BLL Lamp" HID product string — see
[`docs/design/proposals/velleman-k8055-protocol.md`](../design/proposals/velleman-k8055-protocol.md)
and
[`docs/design/proposals/kuando-busylight-protocol.md`](../design/proposals/kuando-busylight-protocol.md)).
Pass a listed device's vendor/product ID to `--transport hid --hidvendorid <n> --hidproductid <n>`
to connect to it — see `CLAUDE.md`'s Commands section for the full flag.

## Errors

An unrecognized transport prints the error and usage text to stderr and exits with code 1 rather
than hanging or crashing:

```
$ dotnet DevTerm.Console.dll --transport carrier-pigeon --cli true
Unknown transport 'carrier-pigeon'. Expected 'serial', 'tcp', or 'hid'.
Usage: dev-term --transport serial --port <name> [--baud <rate>] [--databits <5-8>] [--parity <name>] [--stopbits <name>] [--handshake <name>] [--dtr <bool>] [--rts <bool>] [--presenter <name>] [--lineending <None|Cr|Lf|CrLf>] [--asciimaxlinelength <n>] [--cli <bool>]
   or: dev-term --transport tcp (--host <host> | --listen true) --tcpport <port> [--presenter <name>] [--lineending <None|Cr|Lf|CrLf>] [--asciimaxlinelength <n>] [--cli <bool>]
   or: dev-term --transport hid --hidvendorid <n> --hidproductid <n> [--hidserialnumber <sn>] [--presenter <name>] [--lineending <None|Cr|Lf|CrLf>] [--asciimaxlinelength <n>] [--cli <bool>]
   or: dev-term --listports true
   or: dev-term --listhiddevices true
The full-screen TUI is the default mode; pass --cli true for the plain scriptable loop instead
(e.g. for automation/CI), or --tui false, equivalently.
Settings can also come from environment variables (DEVTERM_PORT, DEVTERM_BAUD, ...) or
from an untracked 'appsettings.Local.json' next to the app, for a saved default profile.
Command-line arguments always win, then environment variables, then the settings file.
```

This same usage text (with exit code 1) also appears for missing required arguments — e.g.
`--transport tcp` with no `--host`/`--listen`/`--tcpport`.

A connection failure (device offline, wrong host/port, wrong serial port) is reported the same
way — see `ConnectionErrorMessages` in [`docs/design/platform.md`](../design/platform.md) for the
hint text each transport gives.
