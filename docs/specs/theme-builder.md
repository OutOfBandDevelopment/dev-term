# Theme Builder

## Purpose

Lets a user create or edit a `ThemeFile` from inside the app — pick a seed (a built-in theme or an
existing user theme), name it, override any `ThemeRole`'s color, see the whole app re-skin live
while editing, and save — instead of hand-writing JSON. Reached from **View > Theme > Build/Edit
Theme...** in either front end, alongside the existing theme picker. Shared logic (seed tracking,
per-role overrides, live-preview building, contrast checking, save/validation) lives in
`DevTerm.Configuration.ThemeBuilderState`, used by both front ends:

- **TUI**: `DevTerm.Console.ThemeBuilderMode` — a seed-picking `Dialog` (`PickSeed`) followed by a
  builder `Window` (`BuildWindow`/`ThemeBuilderWindowParts`), run via a nested `Application.Run`.
- **WPF**: `DevTerm.Wpf.ThemeSeedPickerWindow` (seed + name) followed by `DevTerm.Wpf.ThemeBuilderWindow`
  (the builder), run via `ThemeBuilderWindow.Run(owner)` and two chained `ShowDialog()` calls.

See [`docs/design/features/theme-builder.md`](../design/features/theme-builder.md) for the design
rationale (including a sequence diagram and a wireframe) and
[`docs/design/theming.md`](../design/theming.md) for the underlying `ThemeFile`/`ThemeCatalog`/
`DevTermTheme` model this screen edits.

No new persistence format: saving writes a `ThemeFile`-shaped JSON into
`DevTermUserDataPaths.ThemesDirectory` (`~/.dev-term/themes/`), the same place `ThemeCatalog`
already enumerates from — so a theme built here immediately appears in View > Theme's picker too.

## Fields

| Field | Type | Default | Validation | Notes |
|---|---|---|---|---|
| Seed | one of `ThemeCatalog.SelectionNames` (Light, Dark, System, then every user theme file), picked from a list | the first entry (Light) | Required — Create/Commit does nothing with no selection | Chosen once, on the first ("seed picker") screen, before the builder opens. `System` resolves to whichever of Light/Dark the OS currently prefers (`ActiveTheme.PrefersDark`) at pick time — it is not re-resolved later if the OS theme changes mid-edit |
| New theme's name | free text | `"{seed label} copy"` (e.g. `"Light copy"`, `"My Theme copy"`) | Trimmed; re-validated again at Save time (see Actions) | Shown on the seed picker; carried into the builder's Name field, where it can still be changed before Save |
| Name (builder) | free text | the name from the seed picker | Must be non-empty and pass `ProfileName.IsValid`; must not be a reserved name (`BuiltInThemes.IsReservedName` — `"light"`/`"dark"`/`"system"`, case-insensitive) | Edits as you type (WPF: `TextChanged`) re-preview live but are **not** re-validated until Save — an invalid in-progress value just previews under its old/empty name with no inline error |
| Chart palette | one of Light/Dark | the seed's own `ChartPalette` | n/a (fixed set) | Independent of any individual role color — controls which of the two built-in chart color sets (`ChartPaletteVariant`) a strip chart/bar graph draws with under this theme |
| Colors (one row per `ThemeRole`) | each role: a `#RRGGBB` value, editable via a per-role color editor (see Actions) | the seed's value for that role | A role's value must parse as a valid `#RRGGBB` hex color (`ThemeColor.TryParse`) — enforced by the editor itself, not by a separate inline message | Every `ThemeRole` enum member gets exactly one row, in declaration order. A row overridden from the seed is marked (● in both front ends); an unmarked row still shows the seed's own value verbatim, including when the seed is itself a user theme that already overrode it |
| Contrast warnings | read-only text | `"No contrast problems."` | n/a — display only, never blocks Save | Re-run (`DevTermTheme.ContrastWarnings()`, a WCAG check) after every edit; shows at most the first 3 warnings. A failing contrast pair is reported here but still savable — matches `ThemeFile`'s own load-time behavior ("failures are reported but not rejected") |

## Actions

| Action | Behavior | Preconditions | On failure |
|---|---|---|---|
| **Create** (seed picker: button, or double-clicking a seed row) | Resolves the picked seed to a real `DevTermTheme` (`ActiveTheme.Catalog.Resolve`), builds a fresh `ThemeBuilderState(seed, name)`, calls `ActiveTheme.Preview(state.Build())` so the whole app re-skins immediately, then opens the builder window on that state | A seed must be selected (always true — index 0 is pre-selected) | n/a — this step has no failure path of its own |
| **Edit a role's color** ("Edit..." button, or double-clicking a role row) | TUI: opens a small dialog with a hex `TextField`, validated via `ThemeColor.TryParse` before accepting. WPF: opens `ColorPickerWindow` (RGB/HSV/hex) seeded with the role's current color. Either way, accepting sets the override (`state.Set(role, color)`), refreshes the role list and contrast warnings, and re-previews | A role must be selected in the list | TUI: an unparseable hex value is rejected inline, dialog stays open; WPF: `ColorPickerWindow` only returns a value it already validated, so there is no separate failure path here |
| **Reset to Seed** | Clears the selected role's override (`state.ResetToSeed(role)`) so the row reverts to showing the seed's own value for that role, unmarked; refreshes the list and warnings; re-previews | A role must be selected | n/a — resetting an already-unoverridden role is a harmless no-op |
| **Change Name** | Sets `state.Name`; re-previews (the preview itself doesn't change, since Name isn't a color, but every edit re-runs the same preview call for consistency) | None | n/a |
| **Change Chart palette** | Sets `state.ChartPalette`; re-previews — a strip chart/bar graph in the live preview immediately redraws with the other palette's colors | None | n/a |
| **Save** | Validates Name (non-empty, `ProfileName.IsValid`, not reserved); if a file already exists at `{ThemesDirectory}/{name}.json`, asks to confirm overwrite first; writes the file (`state.Save(path)`, which diffs the fully-built theme against the closer of `BuiltInThemes.Light`/`Dark`, not against the seed — see the design doc's resolved "Open questions"); on success, reloads the catalog (`ActiveTheme.UseCatalog`) and selects the new theme by name (`ActiveTheme.Select`), persisting it as the real app preference; closes the builder | None | Empty/invalid name or a reserved name: inline error, dialog stays open, nothing written. Overwrite declined: nothing written, dialog stays open. A write-level error ( `state.Save` returning a message, e.g. an I/O failure): inline error, dialog stays open |
| **Cancel** / close the builder without saving | Closes the builder; the outer `Run`/`ThemeBuilderMode.Run` caller then calls `ActiveTheme.CancelPreview()` in a `finally`, reverting `Current` back to whatever `Selection` already resolves to | None | n/a |

## States

- **Seed is fixed once chosen.** There is no way to change the seed after the builder window opens
  short of closing it and starting over — the seed only matters at `ThemeBuilderState` construction
  time (it seeds every role's starting value and chart palette) and isn't tracked afterward.
- **Built-in themes are never edited in place.** Picking Light or Dark as a seed never writes back
  to those built-ins; Save always creates a new user theme file. See the design doc's resolved open
  question for exactly how `Save` decides which roles need writing (diffed against the closer
  built-in base, not against the seed).
- **Live preview is scoped, not a real selection change.** `ActiveTheme.Preview(theme)` sets
  `Current` and raises `Changed` (so every open window re-skins) without touching `Selection` or
  persisting anything to `~/.dev-term/preferences.json`. Closing the editor without saving — or
  closing the whole app mid-edit — leaves the real selection untouched; only a successful Save calls
  `ActiveTheme.Select`, which is the only path that persists a choice.
- **The live preview affects every open window of that front end, not just the builder.** Since
  `Preview` goes through the same `ActiveTheme.Changed` mechanism a real theme switch uses, the main
  window, any other open dialog, and the builder itself all re-skin together on every edit — this is
  the point (the design doc's rationale: "provably what the real theme will look like, not an
  approximation"), but it means a color edited mid-session is visible everywhere immediately, not
  just in a contained preview pane.
- **Contrast warnings never block Save.** A theme with failing contrast can still be saved — matches
  `ThemeFile`'s existing load-time tolerance for user themes.
- **No undo across edits.** Reset to Seed only affects the single selected role; there is no
  "revert all" or multi-level undo for a sequence of edits within one builder session short of
  closing without saving and starting over.

## Per-front-end notes

- **Color editing widget**: WPF reuses `ColorPickerWindow` (RGB/HSV/hex — the same control the
  Kuando Busylight panel uses) for each role; the TUI has no native color-picker widget
  (Terminal.Gui v2.5.0), so `ThemeBuilderMode.EditColor` is a small dialog with a validated hex
  `TextField` instead — the design doc's front-end split anticipated exactly this gap.
- **Running the flow**: the TUI's `ThemeBuilderMode.Run(app, themesDirectory)` drives the seed
  picker then a nested `app.Run` on the builder window itself, with `ActiveTheme.CancelPreview()` in
  a `finally` around the whole thing. WPF's `ThemeBuilderWindow.Run(owner, themesDirectory)` chains
  two `ShowDialog()` calls (`ThemeSeedPickerWindow` then `ThemeBuilderWindow`), with the same
  `CancelPreview()` in a `finally`.
- **Testability of the validation/overwrite branches differs by design, not by accident.** WPF's
  `ThemeBuilderWindow.Save`/`ThemeSeedPickerWindow.Commit` are `internal` and call through injectable
  delegate properties (`ReportValidationError`, `ConfirmOverwrite`) that default to real
  `MessageBox.Show` calls — a test replaces them to exercise every branch (empty name, reserved
  name, overwrite decline/confirm) without ever hitting a real blocking dialog. The TUI's equivalent
  branches use real `MessageBox.Query`/`Dialog` calls and need a nested `Application.Run` in the
  test to drive them — see `ThemeBuilderModeTests.cs` vs. `ThemeBuilderWindowTests.cs`/
  `ThemeSeedPickerWindowTests.cs` for the resulting difference in test shape.
- **`DialogResult = true` is defensive, not load-bearing, in WPF.** Both `ThemeBuilderWindow.Save`
  and `ThemeSeedPickerWindow.Commit` wrap the assignment in a try/catch ignoring
  `InvalidOperationException`, since tests call `Save()`/`Commit()` directly without ever calling
  `ShowDialog()` first (same pattern as `DeviceProfilesWindow`'s `CloseRequested` handler) — WPF
  throws that exception when `DialogResult` is set on a window that was never shown as a dialog.
- **Role list row text is a fixed-width string, not separate columns, in both front ends** — a
  marker (`●`/blank), the role name padded to a column width, then the hex value — rather than a
  real multi-column list control, since Terminal.Gui's `ListView` and a WPF `ListBox` bound the same
  simple way both render a list of strings most directly.

## Open items

- No real-hardware/manual-use verification pass yet — this is a pure UI feature with no device
  dependency, so the usual bench-test verification this project does for device modules doesn't
  apply the same way; see the design doc's Status section.
- No way to delete a saved user theme from this screen — deleting a theme file still means removing
  it from `~/.dev-term/themes/` by hand (outside the app). Not raised as a design gap so far, just
  not built.
- The TUI's role list has no scrollbar indicator distinct from any other `ListView` in this codebase
  — with enough `ThemeRole` members to scroll, there's no visual hint beyond the usual Terminal.Gui
  list behavior that more rows exist above/below.
