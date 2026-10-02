# Rigol DG1022: known configuration

| | |
|---|---|
| Profile | `rigol-dg1022.json` |
| Transport | USBTMC, VID 0x1AB1, PID 0x0588 (enumerates as "DG3000 SERIES", the same PID the DS1102E uses) |

```bash
dotnet run --project src/DevTerm.Console -- --listusbtmcdevices true
```

Source: `docs/test/2026-09-24-07-16-34.md` (not attempted there). Current state: [to fill in].
