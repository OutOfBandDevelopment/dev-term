# Proposal: DER EE DE-5000 LCR Meter — Optical-UART-over-BLE Decoder

## Source

Unlike the other three proposals in this directory, this one isn't sourced from
`mwwhited-notes/shared` prior art — it's reverse-engineered by the wider hobbyist community and
independently verified here (2026-09-15) via web research:

- [4x1md/de5000_lcr_py](https://github.com/4x1md/de5000_lcr_py) — Python library with a full
  byte-by-byte protocol breakdown; the primary source for the packet format below.
- [EEVblog: "Decoding DER EE LCR Meter DE-5000 serial bitstream"](https://www.eevblog.com/forum/testgear/decoding-der-ee-lcr-meter-de-5000-serial-bitstream/) —
  the original community reverse-engineering thread.
- The meter uses the **Cyrustek ES51919** chipset, also used in other rebadged LCR meters — the
  decoder below is arguably a decoder for that chipset's output format, not just this one branded
  unit, the same "one decoder, many devices" shape SCPI and Radex One's framing already have.

The DE-5000 is already in the local equipment inventory (`shared/.personal/incoming/test-equipment.md`)
alongside two other handheld LCR meters (Holdpeak HP-4070L, Altnux LCR Meter) that show no
documented computer interface — this is the one of the three that actually has one.

## Device

[DER EE DE-5000](https://www.deree.com.tw/de-5000-lcr-meter.html) — a handheld LCR meter with a
**fully isolated optical (IR) output**, not a wired port: the meter transmits UART frames over IR,
requiring an IR photodiode/phototransistor receiver in the physical chain (either DER EE's own
IR-to-USB adapter, or a self-built one — see below). Serial parameters at the UART level: 9600
baud, 8-N-1.

## Physical/transport chain (the part that makes this proposal unusual)

This isn't "plug in a cable" — three link-layer hops sit between the meter and dev-term:

1. **DE-5000 optical output** — UART frames, 9600-8N1, transmitted via IR.
2. **A custom-built optical-to-BLE adapter** (already built) — receives the IR UART stream and
   re-transmits it over BLE. Exactly which GATT profile it exposes (Nordic UART Service, per
   [transports.md](../transports.md)'s "BLE Serial" default, or a custom one) is **not yet known**
   here and needs confirming before the BLE Serial transport can be pointed at it.
2. **dev-term's BLE transport** ("BLE Serial" mode — see [transports.md](../transports.md)),
   itself not yet built.
3. **This decoder** — parses the same ES51919 UART frame the meter always sent; nothing about the
   frame format changes because it arrived over BLE instead of a direct wired UART. The decoder is
   transport-agnostic by construction (same pattern as every other presenter in this project — see
   [architecture.md](../architecture.md)), so it would work identically if the meter were ever
   connected via a direct wired IR-to-USB adapter instead.

This means the proposal is gated on the **BLE Serial transport** (backlog, cross-platform,
adapter-seam design already decided) being built first — see `BACKLOG.md`. Unlike Radex One (gated on
a transport not yet designed in any real detail) and Favero (gated on hardware access that's gone),
this one is gated on a transport that's already scoped and has real target hardware pushing for it.

## Why this is a good decoder candidate

- **Fully specified, fixed-size, checksum-free framing** — a constant 17-byte packet with known
  header (`0x00 0x0D`) and footer bytes, no length field to parse, no variable extension. About as
  simple as binary framing gets.
- **A mirrored sub-structure worth naming as its own concept**: the primary measurement
  (quantity/value/decimal-multiplier-and-units/display-status, bytes 5–9) and secondary measurement
  (D/Q/ESR-RP/phase-angle, same shape, bytes 10–14) are the *same* 5-byte record type appearing
  twice per packet — a reasonable candidate for a small shared "measurement record" sub-parser
  reused twice, rather than two independent field-by-field parses.
- **Chipset-level reuse**: because the format belongs to the ES51919 chipset rather than
  DER EE specifically, this decoder plausibly works unmodified against other rebadged meters using
  the same chip, without knowing all of them in advance.

## Protocol summary (full detail in the source repo)

**Packet shape** — fixed 17 bytes, no checksum:

```
Byte 0      : Header, always 0x00
Byte 1      : Header, always 0x0D
Byte 2      : Flags — bit0 hold, bit1 delta-reference-shown, bit2 delta mode, bit3 calibration
              mode, bit4 sorting mode, bit5 LCR-auto mode, bit6 auto-range mode, bit7 parallel
              measurement
Byte 3      : Config — bits 5-7 encode test frequency (100Hz/120Hz/1kHz/10kHz/100kHz/DC)
Byte 4      : Sorting-mode tolerance (0-10 -> ±0.25% .. -20/+80%)
Byte 5      : Primary measured quantity (L/C/R/DCR)
Bytes 6-7   : Primary value, MSB/LSB
Byte 8      : Primary — bits 0-2 decimal-point multiplier (10^-val), bits 3-7 units
Byte 9      : Primary display status (normal/blank/lines/OL/PASS/FAIL/...)
Byte 10     : Secondary measured quantity (D/Q/ESR-RP/angle)
Bytes 11-12 : Secondary value, MSB/LSB
Byte 13     : Secondary — same multiplier/units shape as byte 8
Byte 14     : Secondary display status
Bytes 15-16 : Footer — 0x0D 0x0A (see "Resolved discrepancies" below).
```

**Value calculation** (both measurements): `(MSB * 0x100 + LSB) * 10^-multiplier`.

## Resolved discrepancies (2026-09-25)

The source README disagreed with itself in two places; both are resolved against
[4x1md/de5000_lcr_py](https://github.com/4x1md/de5000_lcr_py)'s actual parsing code
(`src/de5000.py`), not just its prose, per this codebase's "verify against real reference code"
convention:

- **Footer bytes are `0x0D 0x0A`**, not `0x0D 0x0D` — the README's own summary line said `0x0D 0x0D`,
  but its byte-by-byte table said `(0x0D, 0x0A)`; the code's `is_data_valid` check
  (`raw_data[15] != '\x0D' or raw_data[16] != '\x0A'`) confirms the table, not the summary.
- **Value calculation is `MSB * 0x100 + LSB`**, not `MSB * 0x10000 + LSB` — `0x10000` doesn't fit in
  a 2-byte value and would be wrong by a factor of 256; the code does `raw_data[i] * 0x100 + raw_data[i + 1]`.

Also confirmed while implementing (see `De5000Framer.cs`'s doc comment): the secondary status byte
(14) is masked to 3 bits, not the 4 bits the primary status byte (9) uses — so FAIL/OPEn/Srt (status
codes 8-10) are only reachable for the primary reading, never the secondary one. This is a real
asymmetry in the source, not an oversight.

## Proposed shape

```plantuml
@startuml
skinparam componentStyle rectangle
skinparam backgroundColor #FEFEFE

package "Physical chain (outside dev-term)" {
  [DE-5000 IR output] as meter
  note bottom of meter : UART 9600-8N1 over IR
  [Custom optical-to-BLE adapter] as adapter
  note bottom of adapter : Already built;\nGATT profile TBD
}

package "DE-5000 Decoder (plugin)" {
  [DE-5000 Framer] <<internal>> as framer
  note bottom of framer : Fixed 17-byte packet,\nheader/footer validation
  [DE-5000 Decoder] <<IPresenter>> as decoder
  note bottom of decoder : Primary/secondary measurement,\nflags, frequency
  [Flag Channels (x7)] <<Sub-presenter>> as flags
  note bottom of flags : Boolean, named per bit\n(hold, delta, calibration, ...)
}

[Session / Transport] <<ITransport>> as transport
note right of transport : BLE Serial (not yet built —\nsee transports.md)

meter --> adapter : IR photodiode
adapter --> transport : BLE (GATT profile TBD)
transport --> framer : Bytes in
framer --> decoder : Validated 17-byte packet
decoder --> flags : Byte 2 bits
decoder --> decoder : Human-readable text baseline\n(e.g. "L=1.234mH D=0.012 @1kHz")
@enduml
```

## Status

Implemented (2026-09-25): `DevTerm.Devices.De5000` — `De5000Framer`/`De5000Decoder` (stream-buffering
around the fixed 17-byte packet, resyncing one byte at a time on a header/footer mismatch),
`De5000ControlSurface` (a deliberate no-op — the meter has no writable commands), and
`De5000UiDefinition` (read-only indicators). Wired into both front ends' Device menu, gated on "any
BLE connection" (`DevicePanels.De5000`). Unit-tested (`tests/DevTerm.Devices.De5000.Tests`) against
synthetic packets; **not yet verified against the real meter/adapter** — see the open question below.

## Open questions

- **What GATT profile the custom BLE adapter exposes** — Nordic UART Service (the default
  `transports.md`'s BLE Serial mode assumes) or a custom one. Still unconfirmed; a
  `RealHardwareDe5000Tests` opt-in test exists but can only be run (and can only prove connectivity/
  framing, not full protocol correctness) once the adapter is available on the bench and its device
  id/UUIDs are known.
- Whether the primary/secondary "measurement record" (value + multiplier/units + display status)
  is worth a small shared sub-parser used twice, or whether that's over-engineering a 5-byte field
  group that's simple enough to just parse twice inline. Implemented as the latter (parsed twice
  inline in `De5000Framer.TryParse`) since both call sites are only a few lines each.
- Whether other ES51919-based meters (rebadged under other brands) are worth explicitly supporting
  via the same decoder, or left as "probably works, not verified" until one shows up.
