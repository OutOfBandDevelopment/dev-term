# Tektronix TDS2024: known configuration

| | |
|---|---|
| Profile | `tektronix-tds2024.json` |
| Transport | TCP through a serial-to-Ethernet bridge, 192.168.0.110:23 |
| Bridge UART (read 2026-10-02) | 19200 baud, 8 data, no parity, 1 stop |
| Line ending | `--lineending Lf` per the 2026-09-25 notes and profile (the 2026-09-24 report ran `Cr`) |
| Pacing | `--writebytedelayms 50` |
| Identify | `*IDN?` -> `TEKTRONIX,TDS 2024,...` |

```bash
dotnet run --project src/DevTerm.Console -- --transport tcp --host 192.168.0.110 --port 23 --presenter ascii --lineending Lf --writebytedelayms 50 --cli true
```

Quirks: `TRIGger...?` queries never reply on this unit, so use `CH1?`/`CH2?`; the bridge can drop a command sent
immediately after connect (retry). Sources: `docs/changes/2026-09-24.md`, `2026-09-25.md`.
Bridge details: [usr-tcp232-302](../usr-tcp232-302/known-configurations.md).
