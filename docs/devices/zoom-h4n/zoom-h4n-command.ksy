meta:
  id: zoom_h4n_command
  title: Zoom H4n RC04/RC2 remote button press
  endian: le
doc: |
  One button press is two writes, per src/DevTerm.Devices.ZoomH4n/ZoomH4nControlSurface.cs: this 2-byte
  press code, then the shared release code 0x80 0x00 (as a separate write, not part of one 4-byte frame).
  Before the first press the device needs a wake handshake: send 0x00 repeatedly (about every 30 ms, up to
  1024 times) until a reply byte with the high bit set arrives, then send 0xA1 0x80 0x00.
  Press codes: record 81 00, play 82 00, stop 84 00, ffwd 88 00, rwd 90 00, volUp 80 08, volDown 80 10,
  recUp 80 20, recDown 80 40, mic 80 01, ch1 80 02, ch2 80 04.
seq:
  - id: transport_bits
    type: u1
    doc: "0x80 plus 0x01 record, 0x02 play, 0x04 stop, 0x08 ffwd, 0x10 rwd; plain 0x80 for the rest"
  - id: button_bits
    type: u1
    doc: "0x08 volUp, 0x10 volDown, 0x20 recUp, 0x40 recDown, 0x01 mic, 0x02 ch1, 0x04 ch2; 0 for the transport buttons"
