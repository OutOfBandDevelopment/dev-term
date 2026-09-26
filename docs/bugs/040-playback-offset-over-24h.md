# 040: Playback offsets over 24 hours wrap

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
| **Confidence** | Confirmed |
| **Area** | DevTerm.Logging (PlaybackText) |
| **Found** | 2026-09-26 code review of `main` @ `42758db` (branch `dev/review-code`); static review, not yet reproduced |

## Where
`src/DevTerm.Logging/Playback/PlaybackText.cs:40` (`FormatOffset`)

## Failure scenario
`h` is the hours component, so 25 h into a log shows as `1:00:...`.

## Suggested fix
Format with `(int)offset.TotalHours`.
