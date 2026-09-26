# 047: The Radex One panel describes the device as USB HID

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
| **Confidence** | Confirmed |
| **Area** | DevTerm.Devices.RadexOne (RadexOneUiDefinition) |
| **Found** | 2026-09-26 code review of `main` @ `42758db` (branch `dev/review-code`); static review, not yet reproduced |

## Where
`src/DevTerm.Devices.RadexOne/RadexOneUiDefinition.cs:17`

## What happens
The description says "USB HID geiger counter", contradicting the corrected serial transport.

## Suggested fix
Change the text to match the serial transport.
