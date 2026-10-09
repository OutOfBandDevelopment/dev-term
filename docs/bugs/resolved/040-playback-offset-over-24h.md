# 040: Playback offsets over 24 hours wrap

| | |
|---|---|
| **Severity** | Low |
| **Status** | Fixed |
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

## Resolution
Fixed on 2026-09-26 on `dev/fix-bugs`: `FormatOffset` (`src/DevTerm.Logging/Playback/PlaybackText.cs`) now builds
the hour part from `(int)offset.TotalHours` explicitly instead of relying on `TimeSpan`'s `h` custom-format
specifier (which is the hour-of-day component, 0-23, not total hours), while `mm:ss.fff` still comes from
`TimeSpan.ToString` for the minute/second/millisecond components. Regression test:
`PlaybackTextTests.FormatOffset_PastTwentyFourHours_DoesNotWrap`.

Resolution recorded in commit `babdf36` (backfilled 2026-10-09 from git history).
