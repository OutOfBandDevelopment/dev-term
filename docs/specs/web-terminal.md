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
| `ReadOnlyToken` | none | A second token for watch-only viewers; must differ from `Token`. Output and the panel layout are shown, every send over `/ws` and every `POST /api/invoke` is refused (`! read-only viewer`, HTTP 403) |
| `Panel` | none | `k8055` or `busylight`: serves that device's `UiDefinition` at `/api/panel` and renders it in the page above the output |

A non-loopback URL needs `AllowRemote`, an explicit `Token`, a `CertificatePath` and `https`; otherwise startup fails
with the reason.

## Endpoints

| Path | Behaviour |
|---|---|
| `/` | The terminal page: status bar, output pane, send box |
| `/ws` | Text WebSocket. A frame received = one typed line to send; a frame sent = one output or status line (`!`-prefixed = error) |
| `/api/status` | `{"state": "...", "connection": "...", "readOnly": false}` |
| `/api/panel` | The configured panel's `UiDefinition` as JSON (404 when `Panel` is unset) |
| `/api/project` | The `--project` file's connections as `[{"name", "description"}]` (no credentials); `[]` without a project |
| `GET /api/connections` | The extra connections currently open: `[{"id", "name", "state"}]` |
| `POST /api/connections?name=<n>` | Opens the named project connection as its own session behind `/ws/{id}`; returns `{"id", "name"}`; 404 for an unknown name, 403 for a read-only viewer |
| `DELETE /api/connections/{id}` | Closes it (204; 404 unknown id; 403 read-only) |
| `/ws/{id}` | The same text WebSocket as `/ws`, for one opened connection |
| `/api/devices` | Attached hardware: `{"serial": [...], "hid": [...], "usbtmc": [...]}`; a kind that cannot be enumerated returns `[]` |
| `/api/events` | Server-Sent Events: `connection-opened` `{id, name}`, `connection-closed` `{id}` and `line` `{id, text}` (`id` is `main` for the shared session). Each subscriber has a 256-event queue that drops its oldest |
| `POST /api/invoke` | `{"commandId": "...", "value": "..."}` through the panel's `IControlSurface`; 403 for a read-only viewer, 400 without a command id |

Every request needs the token (Bearer header, `devterm_auth` cookie, or `?token=` on a GET, which sets the cookie and
redirects). Missing or wrong: 401. A browser `Origin` that differs from the host: 403.

## Control over HTTP (`--controlhttp <port>`)

The same loopback channel as the console front ends (`POST /command`, `GET /events`, `GET /ping`) on the shared session,
with its own bearer token (`--controltoken`, generated if omitted). It is separate from the host token, so a script can
send commands without being able to open or close connections. The URL and token are printed at startup.

## Behaviour

All viewers share one session. Output goes to everyone; sends are serialized. A failed startup connect is shown in the
page and the next sent line retries, as in the TUI/WPF. Input that the parser cannot encode is reported to the sender
only.

## Blazor panel page (`/panel`)

Same token auth as every route. Renders the host's `Web:Panel` `UiDefinition` generically (sections as fieldsets; button, toggle, slider, numeric, choice, text field, indicator (showing the latest value the device's structured presenter published, e.g. the K8055 analog inputs, when that presenter is selected; otherwise its default); other kinds show a placeholder) and sends each change through the same `IControlSurface` as `/api/invoke`. A read-only token sees the page with every control disabled and a notice. With no `Web:Panel`, the page says none is configured.
