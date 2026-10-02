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

## Through bridge 192.168.0.109 (working, 2026-10-02)

```bash
dotnet run --project src/DevTerm.Console -- --transport tcp --host 192.168.0.109 --port 23 --presenter ascii --parser ascii --lineending Lf --writebytedelayms 50 --cli true
```

Bridge UART 9600 8/None/2 (matches the meter). Sent `SYST:REM`, `*IDN?`, `SYST:ERR?`, `MEAS:VOLT:DC?`; replies:
`HEWLETT-PACKARD,34401A,0,5-1-1`, `-410,"Query INTERRUPTED"` (left over from the earlier blocked queries),
`+1.26283000E-04` (leads plugged in, no source: offset only). Repeated on a second run. No error beep.
A third run with the scope probes removed gave `HEWLETT-PACKARD,34401A,0,5-1-1`, `+0,"No error"` and
`+6.28130000E-05`; the front panel showed `0.0628 mV DC` with Rmt lit and ERROR dark, matching the bridge reply. Tester
switches for that run: DSR open, CTS jumpered to DSR, all others closed.

**What made it work is the DB-9 wiring, not the baud.** The meter is a DTE and uses a DTR/DSR handshake (programming
manual, "RS-232 Interface Configuration"): it drops DTR after receiving a query's newline until the reply is read, and
it **sends nothing while its DSR input (pin 6) is false**. The bridge never drives DSR true, so:

| Meter-side wiring | Result |
| --- | --- |
| Straight through, DTR/DSR switches closed, jumper from meter DTR to its own DSR | silent: the meter drops DTR after each query, DSR follows it, output suspended (a loopback jumper deadlocks) |
| DTR/DSR switches open, jumper removed | silent: DSR floating/driven false (scope showed a flat -7 V on DSR) |
| **DSR (pin 6) jumpered to CTS (pin 8)**, data lines straight through | **works** |

CTS (pin 8) is an output from the bridge (stated by the bench operator, not measured: the handshake-line scope captures
never triggered), so the jumper feeds a bridge-driven line into the meter's DSR. Its level was not captured; in the
COM10-to-bridge test CTS followed the inverse of the PC's DTR, so it may not be constant. Treat the jumper as an
empirical fix. The manual's 34398A cable (F1047-80002, DB-9 female both ends) is a null modem: 1-1, 2<->3,
4<->6, 5-5, 7<->8, 9-9. Handshake can also be disabled outright by leaving DTR unconnected and tying DSR true at
300/600/1200 baud.

Direct on a PC (COM10, Prolific USB-UART, 9600 8N2, `--stopbits Two`) it answers `*IDN?` with the same string, but only
with DTR and RTS asserted and all tester lines connected; with only RXD/TXD/GND it beeps (receives) and never
answers. See [usr-tcp232-302](../usr-tcp232-302/known-configurations.md).

Quirk: a terminatorless `raw` presenter surfaces only the first fragment of a reply (`H` instead of the `*IDN?`
string); use `ascii`. Send `SYST:REM` first to enter remote mode. Source: `docs/test/2026-09-24-07-16-34.md`,
`docs/test/2026-09-25-18-57-22.md`. Verify the stop-bits argument spelling with `--help` before relying on the command
above; the matrix records "2 stop bits" but not the literal flag.
