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
| Port (serial) | free text, or picked from a "Detected ports"/"Detect..." list | empty | Required when Transport is `serial` | e.g. `COM3`, `/dev/ttyUSB0`; the list is whatever `ISerialPortDiscovery.GetPortNames()` (the same enumeration `--listports` uses) finds attached right now, captured once at construction |
| Baud (serial) | integer, typed as text | `9600` | Parsed with `int.TryParse`; unparseable input is silently ignored (keeps the previous value) | |
| Data bits (serial) | integer, typed as text | `8` | Same parse behavior as Baud | |
| Parity (serial) | one of `None`/`Odd`/`Even`/`Mark`/`Space` | `None` | n/a (fixed set) | |
| Stop bits (serial) | one of `None`/`One`/`Two`/`OnePointFive` | `One` | n/a (fixed set) | |
| Host (tcp) | free text | empty | Required when Transport is `tcp` and Listen is off | Accepts a hostname, IPv4, or IPv6 literal — passed through as-is to `TcpTransport`/`.NET`'s own connect/resolve, not restricted to one format |
| Port (tcp) | integer, typed as text | `0` | Required, 1–65535, when Transport is `tcp` | |
| Listen (tcp) | boolean | off | none | Server mode; when on, Host is not required |
| Vendor ID (hid) | integer, typed as decimal or 4-digit hex (per "Show as hex"), or picked (with Product ID together) from a "Detected devices"/"Detect..." list | `0` | Required, 1–65535, when Transport is `hid` | Stored/validated as decimal internally regardless of display format — see `ConnectionEditorViewModel.HidVendorIdDisplay`; the picker list is whatever `IHidDeviceDiscovery.GetDevices()` (the same enumeration `--listhiddevices` uses) finds attached right now, formatted `"{VID:X4}:{PID:X4}  {ProductName}"` |
| Product ID (hid) | integer, typed as decimal or 4-digit hex, or picked together with Vendor ID (see above) | `0` | Required, 1–65535, when Transport is `hid` | Same as Vendor ID |
| Show as hex (hid) | boolean | off (decimal) | n/a | Toggles Vendor ID/Product ID's display and typed-input format between decimal and 4-digit uppercase hex (no `0x` prefix, matching `--listhiddevices`'s own formatting) — a display preference only, not part of a saved profile, and doesn't mark the editor dirty by itself |
| Presenter | one of `ascii`/`utf8`/`hex`/`decimal`/`octal`/`binary` | `hex` | n/a (fixed set, every presenter `AddTextPresenters` registers) | Single-select today — see Open items |
| Line ending | one of `None`/`Cr`/`Lf`/`CrLf` | `None` | n/a (fixed set) | Appended to each typed line before sending |
| Save as profile named | free text | empty | Must be non-empty to save | Auto-filled with the loaded profile's name after Load (see Actions) |
| Import/export file path | free text, or picked via "Browse..." (existing file) / "Save As..." (new or existing file), both front ends | empty | Must be non-empty to import/export | Same file, same JSON shape, for both directions |
| Saved profiles | list, one name per saved profile | populated from `ConnectionProfileStore.List()` at construction | n/a | Double-clicking a row loads it — same as selecting it and pressing Load, not a separate action |

## Actions

| Action | Behavior | Preconditions | On failure |
|---|---|---|---|
| **Connect** | Validates the current fields (`CliOptionsValidator`); on success sets `Result`, clears the dirty flag, and raises `CloseRequested` | None | Shows the validation failure message; `Result` stays `null`, window stays open |
| **Close** (WPF) / **Quit** (TUI) | If fields have unsaved edits, asks for confirmation first; otherwise (or once confirmed) discards changes and `Result` stays `null` | None | Declining the confirmation leaves the editor open, untouched |
| **Load** (button, or double-clicking the row) | If fields have unsaved edits, asks for confirmation first; otherwise (or once confirmed) loads the selected saved profile's fields into the editor and sets "Save as profile named" to that profile's name | A profile must be selected in the list | "Select a profile first." / "Load cancelled — you have unsaved changes." if declined / the underlying `IOException`'s message if the file can't be read |
| **Save** | Validates the current fields; if the name already matches an existing profile, asks for confirmation first (a native dialog per front end); saves, refreshes the list, clears the name field | Name must be non-empty; fields must validate | Validation message, or "Not saved — '{name}' already exists." if overwrite is declined |
| **Delete** | Deletes the selected saved profile; refreshes the list; clears the selection | A profile must be selected | "Select a profile first." |
| **Refresh** | Re-reads the profiles directory (picks up a profile saved by another process, e.g. the other front end) | None | n/a — mostly redundant now that a real `FileSystemWatcher` does this automatically (see States), kept as a manual fallback |
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
- **Saved-profiles list auto-refreshes**: a real `FileSystemWatcher` on the profiles directory
  (`ConnectionEditorViewModel`'s constructor) raises `ProfilesChangedExternally` whenever a profile
  is added/removed/renamed on disk by anything other than this view model instance — the other
  front end, or a user editing `~/.dev-term/profiles` by hand. Each front end marshals that onto its
  own UI thread and calls the same `RefreshCommand` the Refresh button does. Disposed when the
  editor closes (`IDisposable`); if the watcher can't be created at all (e.g. a permissions problem
  on the profiles directory), the editor still works, just without auto-refresh — the manual Refresh
  button always works regardless.
- **Dirty tracking**: `IsDirty` flips true the moment any field changes (Transport, Port, Baud, ...
  — everything except `StatusMessage`/`SelectedProfileName`/the `Is*Transport` flags themselves) and
  clears on a successful Load, Save, Connect, or Import. Close/Quit and Load both check it via
  `ConfirmClose()` before discarding whatever's currently unsaved; Connect doesn't need the check
  since nothing is discarded by connecting with the fields as shown. Freshly opening the editor
  (including from a saved profile's initial values) is never dirty — only an edit made *after* that
  counts.

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
- **Import/export path entry**: both front ends have a "Browse..." button next to the typed path
  field — WPF's opens a native `OpenFileDialog` (code-behind, the one exception to pure command
  binding, since a native dialog has no binding equivalent); the TUI's opens a real
  `Terminal.Gui.Views.OpenDialog` the same way (`Application.Run(dialog)`, then reads `dialog.FilePaths`
  if not `dialog.Canceled`) — an open-style picker (requires an existing file), the right fit for
  Import. A second "Save As..." button next to it is the save-style counterpart, for Export
  specifically: WPF's native `SaveFileDialog`, the TUI's `Terminal.Gui.Views.SaveDialog` (reading
  `dialog.FileName`, not `.Path`, once accepted) — both let you type or navigate to a brand-new
  filename that doesn't exist yet, not just pick among existing ones. Browse still works for Export
  too (if the target file already exists); Save As isn't meant for Import (nothing stops picking a
  non-existent path there, but Import will just report the resulting "file not found").
- **The TUI's form scrolls; WPF's doesn't need to** — the TUI form (~33 rows) routinely exceeds a
  default terminal window, so its content sits in a `View` with a real Terminal.Gui viewport/scrollbar
  (`SetContentSize` + `ViewportSettings |= AllowNegativeY | HasVerticalScrollBar`), scrollable via
  PageUp/PageDown (bound on `Application.KeyDown`, skipped while the saved-profiles `ListView` has
  focus so it doesn't steal that list's own PageUp/PageDown/arrow-key navigation — confirmed via
  reflection that `ListView` already binds all of those itself) or the mouse wheel (bound on the
  scrollable container directly, so a wheel over the profiles list itself still scrolls *that* list
  first). Confirmed via a real headless probe against the installed Terminal.Gui v2.5.0 package that
  `View` has no built-in `Command.ScrollDown`/`PageDown` implementation to invoke instead — both keys
  and the wheel are wired by hand. WPF's `ScrollViewer` around the field editor already handled this
  automatically from the start (see the WPF screenshots — the window itself is simply taller/resizable).
- **Overwrite/discard confirmations and delete are native per front end**: WPF uses
  `MessageBox.Show`; the TUI uses `Terminal.Gui.Views.MessageBox.Query`. Both are wired through the
  same `ConnectionEditorViewModel.ConfirmOverwrite`/`ConfirmDiscardChanges` hooks so the view model
  itself has no UI dependency.
- **Detected-hardware pickers fill fields rather than binding directly to them**: `Port` and
  `HidVendorId`/`HidProductId` stay plain, freely-typable fields; a separate `SelectedSerialPort`/
  `SelectedHidDevice` property on the view model is what a picker actually binds to, and setting it
  copies the choice into the real field(s) (`SelectedHidDevice` sets both Vendor and Product ID
  together, since they identify one device). Deliberately not the same property, both to keep typing
  a custom value simple and because a WPF editable `ComboBox`'s `Text` and `SelectedItem` don't share
  one format cleanly once the display string (`"046D:C08B  G502 HERO Gaming Mouse"`) differs from the
  plain decimal the field actually stores. WPF renders this as a second, non-editable `ComboBox`
  ("Detected ports:"/"Detected devices:") next to the real field; the TUI renders it as a "Detect..."
  button that opens a small modal picker (a plain `Dialog` + `ListView`, `Application.Run(dialog)` —
  Terminal.Gui has no built-in combobox widget, confirmed via reflection against the installed
  v2.5.0 package). Both are empty (not an error) if nothing's detected or discovery itself fails.
- **The HID decimal/hex toggle is display-only, backed by a separate `*Display` property per
  field** (`HidVendorIdDisplay`/`HidProductIdDisplay`), not `HidVendorId`/`HidProductId` themselves
  — those two stay canonical decimal strings always (what `BuildOptions`/`LoadIntoFields`/
  `SelectedHidDevice` all read and write), so validation/Save/Connect/profile storage never need to
  know or care which format the user is currently viewing. Toggling `HidIdsShowHex` reformats
  whatever's already entered rather than requiring it to be retyped. WPF binds a `TextBox` directly
  to `HidVendorIdDisplay`/`HidProductIdDisplay` (deliberately *not* re-raising that property's own
  change notification from within its own setter — only from `HidIdsShowHex`'s setter or from
  `HidVendorId`/`HidProductId` changing some other way, e.g. Load or the detected-devices picker —
  so a bound `TextBox` doesn't get its text reformatted, and its caret reset to the end, after every
  single keystroke). The TUI has no continuous binding to fight the same way — its "Show as hex"
  `CheckBox` reformats the two fields immediately on toggle anyway, via its own `Activated` handler
  (confirmed via a headless probe that `Activated` fires *after* `Value` has already flipped).
- **Double-click-to-load is a pure command binding in WPF, an event handler calling the same
  command in the TUI**: WPF's `ListBox` has no XAML way to bind a routed mouse event directly to an
  `ICommand`, but it does support `<ListBox.InputBindings><MouseBinding MouseAction="LeftDoubleClick"
  Command="{Binding LoadCommand}" /></ListBox.InputBindings>` — no code-behind at all. The TUI's
  `ListView` has no such binding concept; a double-click maps to `Command.Accept` there (confirmed
  via reflection — a single click maps to a different command, `Activate`), so `ConfigureMode`
  subscribes `profilesList.Accepting` to the same local `LoadSelectedProfile()` function the Load
  button's own `Accepting` handler calls — one shared code path, not a duplicated one.

## Open items

Requested but not yet built, in the order they came up:

- **A long/short name for a detected serial port.** The "Detected ports" picker lists whatever
  `SerialPort.GetPortNames()` returns, which is short names only (`COM3`) on every platform — no
  cross-platform equivalent of Windows' WMI-based friendly name (`"USB Serial Device (COM3)"`) was
  wired up, to avoid a Windows-only code path in an otherwise cross-platform discovery.
- **Multi-select Presenter.** Today it's single-select, even though the underlying `Pipeline`
  already supports fanning bytes out to multiple presenters at once — `CliOptions.Presenter` itself
  would need to become a list, which also touches `AddDevTermFrontEnd`'s single-presenter lookup and
  raises a real design question for the send path (which presenter encodes a typed line for sending,
  if more than one is active). Needs its own design pass, not a quick UI change.
- **Export-selected/export-all as a single zip, with per-name import conflict resolution**
  (ignore/rename/replace per profile, or "delete all and replace" wholesale). Today import/export is
  one profile, one JSON file, no conflict handling beyond the single-profile Save overwrite prompt —
  a real, larger feature (multi-select in the profiles list, zip creation/extraction, a conflict-
  resolution UI), not implemented yet.
