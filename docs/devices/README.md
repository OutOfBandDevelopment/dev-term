# Devices

One folder per instrument: the vendor's programming manual, and a `known-configuration.md` recording how dev-term
reaches it on the bench (transport, address, framing, quirks). Facts there come from `docs/test/` bench reports,
`docs/changes/`, `docs/bugs/` and the live bridge settings; a blank marked "to fill in" was not recorded anywhere.

| Folder | Transport | Address |
| --- | --- | --- |
| [hp-34401a](hp-34401a/known-configuration.md) | Serial / TCP bridge | COM5, 9600 8N2; or 192.168.0.109 (needs bridge CTS wired to meter DSR) |
| [korad-ka3005p-ka6003p](korad-ka3005p-ka6003p/known-configuration.md) | Serial | COM6 / COM7, 9600 8N1 |
| [tektronix-2230](tektronix-2230/known-configuration.md) | TCP bridge | 192.168.0.108 and .107 |
| [tektronix-tds2024](tektronix-tds2024/known-configuration.md) | TCP bridge | 192.168.0.110 |
| [rigol-dm3058e](rigol-dm3058e/known-configuration.md), [rigol-ds1102e](rigol-ds1102e/known-configuration.md), [rigol-dg1022](rigol-dg1022/known-configuration.md), [rigol-dg1062z](rigol-dg1062z/known-configuration.md) | USBTMC | by VID:PID |
| [usr-tcp232-302](usr-tcp232-302/known-configurations.md) | the serial-to-Ethernet bridges themselves | web UI, [read script](usr-tcp232-302/Get-UsrBridgeSettings.ps1) |

## Binary frame layouts (Kaitai Struct)

The devices below speak binary, not SCPI text, so they have no `known-configuration.md`. Each folder holds a `.ksy`
describing the frame, written from the decoders in `src/` (not from a bench capture), and each can be loaded with the
manifest editor's import button. `DeviceKsyFilesTests` imports and decodes a known frame from the first three.

| File | Frame |
| --- | --- |
| [de5000/de5000.ksy](de5000/de5000.ksy) | DE-5000 LCR meter, 17-byte measurement packet. Sub-byte fields (flags, unit nibble) are documented, not split |
| [radexone/radexone-read-data-reply.ksy](radexone/radexone-read-data-reply.ksy) | Radex One Read Data reply; the word-sum checksums are not validated |
| [k8055/k8055-input-report.ksy](k8055/k8055-input-report.ksy) | Velleman K8055 9-byte HID input report |
| [zoom-h4n/zoom-h4n-status.ksy](zoom-h4n/zoom-h4n-status.ksy) | Zoom H4n status byte, as bit fields; the importer can't read these yet, so it only documents the layout |

The Busylight's reply is an ASCII identification string, so it has no `.ksy`.
