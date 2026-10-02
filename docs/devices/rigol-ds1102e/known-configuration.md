# Rigol DS1102E: known configuration

| | |
|---|---|
| Profile | none bundled; closest is `rigol-ds1105e.json` (a DS1105E, not verified interchangeable) |
| Transport | USBTMC, VID 0x1AB1, PID 0x0588 (enumerates as "DS1000 SERIES") |
| Line ending | none: the device sends no terminator, so use the terminatorless `raw` presenter when over serial |

```bash
dotnet run --project src/DevTerm.Console -- --listusbtmcdevices true
```

Source: `docs/test/2026-09-24-07-16-34.md` (not attempted there). Current state: [to fill in].
