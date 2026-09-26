# 032: A bad value on the command line or in the saved default crashes startup instead of opening the editor

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Open |
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
