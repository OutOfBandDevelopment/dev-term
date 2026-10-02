meta:
  id: k8055_output_report
  title: Velleman K8055 Set Analog/Digital Outputs (0x05) report
  endian: le
doc: |
  The 9-byte HID report dev-term sends to drive the outputs, per
  src/DevTerm.Devices.K8055/K8055ControlSurface.cs and docs/design/features/velleman-k8055-protocol.md.
  The reset-counter commands (0x03 counter 1, 0x04 counter 2) use the same 9 bytes with command set and
  everything after it zero. The duration/debounce bytes are always sent as zero (unconfirmed).
seq:
  - id: report_id
    contents: [0x00]
  - id: command
    type: u1
    doc: "0x05 set outputs, 0x03 reset counter 1, 0x04 reset counter 2"
  - id: digital_out
    type: u1
    doc: Digital output bitmask
  - id: analog_out_1
    type: u1
  - id: analog_out_2
    type: u1
  - id: reserved
    size: 4
