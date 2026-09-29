# 004: One saved profile with a bad value crashes TUI startup and blocks the WPF connect

| | |
|---|---|
| **Severity** | High |
| **Status** | Fixed |
| **Confidence** | Confirmed |
| **Area** | DevTerm.Configuration (ConnectionProfileStore), TUI, WPF |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

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

## Resolution
Fixed on 2026-09-26 (branch `dev/fix-bugs`): `FindName`'s catch filter now also lists
`InvalidOperationException`, which is what `ConfigurationBinder.Bind` actually throws for a value
that fails to convert (confirmed with `"Baud": "fast"`). The duplicate-key case in the report doesn't
reproduce as described: a duplicate key (verified with `{ "Port": 1, "port": 2 }`) makes
`JsonConfigurationFileParser` throw `InvalidDataException`, not `FormatException`, on this .NET
version — and `InvalidDataException` was already in the filter, so that path was never actually
broken. Regression test:
`DevTerm.Configuration.Tests.ConnectionProfileStoreTests.FindName_SkipsAProfileWithAValueThatFailsToConvert_AndKeepsLooking`.
