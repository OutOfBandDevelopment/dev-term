# DER EE DE-5000 LCR meter: known configuration

| | |
|---|---|
| Kind | Handheld LCR meter that streams a measurement packet; takes no commands |
| Transport | Optical (IR) UART, read through an IR-to-USB serial adapter, or over BLE through a bridge |
| Framing | 9600 8N1 |
| Presenter | `de5000` (`src/DevTerm.Devices.De5000`) |
| Frame layout | [de5000.ksy](de5000.ksy): 17 bytes, header `00 0D`, ends `0D 0A`; flag and unit bits are documented, not split |

Not yet checked against a real meter in this repo (the layout comes from the community decodings listed in the proposal);
no COM port or bridge address is recorded here for that reason.

Design notes: [`docs/design/proposals/de5000-lcr-meter-protocol.md`](../../design/proposals/de5000-lcr-meter-protocol.md).
