# Kuando Busylight: known configuration

| | |
|---|---|
| Kind | USB presence light: RGB, blink, audio |
| Transport | USB HID, VID `0x04D8`, PID `0xF848` (1240 / 63560) |
| Report size | 9 bytes for the single-step command |
| Presenter | `busylight` (`src/DevTerm.Devices.Busylight`) |
| Frame layout | [busylight-command.ksy](busylight-command.ksy) (outbound only; the device's reply is an ASCII identification string, so it has no `.ksy`) |

```bash
dotnet run --project src/DevTerm.Console -- --transport hid --vendorid 1240 --productid 63560 --presenter busylight --cli true
```

Verified on the real device in earlier sessions, and again 2026-10-02 (colour sequence observed on the lamp: red, green, blue, yellow, off; see `docs/test/2026-10-02-15-54-12.md`). The 64-byte program/batch form had no visible effect and isn't described.
The `audio` byte is bit-packed (bit 7 play, bits 3-6 track, bits 0-2 volume), which the importer can't split yet.

Design notes: [`docs/design/features/kuando-busylight-protocol.md`](../../design/features/kuando-busylight-protocol.md).
