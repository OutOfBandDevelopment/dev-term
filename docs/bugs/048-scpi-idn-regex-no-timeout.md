# 048: SCPI *IDN? matching runs profile regexes with no timeout

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
| **Confidence** | Confirmed (local files only) |
| **Area** | DevTerm.Devices.Scpi (ScpiProfileCatalog) |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Devices.Scpi/ScpiProfileCatalog.cs:61`

## What happens
`Regex.IsMatch` runs a profile-supplied pattern with no timeout; the manifest reply presenter and editor use 250 ms. A
catastrophic-backtracking pattern in a user profile would hang auto-detect.

## Suggested fix
Construct the regexes with a timeout (250 ms, as elsewhere).
