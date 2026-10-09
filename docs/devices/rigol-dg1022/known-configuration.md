# Rigol DG1022: known configuration

| | |
|---|---|
| Profile | `rigol-dg1022.json` |
| Transport | USBTMC, VID 0x1AB1, PID 0x0588 (enumerates as "DG3000 SERIES", the same PID the DS1102E uses) |

```bash
dotnet run --project src/DevTerm.Console -- --listusbtmcdevices true
```

Current state (2026-10-02, serial `DG1D125306284`): `*IDN?` answered on the first try. It shares VID:PID `1AB1:0588`
with the DS1102E, so select it by serial number:

```bash
dotnet run --project src/DevTerm.Console -- --transport usbtmc --vendorid 6833 --productid 1416 --serialnumber DG1D125306284 --presenter hex --parser ascii --lineending None --cli true
```

Reply: `RIGOL TECHNOLOGIES,DG1022 ,DG1D125306284,,00.02.00.06.00.02.07` followed by LF (`0A`); note the trailing space
after `DG1022` and the empty fourth field. Only the identify was sent. Report: `docs/test/2026-10-02-12-06-32.md`.
Earlier source: `docs/test/2026-09-24-07-16-34.md` (not attempted there).

Current state (2026-10-09): the USB descriptor string "DG3000 SERIES" is this unit, a DG1022 (the `*IDN?` reply
above is the proof); there is no separate DG3000 on the bench. With `--presenter ascii --lineending Lf` and
`--serialnumber DG1D125306284`:

| Sent | Reply |
|---|---|
| `*IDN?` | `RIGOL TECHNOLOGIES,DG1022 ,DG1D125306284,,00.02.00.06.00.02.07` |
| `FUNCtion?` | `CH1:SIN` |
| `FREQuency?` | `1.000000e+04` |
| `VOLTage?` | `5.000000e+00` |
| `SYSTem:ERRor?` | `-110,"Undefined command word"` (answered; the queue held the error from the next row) |
| `:SYSTem:ERRor?` (leading colon) | no reply within 1000 ms; the USBTMC transport dropped the connection and reconnected on the next line, and the unit queued `-110` |

Quirk: this firmware does not accept the leading-colon form of `SYSTem:ERRor?`. Send it without the colon, as the
profile's `sysError` command does. Report: `docs/test/2026-10-09-09-12-00.md`.
