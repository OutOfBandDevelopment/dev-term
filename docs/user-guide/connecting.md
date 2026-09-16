# Connecting to a device

How you get from "just started dev-term" to "connected, ready to send" — three ways in: command-line
flags (CLI), or the shared Connection Editor screen (TUI/WPF), which also opens automatically at
startup when no valid connection is configured. See
[Managing connection profiles](managing-profiles.md) for saving/loading/deleting/importing/exporting
what you set up here, and [`docs/specs/connection-editor.md`](../specs/connection-editor.md) for the
full field-by-field reference. Screenshots below are real, automated captures — see this folder's
[README](README.md) for how.

## CLI: flags

```
$ dotnet DevTerm.Console.dll --transport tcp --host 192.168.0.107 --tcpport 23 --presenter ascii --lineending Cr --cli true
Connected to TCP 192.168.0.107:23 using 'ascii'.
Type a line and press Enter to send; Ctrl+C to exit.
```

No editor screen in CLI mode — settings come from flags, environment variables (`DEVTERM_*`), or a
saved `appsettings.Local.json` profile, in that precedence order (flags win). See `CLAUDE.md`'s
Commands section for the full flag list per transport.

### Discovering hardware first

`--listports true` lists serial ports; `--listhiddevices true` lists USB HID devices. Both exit
immediately, no connection made — real output from a development machine:

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

The list is whatever's actually plugged in (`10CF:5502` is a Velleman K8055 I/O board; `04D8:F848`
is a Kuando Busylight sold under the generic "BLL Lamp" HID product string — see
[`docs/design/proposals/velleman-k8055-protocol.md`](../design/proposals/velleman-k8055-protocol.md)
and
[`docs/design/proposals/kuando-busylight-protocol.md`](../design/proposals/kuando-busylight-protocol.md)).
Pass a listed vendor/product ID to `--transport hid --hidvendorid <n> --hidproductid <n>` to connect.

### Errors

An unrecognized transport, or missing required arguments, print usage text to stderr and exit 1
rather than hanging:

```
$ dotnet DevTerm.Console.dll --transport carrier-pigeon --cli true
Unknown transport 'carrier-pigeon'. Expected 'serial', 'tcp', or 'hid'.
Usage: dev-term --transport serial --port <name> [--baud <rate>] [--databits <5-8>] [--parity <name>] [--stopbits <name>] [--handshake <name>] [--dtr <bool>] [--rts <bool>] [--presenter <name>] [--lineending <None|Cr|Lf|CrLf>] [--asciimaxlinelength <n>] [--cli <bool>]
   or: dev-term --transport tcp (--host <host> | --listen true) --tcpport <port> [--presenter <name>] [--lineending <None|Cr|Lf|CrLf>] [--asciimaxlinelength <n>] [--cli <bool>]
   or: dev-term --transport hid --hidvendorid <n> --hidproductid <n> [--hidserialnumber <sn>] [--presenter <name>] [--lineending <None|Cr|Lf|CrLf>] [--asciimaxlinelength <n>] [--cli <bool>]
   or: dev-term --listports true
   or: dev-term --listhiddevices true
```

A real connection failure (device offline, wrong host/port, wrong serial port) is reported the same
way — see `ConnectionErrorMessages` in [`docs/design/platform.md`](../design/platform.md).

## TUI and WPF: the Connection Editor

Both front ends open the same Connection Editor screen at startup whenever no valid connection is
configured — no flags, no saved profile, or an invalid one. It's also reachable any time from
**File > Device Profiles...**. Selecting a Transport shows only that transport's fields.

**Serial** (TUI, then WPF):

![TUI connection editor, serial transport](images/tui-configure-serial.png)

![WPF connection editor, serial transport](images/wpf-device-profiles-serial.png)

**TCP**:

![TUI connection editor, TCP transport](images/tui-configure-tcp.png)

![WPF connection editor, TCP transport](images/wpf-device-profiles-tcp.png)

**USB HID**:

![TUI connection editor, HID transport](images/tui-configure-hid.png)

![WPF connection editor, HID transport](images/wpf-device-profiles-hid.png)

Filling in the fields and pressing **Connect** validates them and connects immediately — no need to
save a profile first (Save is for reusing the setup later; see
[Managing connection profiles](managing-profiles.md)).

### A TUI-specific limitation to know about

Notice the TUI's TCP/HID captures above have blank rows where the Serial fields used to be:
Terminal.Gui's absolute `Pos.Bottom(view)` positioning is computed from a view's frame regardless of
its `Visible` state, so hiding a field group doesn't let anything below it move up to fill the gap
(WPF's `Grid`/`StackPanel` does this automatically). Also not visible in any TUI capture above: the
Line ending/Save/Import-export controls and the Connect/Quit buttons — the default window is tall
enough for the fields shown but not the whole form, and it doesn't scroll its own content yet (see
[`docs/specs/connection-editor.md`](../specs/connection-editor.md)'s Open items — Terminal.Gui does
support real scrollbars, just not wired up here).
