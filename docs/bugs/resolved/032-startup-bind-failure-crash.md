# 032: A bad value on the command line or in the saved default crashes startup instead of opening the editor

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Fixed |
| **Confidence** | CLI/TUI crash confirmed; WPF hidden process plausible |
| **Area** | DevTerm.Console (Program), DevTerm.Wpf (App) |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Console/Program.cs:137`, `src/DevTerm.Wpf/App.xaml.cs:58` (`DevTermConfiguration.Bind`)

## What happens
`--baud fast`, or a truncated or corrupt `appsettings.Local.json`, throws `InvalidOperationException` or
`InvalidDataException` from binding, before validation runs.

## Failure scenario
- The CLI or TUI dies with a stack trace rather than showing the Connection Editor.
- In WPF, `OnStartup` runs with `ShutdownMode.OnExplicitShutdown` already set; if `DispatcherUnhandledException`
  handles the exception, no window opens and the process stays alive with nothing on screen.

A non-atomic `SaveLocalProfile` write ([033](033-non-atomic-writes.md)) is one way to get a truncated file.

## Suggested fix
Catch around `Bind` and route the message into the editor as its `validationError` (CLI: print it and exit 1).

## Tests to add
Startup with `--baud fast` opens the editor with the message; WPF with a corrupt default shows a window.

## Resolution
Fixed in `dev/fix-bugs` on 2026-09-26. Confirmed the CLI crash directly first: a new process-level test
(`ConsoleAppCliTests.InvalidNumericOption_PrintsErrorAndUsage_ExitsOne`, tagged `BugRegression`) spawned the real
built console app with `--baud fast --cli true` and found it exited with code `-532462766` (the CLR's unhandled-
exception crash code, `0xE0434352`), not `1` — `DevTermConfiguration.Bind` throws `InvalidOperationException`
(`ConfigurationBinder`, wrapping a `FormatException` from `Int32Converter`) from `Program.cs`'s unguarded `Bind`
call, before `CliOptionsValidator` ever runs. Also added a narrower unit test at the binder level,
`DevTermConfigurationTests.Bind_ANonNumericValueForANumericField_ThrowsInvalidOperationException` (tagged
`BugRegression`), confirming the exact exception type.

`Program.cs` (`src/DevTerm.Console`) now wraps `DevTermConfiguration.Bind` in a `try`/`catch` for
`InvalidOperationException`, `FormatException`, and `InvalidDataException` (covering a bad command-line/environment
value and a corrupt/truncated JSON profile alike). On failure, `cliOptions` is reset to a clean default (a failed
bind may have left it only partially populated) and the exception's message is carried forward as `bindError`,
folded into the same branch that already handles a `CliOptionsValidator` failure: CLI mode prints the message and
`Usage` and exits 1; TUI mode passes it into `ConfigureMode.Run` as the editor's `validationError`, exactly like an
ordinary validation failure. `useTui` itself is now read directly from the raw `layeredConfig` (via
`GetValue<bool?>` on `Tui`/`Cli`) rather than from `cliOptions`, since a failed `Bind` can't be trusted to have
populated those two fields correctly before the reset.

`App.xaml.cs` (`src/DevTerm.Wpf`)'s `OnStartup` got the identical guard around its own `Bind` call, falling back to
the existing `DeviceProfilesWindow` editor the same way a `CliOptionsValidator` failure already did. The report's
WPF scenario ("Confidence: plausible") was not independently reproduced as a hung/invisible process — there's no
existing test infrastructure that constructs a real WPF `Application` and drives `OnStartup` (per
`docs/design/testing.md`'s WPF automation notes, this class of startup path is undertested by design), and standing
up one for this single case wasn't justified when the fix is the same guard-and-fall-back-to-the-editor pattern
already proven correct on the CLI/TUI side and confirmed to compile and build cleanly.

Confirmed no regressions: full `DevTerm.Console.Tests` (372 tests) and `DevTerm.Configuration.Tests` (331 tests)
suites pass except one pre-existing, unrelated failure
(`TuiToolWindowLayoutTests.Playback_PartWayThroughWithANote`, a `PlaybackMode` layout/rendering test untouched by
this change, confirmed present both with and without this fix via a targeted `git stash`); full solution build is
0 warnings/0 errors.
