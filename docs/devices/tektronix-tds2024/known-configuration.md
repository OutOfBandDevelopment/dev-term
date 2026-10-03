# Tektronix TDS2024: known configuration

| | |
|---|---|
| Profile | `tektronix-tds2024.json` |
| Transport | TCP through a serial-to-Ethernet bridge, 192.168.0.110:23 |
| Bridge UART (read 2026-10-02) | 19200 baud, 8 data, no parity, 1 stop |
| Line ending | `--lineending Lf` per the 2026-09-25 notes and profile (the 2026-09-24 report ran `Cr`) |
| Pacing | `--writebytedelayms 50` (**required**; saved profile: `WriteByteDelayMs` 50) |
| Identify | `*IDN?` -> `TEKTRONIX,TDS 2024,...` |

```bash
dotnet run --project src/DevTerm.Console -- --transport tcp --host 192.168.0.110 --port 23 --presenter ascii --lineending Lf --writebytedelayms 50 --cli true
```

**The 50 ms write delay is required.** The scope and bridge have no input FIFO, so a command written as one burst
loses bytes. Without the delay, any query can get no reply (not just `TRIGger...?`, seen 2026-09-25) and leaves error
`363 Input buffer overrun`; with it, `HORizontal`, `DISplay`, `MEASUrement` and `ACQuire` queries all answer
(2026-10-03, `docs/test/2026-10-03-17-45-00.md`) and `TRIGger...?` queries answered on 2026-10-02
(`docs/test/2026-10-02-07-01-24.md`). Other quirk: the bridge can drop a command sent immediately after
connect (retry). `HARDCopy STARt` (BMP, RS232, 19200) returns a complete 320x240 8-bit BMP of about 78 KB
(`docs/test/2026-10-03-18-05-00.md`). Sources: `docs/changes/2026-09-24.md`, `2026-09-25.md`.
Bridge details: [usr-tcp232-302](../usr-tcp232-302/known-configurations.md).
