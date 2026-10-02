# Velleman K8055 / K8055N: known configuration

| | |
|---|---|
| Kind | USB experiment board: 5 digital in, 8 digital out, 2 analog in, 2 analog out, 2 counters |
| Transport | USB HID, VID `0x10CF` |
| Board address | A 2-position jumper selects the PID: `0x5500` to `0x5503` (K8055) or `0x5504` to `0x5507` (K8055N variants). The launch profile uses `0x5500` (21760); an earlier session used `0x5502` |
| Report size | 9 bytes both ways (report id `0x00` plus 8) |
| Presenter | `k8055` (`src/DevTerm.Devices.K8055`) |
| Frame layouts | [input report](k8055-input-report.ksy), [output report](k8055-output-report.ksy) |
| Byte order | Little-endian (counters are `u2`) |

```bash
dotnet run --project src/DevTerm.Console -- --transport hid --vendorid 4303 --productid 21760 --presenter k8055 --cli true
```

The four boards share one code path, so one board's hardware pass covers all four. Verified on a real board earlier
(see the protocol doc); the `.ksy` files themselves are written from the code and checked only by unit tests.
Quirk: a zero-length HID write is invalid on Windows, so dev-term no-ops on an empty send.

Design notes: [`docs/design/features/velleman-k8055-protocol.md`](../../design/features/velleman-k8055-protocol.md).
