# Rigol DS1102E: known configuration

| | |
|---|---|
| Profile | `rigol-ds1102e.json` (bundled; matches the `DS1102E` in the `*IDN?` reply) |
| Transport | USBTMC, VID 0x1AB1, PID 0x0588 (enumerates as "DS1000 SERIES") |
| Line ending | none needed either way: sending ``, `
`, `
` or nothing after `*IDN?` gave the identical reply (checked 2026-10-02), and the reply carries no terminator, so the `ascii` presenter buffers it forever. Use `--lineending None` with `--presenter hex`, or `--presenter scpi --scpiprofile "Rigol DS1102E Oscilloscope"` to get the reply as text (the profile's empty terminator is what makes the `scpi` presenter print it) |

```bash
dotnet run --project src/DevTerm.Console -- --listusbtmcdevices true
```

Current state (2026-10-02, serial `DS1ET180300759`): reachable.

```bash
dotnet run --project src/DevTerm.Console -- --transport usbtmc --vendorid 6833 --productid 1416 --serialnumber DS1ET180300759 --presenter hex --parser ascii --lineending None --cli true
```

`*IDN?` reply: `Rigol Technologies,DS1102E,DS1ET180300759,00.04.02.01.00`. It sends **no terminator**, so the `ascii`
presenter prints nothing; use `hex` and decode by hand (`raw` is not a presenter name). Remote mode locks the front
panel, and `:KEY:FORC` (no reply) hands it back. Rapid-fire commands timed out the session once. `:TRIG:SWE SING` left
the trigger in AUTO. Used on the bench as a 2-channel handshake-line monitor (CH1 CTS, CH2 RTS) on the 34401A's RS-232.
Earlier source: `docs/test/2026-09-24-07-16-34.md` (not attempted there).

Current state (2026-10-09): still reachable, now with text replies. `--lineending None --presenter scpi --scpiprofile "Rigol DS1102E Oscilloscope"`
answered `*IDN?`, `:ACQuire:TYPE?` (`NORMAL`), `:TIMebase:SCALe?` (`5.000e-05`), `:CHANnel1:SCALe?` (`2.000e+00`) and
`:TRIGger:MODE?` (`EDGE`) first try. Report: `docs/test/2026-10-09-09-12-00.md`.
