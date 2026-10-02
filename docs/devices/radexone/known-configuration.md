# Radex One: known configuration

| | |
|---|---|
| Kind | Radiation detector, binary request/reply |
| Transport | Serial (a virtual COM port), COM8 on the bench |
| Framing | 9600 8N1, no handshake, DTR and RTS asserted |
| USB-serial bridge id | VID `0xABBA`, PID `0xA011` (Device Manager, 2026-09-25) |
| Presenter | `radexone` (decoder and control surface in `src/DevTerm.Devices.RadexOne`) |
| Frame layouts | [read-data reply](radexone-read-data-reply.ksy), [serial/version reply](radexone-serial-version-reply.ksy), [query request](radexone-query-request.ksy), [write-settings request](radexone-write-settings-request.ksy) |
| Byte order | Little-endian; packets start `7B FF` (to the device) or `7A FF` (from it) |
| Checksums | Word-sum: `0xFFFF - (sum of LE 16-bit words % 0xFFFF)`, one over the envelope's first 10 bytes and one over the extension. Not expressible in `.ksy` |
| Reply lengths | Read Data 34 bytes, Read Serial/Version 42, Read Settings 28 (each is `extension_length` + 12) |

Verified against the real device: 2026-10-02, all three read commands answered first try
([report](../../test/2026-10-02-14-42-26.md), `scripts/radexone_probe.py`); the Read Data exchange is also a regression test in
`DeviceKsyFilesTests`, and `RealHardwareRadexOneTests` runs the imported `.ksy` live through `ManifestFramePresenter` (it publishes
every field). Write Settings and Reset Accumulated have not been run on the device.

Quirks:
- The baud rate is 9600. An earlier 2400 guess received nothing ([bug 061](../../bugs/resolved/061-radexone-wrong-baud-rate.md)).
- It enumerates as a plain COM port, not HID, so no report wrapping is needed.

Design notes and protocol detail: [`docs/design/features/radex-one-protocol.md`](../../design/features/radex-one-protocol.md).
