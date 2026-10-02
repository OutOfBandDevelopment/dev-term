# Korad KA3005P / KA6003P: known configuration

| | KA3005P | KA6003P |
|---|---|---|
| Profile | `korad-ka3005p.json` | `korad-ka6003p.json` |
| Transport | Serial, COM6 | Serial, COM7 |
| Framing | 9600 8N1, no handshake | 9600 8N1, no handshake |
| Line ending | `--lineending None` | `--lineending None` |
| `*IDN?` | `KORAD KA3005P V5.8 SN:03396447` | `KORAD KA6003P V5.8 SN:50266913` |

```bash
dotnet run --project src/DevTerm.Console -- --transport serial --port COM6 --baud 9600 --presenter raw --lineending None --cli true
```

Quirk: these supplies send no terminator, so the `ascii` presenter never flushes a reply; use the terminatorless
`raw` presenter. Source: `docs/test/2026-09-24-07-16-34.md`.
