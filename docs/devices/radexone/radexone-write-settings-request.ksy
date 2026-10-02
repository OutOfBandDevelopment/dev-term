meta:
  id: radexone_write_settings_request
  title: Radex One Write Settings (0x0802) request
  endian: le
doc: |
  The outbound packet that sets the alarm mode and threshold, per
  src/DevTerm.Devices.RadexOne/RadexOneExtensionCodec.cs (BuildWriteSettings). Checksums are the same
  word-sum as in radexone-query-request.ksy; the extension's covers its first 14 bytes. The Read Settings
  reply has the same shape minus the 0x000E word (a zero there) and under an 0x8020 envelope.
seq:
  - id: prefix
    contents: [0x7b, 0xff]
  - id: type_marker
    type: u2
    doc: "Constant 0x0020"
  - id: extension_length
    type: u2
    doc: "16"
  - id: packet_number
    type: u2
  - id: reserved
    size: 2
  - id: envelope_checksum
    type: u2
  - id: extension
    type: write_settings_extension
types:
  write_settings_extension:
    seq:
      - id: command_code
        type: u2
        doc: "0x0802"
      - id: constant_0e
        type: u2
        doc: "0x000E"
      - id: target_value
        type: u2
        doc: "0x0005"
      - id: reserved_a
        size: 2
      - id: alarm_mode
        type: u1
      - id: threshold
        type: u2
      - id: reserved_b
        size: 3
      - id: checksum
        type: u2
