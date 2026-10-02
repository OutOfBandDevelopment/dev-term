# Rigol DG1062Z: known configuration

| | |
|---|---|
| Profile | none bundled |
| Transport | USBTMC, VID 0x1AB1, PID 0x0642 ("DG1000Z Serials") |

```bash
dotnet run --project src/DevTerm.Console -- --listusbtmcdevices true
```

Current state (2026-10-02, serial `DG1ZA232603118`): `*IDN?` answered on the first try:

```bash
dotnet run --project src/DevTerm.Console -- --transport usbtmc --vendorid 6833 --productid 1602 --serialnumber DG1ZA232603118 --presenter hex --parser ascii --lineending None --cli true
```

Reply: `Rigol Technologies,DG1062Z,DG1ZA232603118,03.01.12  ` (two trailing spaces) followed by LF (`0A`). Only the
identify was sent. Report: `docs/test/2026-10-02-12-06-32.md`. Earlier source: `docs/test/2026-09-24-07-16-34.md`
(not attempted there).
