# 067: HP 34401A over RS-232 is not driven with write pacing, and probably needs a per-byte delay to be reliable

| | |
|---|---|
| **Severity** | Low |
| **Status** | Fixed |
| **Confidence** | Plausible (a maintainer's hypothesis, not yet reproduced; no logged failure is attributed to byte timing) |
| **Area** | DevTerm.Transports.Serial (`WriteByteDelayMs`), `hp-agilent-keysight-34401a.json`, `RealHardwareSerialTests` |
| **Created** | 2026-10-02 |
| **Found at commit** | `7a4544a378eee851002ee755ad1c01c758906048` (`dev/hardware-review`) |
| **Found by** | User report ("it probably needs a byte delay added to get it working more reliably") |

## Where
- `src/DevTerm.Transports.Serial/SerialTransport.cs:73-74`: pacing exists (`WriteByteDelayMs`, default -1 = off) but
  nothing configures it for this instrument.
- `src/DevTerm.Devices.Scpi/Profiles/hp-agilent-keysight-34401a.json`: its Notes record 9600 8N2, no handshake, LF, and
  the remote-mode requirement, but nothing about write pacing.
- `tests/DevTerm.Console.Tests/RealHardwareSerialTests.cs:76`
  (`CliMode_AgainstHp34401a_AnswersIdentityAndMeasuresVoltage`) and `devterm.runsettings:78-84`: the bench settings for
  the 34401A carry no delay.

## What happens
A command is written to the serial port as one burst. The 34401A's RS-232 interface has a small input buffer and no
hardware handshake in the documented setup, so a burst can outrun it. The TDS2024 behaved exactly this way: its
`TRIGger...?` queries were silent until `--writebytedelayms 50` was set (`docs/test/2026-10-02-07-01-24.md`,
`docs/test/2026-10-02-07-35-03.md`). The 34401A has a related history, but none of it is a byte-timing failure: replies
logged as single characters (`docs/test/2026-09-25-15-02-44.md`, a harness reading problem) and a
`+550 "Command not allowed in local"` entry (a missing `SYSTem:REMote`). So the hypothesis rests on the instrument class
and the TDS2024 precedent, not on a recorded failure.

## Update 2026-10-02: the 34401A does use DTR/DSR handshake
Through bridge 192.168.0.109 the meter sent nothing until the bridge's CTS was wired to the meter's DSR; it suspends
output while its DSR input is false (`docs/test/2026-10-02-11-21-45.md`). So "no hardware handshake in the documented
setup" above is not true for every path, and a silent or partial reply may be handshake, not byte timing. That run used
`--writebytedelayms 50` and did not isolate whether the delay was needed, so this report is still open and still
unreproduced. Step 1 of the suggested fix should be run on the DSR-wired path.

## Failure scenario
Expected, to be confirmed: with default pacing, a command sent right after connect, or several sent back to back, is
sometimes partly lost, giving a missing reply, a `-102 Syntax error` or `-113 Undefined header` in `SYST:ERR?`, or a
wrong reading. With a per-byte delay (try 5, 10 and 50 ms) the same sequence is reliable.

## Suggested fix
1. Reproduce first: on the real unit (9600 8N2), loop `*IDN?` / `MEAS:VOLT:DC?` / `SYST:ERR?` a few hundred times at
   `--writebytedelayms` -1, 5, 10 and 50, counting missing replies and queue errors. Record it with the `hardware-test`
   skill.
2. If it reproduces, set the smallest reliable delay in the real-hardware test's settings and record it in the profile's
   Notes. Whether a profile should be able to declare a default write delay (so a user need not know the flag) is the
   design question to raise.
3. If it does not reproduce, close this as `Won't fix`, as with [066](066-ds1102e-usbtmc-missing-zlp-at-packet-boundary.md).

## Tests to add
A `[TestCategory(TestCategories.Hardware)]` loop in `RealHardwareSerialTests` for the 34401A that fails on any missing
reply or non-empty `SYST:ERR?`, run with and without the delay.

## Related
[065](../065-hpgl-no-end-detection-splits-one-plot.md) and [068](../068-tek2230-bridge-runs-at-4800-baud.md) concern the same
class of slow-instrument timing.

## Resolution
Resolved 2026-10-02 on the bench, no dev-term code change. Through bridge 192.168.0.109 the meter answers reliably with
`--writebytedelayms 50` and the bridge's CTS (pin 8) wired to the meter's DSR (pin 6); three runs gave clean `*IDN?`,
`MEAS:VOLT:DC?` and `SYST:ERR?` (`docs/test/2026-10-02-11-21-45.md`). The cause of the silence was the DSR handshake,
not byte timing. Caveat: the delay was on for every working run, so whether 50 ms is *required* (versus merely
sufficient) was not isolated. Recorded in `docs/devices/hp-34401a/known-configuration.md`. No regression test: it is a
wiring and bench-setting fix.
