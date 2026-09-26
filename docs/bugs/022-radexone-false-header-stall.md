# 022: One false Radex One header can stall decoding for minutes

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Open |
| **Confidence** | Confirmed |
| **Area** | DevTerm.Devices.RadexOne |
| **Found** | 2026-09-26 code review of `main` @ `42758db` (branch `dev/review-code`); static review, not yet reproduced |

## Where
`src/DevTerm.Devices.RadexOne/RadexOneDecoder.cs:214-221`

## What happens
The decoder waits for `HeaderLength + extensionLength` bytes (up to 65,547) before checking the checksum, even though
that checksum covers only the 12-byte header.

## Failure scenario
Line noise produces `7A FF` with a garbage length. All decoding waits for the full bogus length: at 2400 baud, about
270 s.

## Suggested fix
Check the type marker and header checksum as soon as 12 bytes are buffered and resync on failure; optionally cap
`extensionLength`.

## Tests to add
A false header with a huge length followed by a valid packet decodes the valid packet promptly.
