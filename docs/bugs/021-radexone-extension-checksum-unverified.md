# 021: Radex One readings aren't checksum-verified, although the comments say they are

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Open |
| **Confidence** | Confirmed |
| **Area** | DevTerm.Devices.RadexOne |
| **Found** | 2026-09-26 code review of `main` @ `42758db` (branch `dev/review-code`); static review, not yet reproduced |

## Where
`src/DevTerm.Devices.RadexOne/RadexOneExtensionCodec.cs:352-388`, `RadexOneDecoder.cs:225`

## What happens
The outer checksum covers only Prefix..Reserved (`_checksumCoveredLength = 10`; the proposal says the same at line
69). Code comments and the proposal's Status claim "the outer framer's checksum already guarantees the packet arrived
intact", which is wrong. `TryParseReadData`/`TryParseReadSettings` never verify the extension's own trailing checksum.

## Failure scenario
A corrupted CPM or ambient value is shown as a valid reading.

## Suggested fix
Verify the inner word-sum for ReadData, ReadSettings and the WriteSettings ack (Serial/Version can stay lenient), and
correct the comments and the proposal's Status.

## Tests to add
A ReadData packet with a corrupt extension checksum is rejected.
