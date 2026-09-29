# 005: DE-5000 negative phase angle and D readings decode as huge positives

| | |
|---|---|
| **Severity** | High |
| **Status** | Fixed |
| **Confidence** | Confirmed (checked against the 4x1md/de5000_lcr_py reference) |
| **Area** | DevTerm.Devices.De5000 |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Devices.De5000/De5000Framer.cs:111`

```csharp
var secondaryValue = (frame[11] * 0x100 + frame[12]) * Math.Pow(10, -(frame[13] & 0b0000_0111));
```

## What happens
The 16-bit secondary value is never sign-extended. The reference implementation sign-extends it for `%` and `deg`
units (`if res['sec_units'] in ('%', 'deg') and val & 0x1000: val = val - 0x10000`; the `0x1000` looks like a typo
for `0x8000`).

## Failure scenario
θ = -45.0° (raw `0xFE3E`, one decimal place) decodes as `Theta=6508.6deg`. Negative phase is routine for
capacitors. The wrong number reaches both the output line and the panel's secondary indicator.

## Suggested fix
For `%` and `deg` secondary units, treat the value as signed: `(short)((frame[11] << 8) | frame[12])`.

## Tests to add
Frames with a negative θ and a negative D or %.

## Resolution
Fixed on 2026-09-26 (branch `dev/fix-bugs`): the secondary 16-bit value is now sign-extended via
`(short)` when `secondaryUnit is "%" or "deg"` (`De5000Framer.TryParse`,
`src/DevTerm.Devices.De5000/De5000Framer.cs`) — matching the reference implementation, which only
sign-extends for those two secondary units. Regression tests:
`DevTerm.Devices.De5000.Tests.De5000FramerTests.TryParse_NegativeSecondaryDegrees_DecodesAsANegativeValue`
and `TryParse_NegativeSecondaryPercent_DecodesAsANegativeValue`.
