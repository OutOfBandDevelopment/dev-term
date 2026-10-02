# 068: The Tektronix 2230 bridge runs at 4800 baud; it can probably run at 9600 if dev-term paces its writes

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
| **Confidence** | Plausible (a maintainer's hypothesis; the 4800 figure comes from [065](065-hpgl-no-end-detection-splits-one-plot.md), the 9600 claim is untested) |
| **Area** | `tektronix-2230.json`, DevTerm.Transports.Tcp (`WriteByteDelayMs`), the 2230 serial-to-Ethernet bridge setup |
| **Created** | 2026-10-02 |
| **Found at commit** | `7a4544a378eee851002ee755ad1c01c758906048` (`dev/hardware-review`) |
| **Found by** | User report ("we can probably get it working at 9600 if we add a write delay") |

## Where
- `src/DevTerm.Devices.Scpi/Profiles/tektronix-2230.json`: documents the CR terminator and DIP-switch behavior but not the
  link speed or any pacing.
- `src/DevTerm.Transports.Tcp/TcpTransport.cs:90-91`: `WriteByteDelayMs` exists for TCP, default off.
- `tests/DevTerm.Console.Tests/RealHardwareTcpTests.cs:72-100`: the two 2230 tests (`RealTcpDeviceHost1`/`Host2`) use no delay.
- Units: 192.168.0.107 and 192.168.0.108, both behind serial-to-Ethernet bridges.

## What happens
The 2230's RS-232 option is pre-SCPI and "acts on a command as soon as it's recognized rather than waiting for the line
terminator" (profile Notes), so it is likely as sensitive to bursts as the TDS2024 was. The .108 unit's link runs at
4800 baud, roughly 480 bytes/s, which is why an HP-GL plot can pause past the 2 s idle timeout in 065. A reply also
arrived doubled once (`docs/changes/2026-09-23.md`), which would fit timing trouble. Running the link faster has not been
tried with paced writes.

## Failure scenario
Expected, to be confirmed: at 9600 baud with default pacing a command (for example `ID?` or `HORizontal?`) is sometimes
lost or garbled; with `WriteByteDelayMs` set (try 10 to 50 ms) it is reliable at 9600, halving transfer times and making
065 less likely.

## Suggested fix
1. Reproduce: set the bridge's serial speed to 9600 and the 2230's own PARAMETERS baud setting to match (read at
   power-up, so power-cycle it), then loop `ID?` / `CH1?` / `HORizontal?` at several `WriteByteDelayMs` values and count
   failures. Record it with the `hardware-test` skill.
2. If a delay makes 9600 reliable, add it to the 2230 real-hardware tests, record the speed and delay in the profile's
   Notes, and re-check 065 at the new speed.
3. If it does not, close this as `Won't fix`.

## Tests to add
Hardware-category tests for both 2230 hosts that run the query loop at 9600 with the chosen delay.

## Related
[065](065-hpgl-no-end-detection-splits-one-plot.md), [067](fixed/067-hp34401a-rs232-no-write-pacing.md).
