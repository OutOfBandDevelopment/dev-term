meta:
  id: zoom_h4n_status
  title: Zoom H4n RC04/RC2 remote protocol status byte
  endian: le
doc: |
  Each unsolicited byte is a complete, independent status snapshot, per
  src/DevTerm.Devices.ZoomH4n/ZoomH4nDecoder.cs and docs/design/features/zoom-h4n-remote-protocol.md.
  Bits are listed most significant first. dev-term imports this as a one-byte frame of eight bit
  fields (checked against the decoder's documented masks, not yet against a live recorder).
seq:
  - id: handshake_wake
    type: b1
    doc: "0x80, the handshake's own wake signal, not a status bit"
  - id: led2
    type: b1
    doc: "0x40"
  - id: led1
    type: b1
    doc: "0x20"
  - id: mic_led
    type: b1
    doc: "0x10"
  - id: unknown_08
    type: b1
    doc: "0x08, undocumented"
  - id: unknown_04
    type: b1
    doc: "0x04, undocumented"
  - id: peak
    type: b1
    doc: "0x02"
  - id: record_led
    type: b1
    doc: "0x01"
