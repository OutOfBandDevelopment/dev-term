# 047: The Radex One panel describes the device as USB HID

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
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
