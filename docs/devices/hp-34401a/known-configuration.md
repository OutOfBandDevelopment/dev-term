# HP/Agilent/Keysight 34401A: known configuration

| | |
|---|---|
| Profile | `hp-agilent-keysight-34401a.json` |
| Transport | Serial, COM5 |
| Framing | 9600 baud, 8 data bits, no parity, **2 stop bits**, no handshake |
| Line ending | `--lineending Lf` |
| Presenter | `ascii` (it buffers to the LF terminator) |

```bash
dotnet run --project src/DevTerm.Console -- --transport serial --port COM5 --baud 9600 --databits 8 --parity None --stopbits Two --presenter ascii --lineending Lf --cli true
```

Also reachable through bridge 192.168.0.109 (planned, not yet working: bridge reads 1200 baud, meter is 9600). See [usr-tcp232-302](../usr-tcp232-302/known-configurations.md).

Quirk: a terminatorless `raw` presenter surfaces only the first fragment of a reply (`H` instead of the `*IDN?`
string); use `ascii`. Send `SYST:REM` first to enter remote mode. Source: `docs/test/2026-09-24-07-16-34.md`,
`docs/test/2026-09-25-18-57-22.md`. Verify the stop-bits argument spelling with `--help` before relying on the command
above; the matrix records "2 stop bits" but not the literal flag.
