# Rigol DM3058E: known configuration

| | |
|---|---|
| Profile | `rigol-dm3058e.json` |
| Transport | USBTMC, VID 0x1AB1 (6833), PID 0x09C4 (2500) |

```bash
dotnet run --project src/DevTerm.Console -- --listusbtmcdevices true
```

Current state (2026-10-02, serial `DM3R232301438`): `*IDN?` answered on the first try, no stall:

```bash
dotnet run --project src/DevTerm.Console -- --transport usbtmc --vendorid 6833 --productid 2500 --serialnumber DM3R232301438 --presenter hex --parser ascii --lineending None --cli true
```

Reply: `Rigol Technologies,DM3058E,DM3R232301438,01.01.00.02.03.01` followed by LF (`0A`). Only the identify was sent. The
2026-09-24 bench report recorded a parked bulk-IN stall on USBTMC reads; see `docs/design/usbtmc-transport.md` for it.
Report: `docs/test/2026-10-02-12-06-32.md`.

2026-10-09: all four USBTMC units enumerate (`--listusbtmcdevices true`: DM3058E, DS1ET, DG1022 as "DG3000 SERIES", DG1062Z). The DM3058E again answered `*IDN?` on the first try under the `ascii` presenter with LF: `Rigol Technologies,DM3058E,DM3R232301438,01.01.00.02.03.01`. No stall. The command was `--transport usbtmc --vendorid 6833 --productid 2500 --presenter ascii --lineending Lf --cli true`.
