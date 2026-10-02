meta:
  id: radexone_query_request
  title: Radex One query request (Read Data, Read Serial/Version, Read Settings, Reset Accumulated)
  endian: le
doc: |
  The outbound packet for the four commands that share a 6-byte extension, per
  src/DevTerm.Devices.RadexOne/RadexOneFramer.cs (BuildRequest) and RadexOneExtensionCodec.BuildQuery.
  Both checksums are a word-sum (little-endian 16-bit words, summed, then 0xFFFF - (sum % 0xFFFF)); the
  envelope's covers the first 10 bytes, the extension's its first 4. Kaitai cannot express that, so a
  consumer has to compute them.
seq:
  - id: prefix
    contents: [0x7b, 0xff]
  - id: type_marker
    type: u2
    doc: "Constant 0x0020 on every outbound packet"
  - id: extension_length
    type: u2
    doc: "6 for these commands"
  - id: packet_number
    type: u2
  - id: reserved
    size: 2
  - id: envelope_checksum
    type: u2
  - id: extension
    type: query_extension
types:
  query_extension:
    seq:
      - id: command_code
        type: u2
        doc: "0x0800 Read Data, 0x0001 Read Serial/Version, 0x0801 Read Settings, 0x0803 Reset Accumulated"
      - id: word
        type: u2
        doc: "0x000C for the three reads, 0x0001 for Reset Accumulated"
      - id: checksum
        type: u2
