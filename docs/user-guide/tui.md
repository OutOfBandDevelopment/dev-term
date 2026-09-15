# TUI

The full-screen terminal UI — the console app's default mode (no flags needed; `--cli true` or
`--tui false` gets you the [CLI](cli.md) instead). A single window: a scrolling output pane on top,
a `Send:` line at the bottom. See [`docs/design/frontends.md`](../design/frontends.md) for how this
relates to the CLI and WPF front ends, and its `@startsalt` mockup of the eventual multi-pane design
this is a first stub toward.

The three screens below are real Terminal.Gui screen buffers, read back exactly as the terminal
driver rendered them via `Application.Driver.GetOutputBuffer()` (see
[`docs/design/testing.md`](../design/testing.md)'s TUI automation section) — not drawn by hand.

## Connected, nothing sent yet

```
$ dotnet DevTerm.Console.dll --transport tcp --host 192.168.0.107 --tcpport 23 --presenter ascii
```

```
┌┤dev-term — TCP 192.168.0.107:23 (ascii) — Ctrl+Q to quit├────────────────────┐
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│Send:                                                                         │
└──────────────────────────────────────────────────────────────────────────────┘
```

The title bar shows the connection description and active presenter (see `ConnectionDescription`
in [`docs/design/platform.md`](../design/platform.md)); the output pane above `Send:` is empty
until something arrives or you send something.

## Typing a command

Typed text lands in the `Send:` field as you type; nothing is sent until you press Enter.

```
┌┤dev-term — TCP 192.168.0.107:23 (ascii) — Ctrl+Q to quit├────────────────────┐
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│Send: ID?                                                                     │
└──────────────────────────────────────────────────────────────────────────────┘
```

## After the reply arrives

Pressing Enter sends the line (with the configured line ending appended) and clears the `Send:`
field immediately; the reply appears in the output pane, prefixed with the presenter name, as soon
as the device answers — no polling or manual refresh:

```
┌┤dev-term — TCP 192.168.0.107:23 (ascii) — Ctrl+Q to quit├────────────────────┐
│[ascii] ID TEK/2230,V81.1,VERS:14                                             │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│                                                                              │
│Send:                                                                         │
└──────────────────────────────────────────────────────────────────────────────┘
```

(`ID TEK/2230,V81.1,VERS:14` is the project's real Tektronix 2230 test device's real reply to
`ID?` — captured here against a small throwaway stand-in that answers the same way, not the actual
device, but the response text itself is real.)

Ctrl+Q quits and closes the session cleanly.

## If a send fails

A device I/O failure (a timeout, a HID report whose length doesn't match the device's exact frame
size, etc.) appears as a line in the output pane instead of crashing the app — see
`ConnectionErrorMessages`/`TuiMode.SendAsync` in [`docs/design/platform.md`](../design/platform.md)
and `CLAUDE.md`'s constraints list for why the send path catches more than just `TimeoutException`.
