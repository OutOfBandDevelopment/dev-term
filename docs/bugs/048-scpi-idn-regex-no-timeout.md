# 048: SCPI *IDN? matching runs profile regexes with no timeout

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
| **Confidence** | Confirmed (local files only) |
| **Area** | DevTerm.Devices.Scpi (ScpiProfileCatalog) |
| **Found** | 2026-09-26 code review of `main` @ `42758db` (branch `dev/review-code`); static review, not yet reproduced |

## Where
`src/DevTerm.Devices.Scpi/ScpiProfileCatalog.cs:61`

## What happens
`Regex.IsMatch` runs a profile-supplied pattern with no timeout; the manifest reply presenter and editor use 250 ms. A
catastrophic-backtracking pattern in a user profile would hang auto-detect.

## Suggested fix
Construct the regexes with a timeout (250 ms, as elsewhere).
