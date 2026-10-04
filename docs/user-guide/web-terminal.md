# Using dev-term from a browser

Run the web host with the same connection flags as the console app:

```
dotnet run --project src/DevTerm.Web -- --transport loopback --presenter ascii --Web:Token demo-token
dev-term web: http://127.0.0.1:5080/?token=demo-token
```

Open that URL. The token is exchanged for a cookie and you land on the terminal page; type `hello` and the reply
appears. Real captured checks of the access rules (see `WebHostTests`):

```
GET /                      (no token)            -> 401
GET /api/status            (Bearer demo-token)   -> {"state":"Open","connection":"Loopback"}
GET /?token=demo-token                           -> 302 to /, cookie set
--Web:Urls http://0.0.0.0:5081                   -> 'http://0.0.0.0:5081' is not loopback, so it must use https.
```

Set `Web:Panel` to `k8055` or `busylight` and the page shows that device's control panel above the output, rendered from its
`UiDefinition` (buttons, toggles, sliders, numeric, choice and text fields; indicators show live values when the panel's presenter is selected; bar graphs and charts are listed as not
shown yet). Give a colleague `Web:ReadOnlyToken` and they see the same page with every control disabled, and typed lines are refused.

![The terminal page with the Busylight panel above the output](images/web-terminal-page.png)

It listens on loopback only unless you set `Web:AllowRemote`, `Web:Token`, `Web:CertificatePath` and an `https` URL.
Every browser tab shares the one session. Field reference: [web terminal spec](../specs/web-terminal.md).

## The Blazor panel page

With `--Web:Panel busylight` (or `k8055`), open `/panel` for a server-rendered control panel; it is the same panel the main page shows, using the same token. A read-only token shows it with the controls disabled.

![The /panel page](images/web-blazor-panel.png)

![The /panel page for a read-only viewer](images/web-blazor-panel-readonly.png)

These are real browser captures taken by `WebScreenshotTests`, which drives the installed Microsoft Edge through Playwright (Integration; Inconclusive without Edge), so re-run that class when the page changes.
