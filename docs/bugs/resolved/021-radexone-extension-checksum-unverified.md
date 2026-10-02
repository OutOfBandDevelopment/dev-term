# 021: Radex One readings aren't checksum-verified, although the comments say they are

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Fixed |
| **Confidence** | Confirmed |
| **Area** | DevTerm.Devices.RadexOne |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

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

## Resolution
Fixed in `dev/fix-bugs` on 2026-09-26: `RadexOneExtensionCodec` now verifies each extension's own
trailing checksum via a shared `HasValidChecksum` helper — `TryParseReadData`/`TryParseReadSettings`
reject a corrupt extension outright, and a new `TryVerifyWriteSettingsAck` does the same for the
Write Settings ack (previously not parsed at all; `RadexOneDecoder` had a hardcoded acknowledgement
string regardless of content). Read Serial/Version deliberately stays lenient per this report's own
carve-out, since its reserved-byte layout still doesn't fully reconcile against the source doc.
Corrected the misleading "the outer framer's checksum already guarantees the packet arrived intact"
comment in both `RadexOneExtensionCodec.cs` and `docs/design/proposals/radex-one-protocol.md`, and
updated the proposal's Status/Open questions sections. Regression tests:
`RadexOneDecoderTests.Render_WithReadDataReply_AndCorruptedExtensionChecksum_IsNotShownAsAValidReading`,
`RadexOneDecoderTests.Render_WithSettingsReply_AndCorruptedExtensionChecksum_IsNotShownAsAValidReading`,
`RadexOneDecoderTests.Render_WithWriteSettingsAckReply_AndCorruptedExtensionChecksum_IsNotShownAsAcknowledged`.
