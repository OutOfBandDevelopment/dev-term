meta:
  id: de5000
  title: DER EE DE-5000 LCR meter measurement packet (Cyrustek ES51919)
  endian: be
doc: |
  One fixed 17-byte packet the meter streams. Field meaning follows
  src/DevTerm.Devices.De5000/De5000Framer.cs and docs/design/proposals/de5000-lcr-meter-protocol.md.
  Sub-byte fields are described in each attribute's doc, since dev-term's importer does not
  read bit fields yet.
seq:
  - id: header
    contents: [0x00, 0x0d]
  - id: flags
    type: u1
    doc: "bit0 Hold, bit1 Reference shown, bit2 Delta, bit3 Calibration, bit4 Sorting, bit5 LCR auto, bit6 Auto range, bit7 Parallel (selects Lp/Cp/Rp over Ls/Cs/Rs)"
  - id: frequency
    type: u1
    doc: "bits 5-7: 0 100 Hz, 1 120 Hz, 2 1 kHz, 3 10 kHz, 4 100 kHz, 5 DC"
  - id: tolerance
    type: u1
    doc: "Sorting-mode tolerance code: 3 +-0.25%, 4 +-0.5%, 5 +-1%, 6 +-2%, 7 +-5%, 8 +-10%, 9 +-20%, 10 -20%/+80%"
  - id: primary_quantity
    type: u1
    doc: "1 L, 2 C, 3 R, 4 DCR (series or parallel per the Parallel flag)"
  - id: primary_raw
    type: u2
    doc: "Unsigned count; value = raw * 10^-(primary_unit & 7)"
  - id: primary_unit
    type: u1
    doc: "bits 3-7 unit (1 Ohm, 2 kOhm, 3 MOhm, 5 uH, 6 mH, 7 H, 8 kH, 9 pF, 10 nF, 11 uF, 12 mF, 13 %, 14 deg); bits 0-2 decimal places"
  - id: primary_status
    type: u1
    doc: "low 4 bits: 0 normal, 1 blank, 2 ----, 3 OL, 7 PASS, 8 FAIL, 9 OPEn, 10 Srt"
  - id: secondary_quantity
    type: u1
    doc: "1 D, 2 Q, 3 ESR, 4 Theta"
  - id: secondary_raw
    type: u2
    doc: "Signed (two's complement) only when the unit is % or deg; value = raw * 10^-(secondary_unit & 7)"
  - id: secondary_unit
    type: u1
    doc: "Same unit and decimal-place coding as primary_unit"
  - id: secondary_status
    type: u1
    doc: "low 3 bits, same table as primary_status"
  - id: footer
    contents: [0x0d, 0x0a]
