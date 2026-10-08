# Python control-channel client

`echo_bot.py` shows the other way to extend dev-term from any language: instead of being started by the host (the stdin/stdout plugin protocol), a program attaches to a running session over the loopback HTTP control channel. It tails the `GET /events` stream (Server-Sent Events: `open`, `rx HEX`, `tx HEX`, `closed ...`) and sends with `POST /command` (`send <text>`, `sendhex <HEX>`, `ping`). Every request carries `Authorization: Bearer <token>`.

```
dotnet run --project src/DevTerm.Console -- --transport loopback --cli true --controlhttp 8765 --controltoken secret
python examples/python/control-channel-client/echo_bot.py 8765 secret
```

Type a line in the dev-term window; the loopback device answers `From Loopback test`, and the bot sends `world` back in reply. Checked 2026-10-07: `ping` returned `ok` and the `tx`/`rx`/`closed` events streamed. The same commands work over the named pipe (`--control <name>`) for programs that prefer it. Design: [cross-process-control-channel](../../../docs/design/proposals/cross-process-control-channel.md).
