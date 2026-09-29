# 047: The Radex One panel describes the device as USB HID

| | |
|---|---|
| **Severity** | Low |
| **Status** | Fixed |
| **Confidence** | Confirmed |
| **Area** | DevTerm.Devices.RadexOne (RadexOneUiDefinition) |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Devices.RadexOne/RadexOneUiDefinition.cs:17`

## What happens
The description says "USB HID geiger counter", contradicting the corrected serial transport.

## Suggested fix
Change the text to match the serial transport.

## Resolution
Fixed on 2026-09-29 on `dev/fix-bugs`: `RadexOneUiDefinition.Build`'s `Description`
(`src/DevTerm.Devices.RadexOne/RadexOneUiDefinition.cs:18`) now reads "Geiger counter over serial —
read live data, serial/version, and alarm settings.", matching the device's actual serial transport
(see `RadexOneFramer`'s own doc comment and [061](fixed/061-radexone-wrong-baud-rate.md)) instead of
the stale "USB HID geiger counter" wording. `RadexOneFramer.cs:53`'s doc comment already correctly
notes it is "not a USB HID device" and needed no change.

Regression test: `RadexOneUiDefinitionTests.Build_DescriptionDoesNotClaimUsbHid` (fails against the
pre-fix code — the description contained "USB HID"). Full `TestCategory=Unit` run green across the
whole solution (no regressions).
