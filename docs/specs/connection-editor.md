# Connection Editor

## Purpose

Lets a user pick, create, edit, save, delete, and import/export a connection profile, and connect
with the current fields. Shared logic (validation, `ConnectionProfileStore` I/O,
`RelayCommand`-based commands) lives in `DevTerm.Configuration.ConnectionEditorViewModel`, used by
both front ends:

- **TUI**: `DevTerm.Console.ConfigureMode` — a Terminal.Gui `Window`.
- **WPF**: `DevTerm.Wpf.DeviceProfilesWindow` — bound directly via XAML `{Binding ...}`/
  `Command="{Binding ...}"`, no business logic in code-behind.

Shown in two situations:

1. **At startup**, when the bound `CliOptions` doesn't validate (see
   [connection-profiles.md](../design/connection-profiles.md)'s startup flow) — instead of
   hard-failing. `Result`, once set, is used directly to build the DI host and connect immediately.
2. **From "File > Device Profiles..."** in either front end, at any time, already connected —
   `Result`, once set, is saved as the untracked default profile *and* live-switched to
   immediately (`DevTermSessionBuilder`/`MainWindow.SwitchProfileAsync`/`TuiMode.BuildWindow`'s
   `SwitchProfileAsync`) — the running session tears down and the new one opens in place, no
   restart needed.

## Fields

| Field | Type | Default | Validation | Notes |
|---|---|---|---|---|
| Transport | one of `serial`/`tcp`/`hid` | `serial` | Must be one of the three | Selecting a value shows only that transport's field group (see States) |
| Description | free text | empty | none | Purely descriptive; never read by any transport |
| Port (serial) | free text | empty | Required when Transport is `serial` | e.g. `COM3`, `/dev/ttyUSB0` |
| Baud (serial) | integer, typed as text | `9600` | Parsed with `int.TryParse`; unparseable input is silently ignored (keeps the previous value) | |
| Data bits (serial) | integer, typed as text | `8` | Same parse behavior as Baud | |
| Parity (serial) | one of `None`/`Odd`/`Even`/`Mark`/`Space` | `None` | n/a (fixed set) | |
| Stop bits (serial) | one of `None`/`One`/`Two`/`OnePointFive` | `One` | n/a (fixed set) | |
| Host (tcp) | free text | empty | Required when Transport is `tcp` and Listen is off | Accepts a hostname, IPv4, or IPv6 literal — passed through as-is to `TcpTransport`/`.NET`'s own connect/resolve, not restricted to one format |
| Port (tcp) | integer, typed as text | `0` | Required, 1–65535, when Transport is `tcp` | |
| Listen (tcp) | boolean | off | none | Server mode; when on, Host is not required |
| Vendor ID (hid) | integer (decimal), typed as text | `0` | Required, 1–65535, when Transport is `hid` | Device Manager shows hex — see `CliOptions.HidVendorId`'s own doc comment for the conversion |
| Product ID (hid) | integer (decimal), typed as text | `0` | Required, 1–65535, when Transport is `hid` | Same decimal-only caveat as Vendor ID |
| Presenter | one of `ascii`/`utf8`/`hex`/`decimal`/`octal`/`binary` | `hex` | n/a (fixed set, every presenter `AddTextPresenters` registers) | Single-select today — see Open items |
| Line ending | one of `None`/`Cr`/`Lf`/`CrLf` | `None` | n/a (fixed set) | Appended to each typed line before sending |
| Save as profile named | free text | empty | Must be non-empty to save | Auto-filled with the loaded profile's name after Load (see Actions) |
| Import/export file path | free text (+ "Browse..." in WPF) | empty | Must be non-empty to import/export | Same file, same JSON shape, for both directions |
| Saved profiles | list, one name per saved profile | populated from `ConnectionProfileStore.List()` at construction | n/a | Selecting one doesn't load it by itself — press Load |

## Actions

| Action | Behavior | Preconditions | On failure |
|---|---|---|---|
| **Connect** | Validates the current fields (`CliOptionsValidator`); on success sets `Result` and raises `CloseRequested` | None | Shows the validation failure message; `Result` stays `null`, window stays open |
| **Close** (WPF) / **Quit** (TUI) | Discards changes; `Result` stays `null` | None | n/a |
| **Load** | Loads the selected saved profile's fields into the editor; also sets "Save as profile named" to that profile's name | A profile must be selected in the list | "Select a profile first." / the underlying `IOException`'s message if the file can't be read |
| **Save** | Validates the current fields; if the name already matches an existing profile, asks for confirmation first (a native dialog per front end); saves, refreshes the list, clears the name field | Name must be non-empty; fields must validate | Validation message, or "Not saved — '{name}' already exists." if overwrite is declined |
| **Delete** | Deletes the selected saved profile; refreshes the list; clears the selection | A profile must be selected | "Select a profile first." |
| **Refresh** | Re-reads the profiles directory (picks up a profile saved by another process, e.g. the other front end) | None | n/a |
| **Import** | Reads a `CliOptions`-shaped JSON file at the given path into the fields — does **not** save it as a profile by itself, review then Save | Path must be non-empty | "Could not import '{path}': {message}" — a missing/malformed file doesn't throw, it reports and leaves fields untouched |
| **Export** | Validates the current fields; writes them to the given path as JSON (same shape a saved profile uses) | Path must be non-empty; fields must validate | Validation message |

## States

- **Transport-based field-group visibility**: only the field group matching the selected Transport
  is shown (Serial / TCP / USB HID) — `IsSerialTransport`/`IsTcpTransport`/`IsHidTransport` on the
  view model, recomputed whenever `Transport` changes. Presenter/Line ending/Description/Save/
  Import-export are always visible regardless of Transport.
- **Status message**: a single line (`StatusMessage`) shows the most recent action's result or a
  validation failure — success and failure share the same field, there's no separate "error" vs.
  "info" styling today (WPF renders it in dark red regardless).

## Per-front-end notes

- **Widgets**: WPF uses a `ComboBox` for Transport/Presenter/Line ending/Parity/Stop bits, bound via
  `SelectedItem`. The TUI uses Terminal.Gui's `OptionSelector<TEnum>` (a radio-button-style
  selector) for the same five fields — it requires a real enum, so `ConfigureMode` declares two
  TUI-only enums (`TransportChoice`, `PresenterChoice`) purely to drive that widget, converting
  to/from the view model's plain strings; `Parity`/`StopBits`/`LineEnding` are already enums shared
  with `CliOptions` itself, no extra enum needed for those three.
- **Binding vs. push/pull**: WPF's controls stay continuously in sync with the view model via real
  `{Binding ...}` — editing a field updates the view model immediately, and vice versa. The TUI has
  no data-binding system, so `ConfigureMode` copies every field into the view model right before
  each command executes (`PushFieldsIntoViewModel`) and copies the view model's state back into the
  controls right after (`PullFieldsFromViewModel`), including the saved-profiles list and the status
  message.
- **Field-group visibility doesn't reflow the TUI's layout**: hiding the Serial group when Transport
  is `tcp`, for example, leaves the space it occupied blank rather than letting the TCP group move
  up to fill it — Terminal.Gui's `Pos.Bottom(view)` positioning is computed from a view's frame
  regardless of its `Visible` state. WPF's `Grid`/`StackPanel` layout collapses automatically when a
  panel's `Visibility` is `Collapsed`, so this doesn't affect WPF.
- **Saved-profiles list sizing**: WPF's list grows/shrinks proportionally with the window (a `Grid`
  row sized `1*` against the field editor's `2*`, both with a `MinHeight`). The TUI's list has a
  fixed height (4 rows) — Terminal.Gui's absolute-position layout doesn't have an equivalent to
  WPF's star-sized rows without a more involved container.
- **Import/export path entry**: WPF has a "Browse..." button (a native `OpenFileDialog`) next to the
  typed path field. The TUI only has the typed field — Terminal.Gui v2.5.0 does ship real file-picker
  dialogs (`Terminal.Gui.Views.OpenDialog`/`SaveDialog`/`FileDialog`, all public), so a TUI file
  browser is possible; it's just not wired up here yet (see Open items).
- **Overwrite confirmation and delete are native per front end**: WPF uses `MessageBox.Show`; the
  TUI uses `Terminal.Gui.Views.MessageBox.Query`. Both are wired through the same
  `ConnectionEditorViewModel.ConfirmOverwrite` hook so the view model itself has no UI dependency.

## Open items

Requested but not yet built, in the order they came up:

- **HID Vendor/Product ID as comboboxes** enumerating the real devices already connected to the
  local machine (reusing `SystemHidDeviceDiscovery`, the same discovery `--listhiddevices` uses),
  while still allowing a typed custom value, plus a decimal/hex display toggle. Today both are
  plain decimal-only text fields.
- **Multi-select Presenter.** Today it's single-select, even though the underlying `Pipeline`
  already supports fanning bytes out to multiple presenters at once — `CliOptions.Presenter` itself
  would need to become a list, which also touches `AddDevTermFrontEnd`'s single-presenter lookup and
  raises a real design question for the send path (which presenter encodes a typed line for sending,
  if more than one is active). Needs its own design pass, not a quick UI change.
- **Dirty-field confirmation.** No "you have unsaved changes — continue?" prompt on Load/Close/
  Connect when fields have been edited since the last Load/Save/Connect. Would need a tracked dirty
  flag on the view model (set on any field `PropertyChanged`, cleared on Load/Save/Connect).
- **Export-selected/export-all as a single zip, with per-name import conflict resolution**
  (ignore/rename/replace per profile, or "delete all and replace" wholesale). Today import/export is
  one profile, one JSON file, no conflict handling beyond the single-profile Save overwrite prompt —
  a real, larger feature (multi-select in the profiles list, zip creation/extraction, a conflict-
  resolution UI), not implemented yet.
- **Profiles-folder auto-refresh.** Only a manual Refresh button today; no `FileSystemWatcher` on
  `~/.dev-term/profiles`.
- **A TUI file-picker for the import/export path.** Terminal.Gui v2.5.0 has real file dialogs
  (`OpenDialog`/`SaveDialog`) — confirmed to exist, not yet wired to the TUI's path field.
- **Scrolling for a short terminal.** The TUI's form can be taller than a small terminal window —
  confirmed Terminal.Gui's `ListView`/`View` base class does have built-in scrollbar support
  (`HorizontalScrollBar`/`VerticalScrollBar` properties, a `ScrollBarVisibilityMode` for
  auto/always/never), but the editor `Window` itself isn't currently set up to scroll its content
  when it doesn't fit.
