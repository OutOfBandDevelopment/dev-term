# Rigol DM3058E: known configuration

| | |
|---|---|
| Profile | `rigol-dm3058e.json` |
| Transport | USBTMC, VID 0x1AB1 (6833), PID 0x09C4 (2500) |

```bash
dotnet run --project src/DevTerm.Console -- --listusbtmcdevices true
```

The 2026-09-24 bench report recorded a parked bulk-IN stall on USBTMC reads; check `docs/changes/` and
`docs/design/usbtmc-transport.md` for its current state before relying on it. Not otherwise verified here.
