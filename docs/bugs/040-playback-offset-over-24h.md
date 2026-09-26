# 040: Playback offsets over 24 hours wrap

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
| **Confidence** | Confirmed |
| **Area** | DevTerm.Logging (PlaybackText) |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Logging/Playback/PlaybackText.cs:40` (`FormatOffset`)

## Failure scenario
`h` is the hours component, so 25 h into a log shows as `1:00:...`.

## Suggested fix
Format with `(int)offset.TotalHours`.
