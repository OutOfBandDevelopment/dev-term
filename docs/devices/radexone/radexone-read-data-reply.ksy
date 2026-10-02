meta:
  id: radexone_read_data_reply
  title: Radex One Read Data (0x0800) reply
  endian: le
doc: |
  The inbound packet for the Read Data command, per src/DevTerm.Devices.RadexOne/RadexOneFramer.cs and
  RadexOneExtensionCodec.cs. Both checksums are a word-sum (little-endian 16-bit words, summed, then
  0xFFFF - (sum % 0xFFFF)): the envelope's covers the first 10 bytes, the extension's covers the
  extension's first 20 bytes. Kaitai cannot express that, so neither is validated here.
seq:
  - id: prefix
    contents: [0x7a, 0xff]
  - id: type_marker
    type: u2
    doc: "Constant 0x8020 on every inbound packet"
  - id: extension_length
    type: u2
    doc: "22 for this reply"
  - id: packet_number
    type: u2
  - id: reserved
    size: 2
  - id: envelope_checksum
    type: u2
  - id: extension
    type: read_data_extension
types:
  read_data_extension:
    seq:
      - id: command_code
        type: u2
        doc: "0x0800 Read Data (a reply echoes its request's code)"
      - id: reserved_a
        size: 2
      - id: constant_0c
        type: u2
        doc: "0x000C"
      - id: reserved_b
        size: 2
      - id: ambient
        type: u2
        doc: Ambient dose rate; units not specified by the source doc
      - id: reserved_c
        size: 2
      - id: accumulated
        type: u2
        doc: Accumulated dose
      - id: reserved_d
        size: 2
      - id: cpm
        type: u2
        doc: Counts per minute
      - id: reserved_e
        size: 2
      - id: checksum
        type: u2
