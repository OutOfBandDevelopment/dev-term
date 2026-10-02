# USR-TCP232-302 bridges: known configurations

The bench reaches several RS-232 instruments through USR-IOT serial-to-Ethernet bridges. dev-term sees each as a
plain `--transport tcp` connection on port 23; the bridge's own UART settings (set on its web UI) must match the
instrument's serial settings or the instrument sees garbage. Read them with
[`Get-UsrBridgeSettings.ps1`](Get-UsrBridgeSettings.ps1) (read-only HTTP GETs of the status, IP, Serial Port and
Expand Function pages; the web UI login is the vendor default, see [Index Function Example.md](Index%20Function%20Example.md)).

```powershell
.\Get-UsrBridgeSettings.ps1 -HostName 192.168.0.107,192.168.0.108,192.168.0.109,192.168.0.110 -Format Markdown
```

## Settings read from the bridges (2026-10-02)

Read with the script above, not typed from memory.

| Bridge | Module | Behind it | IP config | Work mode | Local port | UART | RFC2217-like |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 192.168.0.107 | RS232-1 (MAC 9c-a5-25-a8-6e-3a) | Tektronix 2230 (second unit) | DHCP | TCP Server | 23 | 4800 8/None/1 | on (box checked) |
| 192.168.0.108 | RS232-2 (fw 4018, MAC 9c-a5-25-a8-6e-80) | Tektronix 2230 | DHCP | TCP Server | 23 | 4800 8/None/1 | on (box checked) |
| 192.168.0.109 | USR-TCP232-302 (MAC 9c-a5-25-a8-6e-83) | HP 34401A (meter set to 9600; **bridge does not match**) | static | TCP Server | 23 | 1200 8/None/2 | on |
| 192.168.0.110 | USR-TCP232-302 (MAC 9c-a5-25-a8-6e-86) | Tektronix TDS2024 | DHCP | TCP Server | 23 | 19200 8/None/1 | on |

Common to .107/.108/.109/.110: remote (target) address 192.168.0.201 and remote port 8234 (unused in TCP Server mode).
"UART Set Parameter" (Expand Function page) is on for .109 only. The "Similar RFC2217" box on the Serial Port page is
on for all three; what that does on the wire is unverified here (see `RFC2217-like Function Example.md` and
`docs/design/rfc2217.md`).

The .108 and .110 units show "DHCP" in the IP page while answering on .108/.110, so those addresses come from a
router reservation, not the module. If the router lease changes, the profiles in
`src/DevTerm.Console/Properties/launchSettings.json` and `devterm.runsettings` need the new address.

## What dev-term needs to match

| Behind the bridge | dev-term arguments | Bridge UART must equal the instrument's | Source |
| --- | --- | --- | --- |
| Tektronix 2230 (.108) | `--transport tcp --host 192.168.0.108 --port 23 --presenter ascii --lineending Cr` | its PARAMETERS baud setting. Runs at 4800; bug 068 asks whether 9600 works with write pacing | [068](../../bugs/068-tek2230-bridge-runs-at-4800-baud.md) |
| Tektronix TDS2024 (.110) | `--transport tcp --host 192.168.0.110 --port 23 --presenter ascii --lineending Lf --writebytedelayms 50` | the scope's RS-232 baud (19200 as read) | [2026-09-25](../../changes/2026-09-25.md) |

The TDS2024 hangs on `TRIGger...?` queries; use `CH1?`/`CH2?`. Earlier reports ran it with `Cr`; the profile and the
2026-09-25 notes use `Lf`. Both are recorded here because they differ.

The older `launchSettings.json` TCP profiles for .107-.110 all use `--lineending Cr --asciimaxlinelength 512`.

## .109 and the HP 34401A (not working yet)

The HP 34401A behind .109 is configured for **9600 baud** (on COM5 it runs 9600 8N2, see
[hp-34401a](../hp-34401a/known-configuration.md)), but the bridge's UART reads **1200 8N2** (2026-10-02). With the
bridge at 1200 the meter's 9600 replies arrive as garbage or nothing, which is the most likely reason it doesn't
work yet. Fix: set the bridge's Serial Port baud to 9600 (keeping 8/None/2) on its web UI; not done here since the
script is read-only. Then use `--lineending Lf`, `--presenter ascii`, and send `SYST:REM` first.
Check the meter's own parity/stop-bit setting matches (8N2 on COM5; its front panel can differ).

## Not recorded anywhere yet

- Current state of the USBTMC Rigol units.
