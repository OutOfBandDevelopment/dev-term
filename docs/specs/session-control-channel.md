# Session control channel

**Purpose.** Lets a second process or script read and drive a session one dev-term process already has open: a read-only
event pipe (`--pipe`), a read-write command pipe (`--control`) with a client (`--controlclient`), and a loopback HTTP variant
(`--controlhttp`). Works in the console CLI, the TUI and WPF (the first tab's session only); `DevTerm.Web` offers
`--controlhttp` for its shared session. Design and history: [cross-process-control-channel](../design/proposals/cross-process-control-channel.md).
The user-facing walkthrough is in [sending-and-receiving](../user-guide/sending-and-receiving.md).

## Flags

| Flag | Role | Notes |
|---|---|---|
| `--pipe <name>` | Host: read-only event pipe `devterm-session-{name}` | Clients can only read; anything they write is ignored |
| `--attach <name>` | Client for `--pipe`: prints `open`/`rx`/`tx`/`closed` lines with the ASCII beside each | Sees only traffic from when it connects. Exits 1 with a hint when nothing is listening |
| `--control <name>` | Host: read-write duplex pipe `devterm-control-{name}` | Limited to the current OS user (`PipeOptions.CurrentUserOnly`) |
| `--controlclient <name>` | Client for `--control`: each non-empty stdin line is a command; replies and events go to stdout | Exits 1 with "No session named ..." when no host accepts within the connect timeout; exits 0 when stdin ends |
| `--controlhttp <port>` | Host: the same commands on `http://127.0.0.1:<port>/` | Loopback only; bearer token printed at startup, or fixed with `--controltoken` |

## Commands (pipe and HTTP share `SessionCommands`)

One command per line; the verb is case-insensitive. Every command is answered with `ok` or `error <why>` (the reason is one line).

| Command | Does | Errors |
|---|---|---|
| `ping` | Replies `ok` | none |
| `send <text>` | Encodes the text with the host's parser and line ending (the same path as typing it) and sends it | `error <encoding message>` when the text cannot be encoded; `error <message>` when the send fails |
| `sendhex <HEX>` | Sends the bytes; spaces in the digits are ignored | `error sendhex needs an even number of hex digits` |
| anything else | n/a | `error unknown command '<verb>' (send, sendhex, ping)` |

`presenter add/remove` is not built.

## Events

The same lines as the read-only pipe stream on the control pipe, interleaved with replies: `open`, `rx <hex>`, `tx <hex>`,
`closed <reason>`. Over HTTP they are `GET /events` (Server-Sent Events); commands are `POST /command`, liveness `GET /ping`.

## States and limits

- Each client has a bounded queue of 1000 lines, drop-oldest, and its own writer task, so a slow client loses lines and never stalls the session's read loop.
- Several clients can connect at once; sends from all of them interleave through `Session.SendAsync`.
- The pipe name is whatever the host was given; there is no discovery of live names.
- The first tab's session only in the TUI and WPF; tabs added later are not published.

## Open items

- Naming and discovery of live sessions (see the proposal's open questions).
- The front-end (TUI/WPF) wiring has no test of its own; the channel itself is covered by Core's pipe and HTTP tests.
