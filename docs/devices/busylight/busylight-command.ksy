meta:
  id: busylight_command
  title: Kuando Busylight single-step command report
  endian: le
doc: |
  The 9-byte HID report "apply" sends, per src/DevTerm.Devices.Busylight/BusylightControlSurface.cs and
  docs/design/features/kuando-busylight-protocol.md. The 64-byte program/batch form is not described
  here (it had no visible effect on the real device).
seq:
  - id: report_id
    contents: [0x00]
  - id: next_step
    type: u1
    doc: Always sent as 0
  - id: repeat
    type: u1
    doc: Always sent as 0
  - id: red
    type: u1
  - id: green
    type: u1
  - id: blue
    type: u1
  - id: on_time
    type: u1
    doc: "0x01 solid; 0x50/0x50 and 0x10/0x10 are the two blink rates"
  - id: off_time
    type: u1
  - id: audio
    type: u1
    doc: "bit7 play, bits 3-6 track index, bits 0-2 volume. The importer reads this as one byte"
