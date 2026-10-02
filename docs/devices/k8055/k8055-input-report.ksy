meta:
  id: k8055_input_report
  title: Velleman K8055 unprompted 9-byte input report
  endian: le
doc: |
  Streamed continuously by the board over HID, per src/DevTerm.Devices.K8055/K8055Decoder.cs and
  docs/design/features/velleman-k8055-protocol.md. The per-bit mapping of digital_in to the five
  physical input pins is not yet confirmed on hardware.
seq:
  - id: report_id
    contents: [0x00]
    doc: The HID report id, never device data
  - id: digital_in
    type: u1
    doc: Digital input bitmask (five inputs)
  - id: status
    type: u1
    doc: "Observed constant 0x03; meaning undocumented"
  - id: analog_in_1
    type: u1
  - id: analog_in_2
    type: u1
  - id: counter_1
    type: u2
  - id: counter_2
    type: u2
