# 061: The Radex One never replies because its serial connection uses the wrong baud rate

| | |
|---|---|
| **Severity** | High |
| **Status** | Fixed |
| **Confidence** | Reproduced (real hardware, COM8, 2026-09-26) |
| **Area** | DevTerm.Devices.RadexOne |
| **Created** | 2026-09-26 |
| **Found at commit** | `03fce1632ab2f4b8e790c245d68d6fde62574988` (`dev/fix-bugs`) |
| **Found by** | User report ("Did the radex work after the fixes? It hadn't been returning anything in the past") + real-hardware bench test on COM8 |

## Where
- `tests/DevTerm.Devices.RadexOne.Tests/RealHardwareRadexOneTests.cs:59` (`BaudRate = 2400`)
- `devterm.runsettings:133-141` (Radex One comment block, claiming "2400 8-N-1 ... real-hardware confirmed 2026-09-25")
- `src/DevTerm.Devices.RadexOne/RadexOneControlSurface.cs:10-11` (doc comment)
- `src/DevTerm.Devices.RadexOne/RadexOneFramer.cs:43` (doc comment)
- `src/DevTerm.Devices.RadexOne/RadexOneDecoder.cs:13` (doc comment)
- `docs/design/proposals/radex-one-protocol.md` (multiple locations: lines 13-15, 20-21, 152, 176-198)

None of these set the baud rate at the `SerialTransportOptions` level in shipped product code — the
Radex One has no CLI/manifest-driven default profile yet, so the only place the wrong value was
actually load-bearing was the real-hardware test. But every one of the doc comments above asserts
"2400 baud, real-hardware confirmed 2026-09-25" as settled fact, and that claim is what led directly
to the user's "it hadn't been returning anything" experience the one time this device's connection
*was* configured (by hand, at the documented baud) against the real unit.

## What happens
The 2026-09-25 pass that rewrote the Radex One module from a wrong HID assumption to the correct
serial transport also picked 2400 baud, based on the source reverse-engineering doc's prose rather
than a real-hardware check of the baud rate specifically (`docs/design/proposals/radex-one-protocol.md`
line ~192, before this fix: "`RealHardwareRadexOneTests` was fixed to use 2400 baud (was 9600) to
match the device's real serial settings" — a claim that was never actually verified against the real
device at that specific rate). Every later doc comment then repeated "real-hardware confirmed
2026-09-25" for the baud rate specifically, even though only the *transport kind* (serial vs. HID)
had actually been confirmed that day.

## Failure scenario
Connect to the real Radex One on COM8 at 2400 baud (the documented, "confirmed" setting) and send a
Read Data request: the device returns zero bytes, even after 5 seconds of listening, and even with
nothing sent at all (pure listen). At 4800 baud: also zero bytes. At **9600 baud**, sending the
identical request produces a structurally valid 34-byte reply
(`7AFF20801600010000004D80000800000C0000000E0000000201000010000000D3F6`) that decodes correctly
end-to-end through the real `Session`/`RadexOneDecoder`/`RadexOneControlSurface` pipeline to
`RADEX-ONE: CPM=15 Ambient=10 Accum=259`.

## Suggested fix
Correct every "2400 baud" claim to 9600 baud, dated 2026-09-26, and note it corrects the earlier,
unverified 2026-09-25 claim. Already done in this fix:
- `RealHardwareRadexOneTests.cs`: `BaudRate = 2400` → `9600`.
- `devterm.runsettings`'s Radex One comment.
- `RadexOneControlSurface.cs`, `RadexOneFramer.cs`, `RadexOneDecoder.cs` doc comments.
- `docs/design/proposals/radex-one-protocol.md`'s "Source", "Device", the PlantUML note, and "Status"
  sections.

No production code changes were needed — `RadexOneFramer`/`RadexOneExtensionCodec`/`RadexOneDecoder`
were all already correct; only the baud rate configuration/documentation was wrong.

## Tests to add
`RealHardwareRadexOneTests.RealDevice_ReadData_ReceivesADecodedReply` already exists and is the
regression test: it failed at 2400 baud (`Assert.IsNotNull(received)` — "No reply received from the
real device") and now passes at 9600 baud, receiving `RADEX-ONE: CPM=15 Ambient=10 Accum=259`.

## Resolution
Fixed on 2026-09-26 on `dev/fix-bugs`: corrected the Radex One's real-hardware baud rate from the
unverified "2400" to the actual, confirmed 9600 across `RealHardwareRadexOneTests.cs`,
`devterm.runsettings`, `RadexOneControlSurface.cs`, `RadexOneFramer.cs`, `RadexOneDecoder.cs`, and
`docs/design/proposals/radex-one-protocol.md`. Regression test:
`RealHardwareRadexOneTests.RealDevice_ReadData_ReceivesADecodedReply`, run against the real device on
COM8 — failed at 2400 baud (no reply), passes at 9600 baud (`RADEX-ONE: CPM=15 Ambient=10 Accum=259`).
See `docs/test/2026-09-26-15-57-38.md` for the full session transcript.
