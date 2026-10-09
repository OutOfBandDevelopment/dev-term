# Korad KA3005P / KA6003P: known configuration

| | KA3005P | KA6003P |
|---|---|---|
| Profile | `korad-ka3005p.json` | `korad-ka6003p.json` |
| Transport | Serial, COM6 | Serial, COM7 |
| Framing | 9600 8N1, no handshake | 9600 8N1, no handshake |
| Line ending | `--lineending None` | `--lineending None` |
| `*IDN?` | `KORAD KA3005P V5.8 SN:03396447` | `KORAD KA6003P V5.8 SN:50266913` |

```bash
dotnet run --project src/DevTerm.Console -- --transport serial --port COM6 --baud 9600 --presenter hex --parser ascii --lineending None --cli true
```

Quirk: these supplies send no terminator, so the `ascii` presenter never flushes a reply; the 2026-09-24
bench pass used a terminatorless `RawPresenter`. That class is test-local
(`tests/DevTerm.Console.Tests`), not a console `--presenter` name (the catalog has ascii, utf8, hex, decimal, octal,
binary, scpi, manifest), so the command above uses `hex` and you decode the bytes by hand, or use
`--presenter scpi --scpiprofile "Korad KA3005P Power Supply"` (the profile's empty terminator flushes replies; this
is the mechanism confirmed on the DS1102E, and run on the KA3005P at COM6 on 2026-10-09: `*IDN?` came back as `[scpi] KORAD KA3005P V5.8 SN:03396447`; the KA6003P was not re-run). Source: `docs/test/2026-09-24-07-16-34.md`.
