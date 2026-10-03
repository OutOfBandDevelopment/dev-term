# Web terminal (`DevTerm.Web`)

Reference for the browser front end. Intent and decisions: [proposal](../design/proposals/web-tunnel-blazor-frontend.md).

## Starting it

`dotnet run --project src/DevTerm.Web -- <connection flags> [--Web:Token <t>] [--Web:Urls <urls>]`. Connection flags and
saved profile are the same as the console app's. On start it prints `dev-term web: <url>/?token=<token>`.

## Settings (`Web` section; also `DEVTERM_Web__<Name>`)

| Setting | Default | Meaning |
|---|---|---|
| `Urls` | `http://127.0.0.1:5080` | `;`-separated listen URLs |
| `Token` | generated, printed once | Shared access token |
| `AllowRemote` | `false` | Permit a non-loopback URL |
| `CertificatePath` / `CertificatePassword` | none | PFX for `https` |
| `BacklogLines` | `500` | Lines replayed to a viewer that joins later |

A non-loopback URL needs `AllowRemote`, an explicit `Token`, a `CertificatePath` and `https`; otherwise startup fails
with the reason.

## Endpoints

| Path | Behaviour |
|---|---|
| `/` | The terminal page: status bar, output pane, send box |
| `/ws` | Text WebSocket. A frame received = one typed line to send; a frame sent = one output or status line (`!`-prefixed = error) |
| `/api/status` | `{"state": "...", "connection": "..."}` |

Every request needs the token (Bearer header, `devterm_auth` cookie, or `?token=` on a GET, which sets the cookie and
redirects). Missing or wrong: 401. A browser `Origin` that differs from the host: 403.

## Behaviour

All viewers share one session. Output goes to everyone; sends are serialized. A failed startup connect is shown in the
page and the next sent line retries, as in the TUI/WPF. Input that the parser cannot encode is reported to the sender
only.
