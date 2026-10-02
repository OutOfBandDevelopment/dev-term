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

The devices below speak binary, not SCPI text, so they have no `known-configuration.md`. Each folder holds `.ksy`
files describing the frames in each direction, written from the decoders and control surfaces in `src/` (not from a
bench capture); load one with the manifest editor's import button. `DeviceKsyFilesTests` imports and decodes a known
frame from each file except the Zoom H4n status byte.

| Device | Device to computer | Computer to device |
| --- | --- | --- |
| DE-5000 LCR meter | [de5000.ksy](de5000/de5000.ksy): 17-byte measurement packet (flag and unit bits documented, not split) | none: it streams and takes no commands |
| Radex One | [read-data reply](radexone/radexone-read-data-reply.ksy) | [query](radexone/radexone-query-request.ksy) (Read Data, Read Serial/Version, Read Settings, Reset Accumulated), [write settings](radexone/radexone-write-settings-request.ksy); the word-sum checksums are not validated |
| Velleman K8055 | [input report](k8055/k8055-input-report.ksy) | [output report](k8055/k8055-output-report.ksy) (set outputs, reset counters) |
| Zoom H4n | [status byte](zoom-h4n/zoom-h4n-status.ksy), as bit fields the importer can't read yet, so documentation only | [button press](zoom-h4n/zoom-h4n-command.ksy), with the release and wake sequences in its doc |
| Kuando Busylight | an ASCII identification string, so no `.ksy` | [command](busylight/busylight-command.ksy): the 9-byte single-step report |
