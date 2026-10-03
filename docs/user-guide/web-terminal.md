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

It listens on loopback only unless you set `Web:AllowRemote`, `Web:Token`, `Web:CertificatePath` and an `https` URL.
Every browser tab shares the one session. Field reference: [web terminal spec](../specs/web-terminal.md).
