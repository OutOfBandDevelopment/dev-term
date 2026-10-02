meta:
  id: radexone_serial_version_reply
  title: Radex One Read Serial/Version (0x0001) reply
  endian: le
doc: |
  The inbound packet for Read Serial/Version. The payload layout is inferred, not documented: a real unit's reply
  (2026-10-02, COM8, docs/test/2026-10-02-14-42-26.md) decodes to exactly the source doc's example string
  "SN: 180620-0840-008344 v1.8" with the fields below (day 20, month 06, year 18 -> "180620"; batch 0x0348 = 840 -> "0840";
  serial 0x2098 = 8344 -> "008344"; version 1.8). The three `unknown_*` fields are not understood. This reply is 42 bytes; a
  unit with a longer payload would need the variable-length support the importer doesn't have yet. The extension checksum is
  not the word-sum over the first 28 bytes the other extensions use (it did not reconcile against the source trace), so
  nothing here validates it.
seq:
  - id: prefix
    contents: [0x7a, 0xff]
  - id: type_marker
    type: u2
    doc: "Constant 0x8020"
  - id: extension_length
    type: u2
    doc: "30 for this reply"
  - id: packet_number
    type: u2
  - id: reserved
    size: 2
  - id: envelope_checksum
    type: u2
  - id: extension
    type: serial_version_extension
types:
  serial_version_extension:
    seq:
      - id: command_code
        type: u2
        doc: "0x0001 (a reply echoes its request's code)"
      - id: reserved_a
        size: 2
      - id: unknown_a
        type: u4
        doc: "0x14 on the real unit; meaning unknown"
      - id: unknown_b
        type: u4
        doc: "0xA411 on the real unit; meaning unknown"
      - id: serial_number
        type: u4
        doc: "Last block of the printed serial (008344)"
      - id: day
        type: u2
      - id: month
        type: u1
      - id: year
        type: u1
        doc: "Two-digit year (18 = 2018)"
      - id: version_major
        type: u1
      - id: version_minor
        type: u1
      - id: batch
        type: u2
        doc: "Middle block of the printed serial (0840)"
      - id: unknown_c
        type: u4
      - id: checksum
        type: u2
