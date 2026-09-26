# 013: Save Profile and Export crash on a bad name or path

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Open |
| **Confidence** | Confirmed |
| **Area** | DevTerm.Configuration (ConnectionEditorViewModel), TUI, WPF |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Configuration/ConnectionEditorViewModel.cs:1274` (`_store.Save(name, options)`) and `:1399`
(`ConnectionProfileStore.ExportToFile`). Load, Import, ImportZip, ExportZip and Replace All all catch and set
`StatusMessage`; these two don't.

## Failure scenario
- Profile name `bench/meter` throws `DirectoryNotFoundException`; `bench?` throws `IOException`.
- An export path in a folder that doesn't exist throws.
- In the startup TUI editor, `ConfigureMode.Run` calls `app.Run(parts.Window)` with no error handler, so the process
  crashes. In WPF the user gets a stack-trace dialog instead of a status line.

## Suggested fix
Wrap both calls like `ExportProfilesZip`: `catch (Exception ex) { StatusMessage = ...; }`.

## Tests to add
Save with an invalid name and Export to a missing folder set `StatusMessage` and don't throw.
