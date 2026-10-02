# Tektronix 2230: known configuration

| | |
|---|---|
| Profile | `tektronix-2230.json` |
| Transport | TCP through a serial-to-Ethernet bridge, port 23 |
| Units | 192.168.0.108 (`ID TEK/2230,V81.1,VERS:13;`), 192.168.0.107 (`VERS:14`) |
| Bridge UART (read 2026-10-02) | .108: 4800 baud, 8 data, no parity, 1 stop. .107: read 4800, then changed to 9600 and re-read as 9600 8/None/1 (the unit's own PARAMETERS baud must match; with a 175 ms write delay it works well enough, bug 068) |
| Line ending | `--lineending Cr`, `--asciimaxlinelength 512` |
| Identify | `ID?` (pre-SCPI, not `*IDN?`) |

```bash
dotnet run --project src/DevTerm.Console -- --transport tcp --host 192.168.0.108 --port 23 --presenter ascii --lineending Cr --asciimaxlinelength 512 --cli true
```

The bridge UART must equal the scope's own PARAMETERS baud. The scope acts on a command as soon as it is recognised,
so bursts can be dropped; bug 068 tracks whether 9600 works with write pacing, bug 065 an HP-GL plot reply split.
The bench has one spare network cable, so usually only one of the two 2230s is online. Bridge details:
[usr-tcp232-302](../usr-tcp232-302/known-configurations.md).
