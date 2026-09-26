# 004: One saved profile with a bad value crashes TUI startup and blocks the WPF connect

| | |
|---|---|
| **Severity** | High |
| **Status** | Open |
| **Confidence** | Confirmed |
| **Area** | DevTerm.Configuration (ConnectionProfileStore), TUI, WPF |
| **Found** | 2026-09-26 code review of `main` @ `42758db` (branch `dev/review-code`); static review, not yet reproduced |

## Where
- `src/DevTerm.Configuration/ConnectionProfileStore.cs:69-78` (`FindName`)
- Called from `ConnectionDescription.WindowTitle` (`ConnectionDescription.cs:115`) and `StreamMonitor.DeviceNameFor`

## What happens
`FindName`'s catch filter is `IOException or InvalidDataException or JsonException or UnauthorizedAccessException`.
Loading a profile runs `DevTermConfiguration.Bind` and then `ConfigurationBinder.Bind`, which throws
`InvalidOperationException` ("Failed to convert configuration value at 'Baud'") for a value that doesn't convert,
such as `"Baud": "fast"` or `"Parity": "Bogus"`. `JsonConfigurationFileParser` throws `FormatException` for a
duplicate key (`{"Baud":1,"baud":2}`). Neither is caught.

## Failure scenario
Any such file in `~/.dev-term/profiles`, hand-edited or imported (`ReadZip`'s own doc says a bad field value "still
surfaces at Load"):
- **TUI:** `TuiMode.cs:138` (`Title = TitleFor()`) runs in `BuildWindow`, before `app.Run(..., OnUnhandledException)`,
  so the TUI crashes at startup.
- **WPF:** `MainWindow.RefreshConnectionUi` (line 141, `Title = TitleText`) is called at `ConnectAsync` line 110,
  outside its try. The connect never happens; the user gets the generic "unexpected error" dialog.
- Logging start/follow and Stream Monitor naming fail the same way.

The doc comment promises "an unreadable profile is skipped rather than failing the title". The only test,
`FindName_SkipsAnUnreadableProfileAndKeepsLooking`, uses `"{ not json"`, which throws one of the caught types.

## Suggested fix
Add `InvalidOperationException or FormatException` to the filter, or catch `Exception` as `LoadSelected` does.

## Tests to add
`FindName` with a profile containing `"Baud": "fast"`, and one with a duplicate key.
