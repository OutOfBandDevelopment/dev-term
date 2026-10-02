# Zoom H4n (RC04/RC2 remote port): known configuration

| | |
|---|---|
| Kind | Handheld recorder driven through its wired-remote port |
| Transport | Serial through an h4n2rs485 adapter, COM5 in the design notes |
| Framing | 2400 8N1, no handshake |
| Presenter | `zoomh4n` (`src/DevTerm.Devices.ZoomH4n`) |
| Frame layouts | [button press](zoom-h4n-command.ksy) (computer to device), [status byte](zoom-h4n-status.ksy) (device to computer; bit fields, documentation only until the importer reads them) |

One button press is two writes: the 2-byte press code, then the shared release `80 00`. Before the first press the device
needs a wake handshake (`00` repeatedly until a reply byte with the high bit set arrives, then `A1 80 00`); both are in
[zoom-h4n-command.ksy](zoom-h4n-command.ksy)'s `doc`.

Status: unit-tested; see the protocol doc for what has been checked on a real recorder.

Design notes: [`docs/design/features/zoom-h4n-remote-protocol.md`](../../design/features/zoom-h4n-remote-protocol.md).
