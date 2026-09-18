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
| Port (serial) | free text, or picked from a "Detected ports"/"Detect..." list | empty | Required when Transport is `serial` | e.g. `COM3`, `/dev/ttyUSB0`; the list is whatever `ISerialPortDiscovery.GetPortNames()` (the same enumeration `--listports` uses) finds attached right now, captured once at construction; on Windows each entry is shown as `COM3 — Prolific USB-to-Serial Comm Port` (see Per-front-end notes), but only the short name is written into the field |
| Baud (serial) | integer, typed as text | `9600` | Parsed with `int.TryParse`; unparseable input is silently ignored (keeps the previous value) | |
| Data bits (serial) | integer, typed as text | `8` | Same parse behavior as Baud | |
| Parity (serial) | one of `None`/`Odd`/`Even`/`Mark`/`Space` | `None` | n/a (fixed set) | |
| Stop bits (serial) | one of `None`/`One`/`Two`/`OnePointFive` | `One` | n/a (fixed set) | |
| Host (tcp) | free text | empty | Required when Transport is `tcp` and Listen is off | Accepts a hostname, IPv4, or IPv6 literal — passed through as-is to `TcpTransport`/`.NET`'s own connect/resolve, not restricted to one format |
| Port (tcp) | integer, typed as text | `0` | Required, 1–65535, when Transport is `tcp` | |
| Listen (tcp) | boolean | off | none | Server mode; when on, Host is not required |
| Vendor ID (hid) | integer, typed as decimal or 4-digit hex (per "Show as hex"), or picked (with Product ID together) from a "Detected devices"/"Detect..." list | `0` | Required, 1–65535, when Transport is `hid` | Stored/validated as decimal internally regardless of display format — see `ConnectionEditorViewModel.HidVendorIdDisplay`; the picker list is whatever `IHidDeviceDiscovery.GetDevices()` (the same enumeration `--listhiddevices` uses) finds attached right now, formatted `"{VID:X4}:{PID:X4}  {ProductName}"`. The picker is **filtered by the Vendor/Product ID fields**: a non-zero id keeps only devices with that id, `0` means any (see Per-front-end notes) |
| Product ID (hid) | integer, typed as decimal or 4-digit hex, or picked together with Vendor ID (see above) | `0` | Required, 1–65535, when Transport is `hid` | Same as Vendor ID |
| Show as hex (hid) | boolean | off (decimal) | n/a | Toggles Vendor ID/Product ID's display and typed-input format between decimal and 4-digit uppercase hex (no `0x` prefix, matching `--listhiddevices`'s own formatting) — a display preference only, not part of a saved profile, and doesn't mark the editor dirty by itself |
| Presenters | any non-empty subset of `ascii`/`utf8`/`hex`/`decimal`/`octal`/`binary` (a row of checkboxes) | `hex` | At least one must be checked — "Select at least one presenter." (n/a otherwise: fixed set, every presenter `AddTextPresenters` registers) | **Display only**: every checked presenter renders each incoming chunk, side by side, each output line tagged `[name]`. Stored as `CliOptions.Presenter`, a JSON array in a saved profile (`"Presenter": ["ascii", "hex"]`); a profile saved before this became a list (`"Presenter": "hex"`) still loads, as does the command-line/environment form `--presenter ascii,hex` — see `DevTermConfiguration.Bind`. Nothing here affects what is *sent* — see Send as |
| Send as | one of `ascii`/`utf8`/`hex`/`decimal`/`octal`/`binary` | the first checked presenter (a profile with no `Parser`, i.e. one saved before this existed, sends as its first presenter — what it always did) | n/a (fixed set) | The **parser**: which presenter's input encoding (`IPresenterInput.Parse`) turns a typed line into bytes. Independent of Presenters. Stored as `CliOptions.Parser` (`--parser`). This is only the *starting* value: the main windows can switch it per typed line — see Per-front-end notes |
| Line ending | one of `None`/`Cr`/`Lf`/`CrLf` | `None` | n/a (fixed set) | Appended to each typed line before sending |
| Save as profile named | free text | empty | Must be non-empty to save | Auto-filled with the loaded profile's name after Load (see Actions) |
| Import/export file path | free text, or picked via "Browse..." (existing file) / "Save As..." (new or existing file), both front ends | empty | Must be non-empty to import/export | A single-profile JSON path for Import/Export, or a `.zip` path (detected by extension) for Import/Export Selected/Export All — see Actions |
| Saved profiles | list, one name per saved profile, multi-select | populated from `ConnectionProfileStore.List()` at construction | n/a | Double-clicking a row loads it — same as selecting it and pressing Load, not a separate action. Multiple rows can be marked/selected at once — see Export Selected below — independent of the single-item selection Load/Delete use |

## Actions

| Action | Behavior | Preconditions | On failure |
|---|---|---|---|
| **Connect** | Validates the current fields (`CliOptionsValidator`); on success sets `Result`, clears the dirty flag, and raises `CloseRequested` | None | Shows the validation failure message; `Result` stays `null`, window stays open |
| **Close** (WPF) / **Quit** (TUI) | If fields have unsaved edits, asks for confirmation first; otherwise (or once confirmed) discards changes and `Result` stays `null` | None | Declining the confirmation leaves the editor open, untouched |
| **Load** (button, or double-clicking the row) | If fields have unsaved edits, asks for confirmation first; otherwise (or once confirmed) loads the selected saved profile's fields into the editor and sets "Save as profile named" to that profile's name | A profile must be selected in the list | "Select a profile first." / "Load cancelled — you have unsaved changes." if declined / the underlying `IOException`'s message if the file can't be read |
| **Save** | Validates the current fields; if the name already matches an existing profile, asks for confirmation first (a native dialog per front end); saves, refreshes the list, clears the name field | Name must be non-empty; fields must validate | Validation message, or "Not saved — '{name}' already exists." if overwrite is declined |
| **Delete** | Deletes the selected saved profile; refreshes the list; clears the selection | A profile must be selected | "Select a profile first." |
| **Refresh** | Re-reads the profiles directory (picks up a profile saved by another process, e.g. the other front end) | None | n/a — mostly redundant now that a real `FileSystemWatcher` does this automatically (see States), kept as a manual fallback |
| **Import** | If the path ends in `.zip`, imports every `*.json` entry straight into the profile store (see Export Selected/Export All) and refreshes the list — does **not** load anything into the fields. Otherwise, reads a single `CliOptions`-shaped JSON file at the given path into the fields — does **not** save it as a profile by itself, review then Save | Path must be non-empty | "Could not import '{path}': {message}" — a missing/malformed file doesn't throw, it reports and leaves fields untouched |
| **Replace All** | Restore-from-backup: reads the `.zip` at the given path, then deletes **every** saved profile (including ones the zip doesn't mention) and writes every profile in the zip. The whole archive is read and checked (each `*.json` entry must be valid JSON) *before* anything is deleted, then a native confirmation gives the saved-profile and zip-profile counts — skipped when nothing is saved yet, since there's nothing to lose. Clears the single and multi selections | Path must be non-empty and end in `.zip` | "Type a file path to import from." / "Replace All needs a .zip file …" / "Could not import '{path}': … Nothing was deleted." (unreadable/invalid zip) / "'{path}' contains no profiles. Nothing was deleted." / "Replace All cancelled." if declined / "Replace All failed partway: …" on a mid-write I/O error (see Per-front-end notes) |
| **Export** | Validates the current fields; writes them to the given path as JSON (same shape a saved profile uses) | Path must be non-empty; fields must validate | Validation message |
| **Export Selected** | Writes every profile marked/selected in the saved-profiles list to the given path as a single zip (one `{name}.json` entry per profile, the exact bytes already on disk, not a re-serialized round trip) | Path must be non-empty; at least one profile must be marked/selected | "Select one or more saved profiles to export first." / the underlying I/O exception's message |
| **Export All** | Same as Export Selected, but always writes every saved profile regardless of what's marked/selected | Path must be non-empty | Same as Export Selected |
| **Delete Selected** | Asks for confirmation naming the profiles about to go (a native dialog per front end — unlike single **Delete**, which doesn't ask), then deletes every profile marked/selected in the saved-profiles list, refreshes the list, and clears both the multi-selection and (if it was one of them) the single selection | At least one profile must be marked/selected | "Select one or more saved profiles to delete first." / "Delete cancelled." if declined / "Deleted N profile(s). M not found." when a profile had already been removed by another process |

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

- **Widgets**: WPF uses a `ComboBox` for Transport/Send as/Line ending/Parity/Stop bits, bound via
  `SelectedItem`, and an `ItemsControl` of `CheckBox`es (a `WrapPanel`) for Presenters, each bound to
  its `PresenterSelection.IsSelected` in `ConnectionEditorViewModel.PresenterChoices`. The TUI uses
  Terminal.Gui's `OptionSelector<TEnum>` (a radio-button-style selector) for Transport/Send as/Line
  ending/Parity/Stop bits — it requires a real enum, so `ConfigureMode` declares two TUI-only enums
  (`TransportChoice`, `PresenterChoice` — the latter drives Send as) purely to drive that widget,
  converting to/from the view model's plain strings; `Parity`/`StopBits`/`LineEnding` are already
  enums shared with `CliOptions` itself, no extra enum needed for those three. Presenters is
  multi-select, which a radio selector can't do, so the TUI uses one `CheckBox` per presenter on a
  single row instead, copied to/from `PresenterChoices` by push/pull like every other field.
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
- **Overwrite/discard/bulk-delete confirmations are native per front end**: WPF uses
  `MessageBox.Show`; the TUI uses `Terminal.Gui.Views.MessageBox.Query`. Both are wired through the
  same `ConnectionEditorViewModel.ConfirmOverwrite`/`ConfirmDiscardChanges`/`ConfirmDeleteProfiles`
  hooks so the view model itself has no UI dependency. Single-profile **Delete** doesn't confirm.
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
- **A detected serial port's description is a separate lookup, not part of the port list.**
  `ISerialPortDiscovery` keeps `GetPortNames()` as-is (the `--listports` and `SerialPort` contract)
  and gains a default-interface-method `GetPortDescriptions()` returning `port name → description`
  (empty by default, so every existing implementation/fake keeps compiling). The view model builds
  a `SerialPortOption(Name, Display)` per name from `GetPortNames()` and looks each one up
  case-insensitively: `Display` is `"COM3 — Prolific USB-to-Serial Comm Port"`, or just `"COM3"` when
  there's no description. Descriptions **only decorate ports `GetPortNames()` reported** — Windows'
  Plug-and-Play registry remembers devices unplugged long ago (this machine's has a stale Prolific
  COM3 with nothing attached), so it can never be a source of ports. A throwing description lookup
  degrades to short names rather than failing construction, same as a throwing port lookup already
  yields an empty list. `SelectedSerialPort` and `Port` still hold the short name only: WPF's
  `DetectedPortsBox` uses `DisplayMemberPath="Display"` with `SelectedValuePath="Name"`/
  `SelectedValue="{Binding SelectedSerialPort}"`; the TUI's Detect picker lists `Display` and writes
  the chosen entry's `Name`.
- **The Windows description comes straight from the registry, not WMI.**
  `WindowsSerialPortDescriptions` walks `HKLM\SYSTEM\CurrentControlSet\Enum\{bus}\{device}\{instance}`,
  pairing each instance's `Device Parameters\PortName` with its `FriendlyName`, then strips the
  redundant trailing `(COMn)` Windows appends (`SystemSerialPortDiscovery.StripPortSuffix`), since the
  picker already shows the name next to it. Chosen over `Win32_PnPEntity` because it needs no new
  package and doesn't start the WMI service (~1 s cold vs ~6 ms measured here); read-only, no
  elevation. Unreadable keys are skipped; if a port name appears under several stale instances the
  first wins. Verified against this machine's real registry (found the stale COM3 entry, correctly
  not listed because nothing's attached) — **not** verified with a real device attached, since none
  is available right now.
- **The HID picker is filtered by the ID fields, live.** `HidDeviceOptions` is not the raw
  discovery result: it's `detected.Where(d => (vendorId == 0 || d.VendorId == vendorId) &&
  (productId == 0 || d.ProductId == productId))`, so typing a vendor id narrows the picker to that
  vendor's devices, adding a product id narrows it to one, and `0` (the default) lists everything.
  An id that isn't a number yet (mid-typing, or a bad value) counts as `0` rather than emptying the
  list. Recomputed whenever the canonical `HidVendorId`/`HidProductId` change — from typing, the hex
  display field, Load, or picking a device — by editing an `ObservableCollection` in place (remove
  what no longer matches, insert what now does, detection order), not by replacing the list: a
  bound WPF `ComboBox` follows it live and keeps a selection that still matches, where a replaced
  `ItemsSource` would reset it. It is exposed as `IReadOnlyList<HidDeviceOption>`; the
  `ObservableCollection` is the runtime type. `HidDevicesHiddenByFilter` (some detected device is
  hidden only by the filter) lets the TUI say "No detected HID device matches the Vendor/Product ID
  entered (0 means any)" instead of a misleading "Nothing was detected." for an empty picker. The
  TUI's fields only reach the view model when pushed, so its Detect button pushes them first.
  Consequence worth knowing: **picking a device fills in both ids, which then narrows the list to
  just that device** — to pick a different one, clear an id (or set it to 0) first. A device whose
  own vendor id is 0 is not treated as a wildcard.
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
- **Double-click-to-load is a per-row `MouseDoubleClick` handler in WPF, an event handler calling
  the same command in the TUI**: the original WPF version was a `<ListBox.InputBindings><MouseBinding
  MouseAction="LeftDoubleClick" Command="{Binding LoadCommand}" />` (pure binding, no code-behind) and
  **never actually fired for a real double-click** — `ListBoxItem` marks the mouse-down handled in
  order to select the row, so the `ListBox`'s own `InputBindings` never see it (it would only fire on
  the empty area below the last row). Its test only asserted that the binding's `Command` was
  `LoadCommand`, so it passed regardless. Fixed 2026-09-18: `ListBox.ItemContainerStyle` has an
  `EventSetter` for `MouseDoubleClick` (which the `Control` base class raises even for a handled
  mouse-down) → `ProfilesList_ItemDoubleClick`, which selects the clicked row — so a ctrl/shift
  multi-selection can't leave Load acting on a different profile — and executes the same
  `LoadCommand` the button is bound to. The tests now send real `MouseDown` events with
  `ClickCount` 1 and 2 through the row (`DeviceProfilesWindowTests.ClickRow`). The TUI's `ListView` has no such binding concept; a double-click maps to `Command.Accept` there (confirmed
  via reflection — a single click maps to a different command, `Activate`), so `ConfigureMode`
  subscribes `profilesList.Accepting` to the same local `LoadSelectedProfile()` function the Load
  button's own `Accepting` handler calls — one shared code path, not a duplicated one.

- **Export Selected/Delete Selected's multi-select is a separate collection from the single-item `SelectedProfileName`
  Load/Delete use** (`ConnectionEditorViewModel.SelectedProfileNames`, a plain `ObservableCollection<string>`
  each front end populates itself, since neither front end's list control notifies the view model
  live as marks/selection change). WPF's `ListBox` uses `SelectionMode="Extended"` (ctrl/shift-click)
  with a code-behind `SelectionChanged` handler mirroring `SelectedItems` into it — `ListBox.SelectedItems`
  has no dependency property of its own to bind two-way in pure XAML, the one other code-behind
  exception besides the native file dialogs. The TUI's `ListView` uses `MarkMultiple`/`ShowMarks`
  (checkbox-style marks, SPACE to toggle — confirmed against the installed Terminal.Gui v2.5.0
  package that `ListWrapper<T>`, what `SetSource` builds, already implements the `IsMarked`/`SetMark`
  storage needed) with no live-sync event at all; `ConfigureMode` just reads `GetAllMarkedItems()`
  right before the Export Selected/Delete Selected button's own command runs, the same "copy into the
  view model right before the command executes" pattern already used for the single-item case.
  In the TUI, Delete Selected sits on the Export Selected/Export All row (an 80-column window has no
  room for a fourth button on the Load/Delete/Refresh row); in WPF it's in the button column under Delete.
- **Replace All is two store calls, not one, on purpose**: `ConnectionProfileStore.ReadZip(path)`
  (static, no side effects — reads every `*.json` entry and rejects invalid JSON, naming the entry)
  and then `ReplaceAll(profiles)` (deletes every saved profile, writes the given ones). Splitting
  them means nothing is deleted unless the entire archive was readable, and it gives the
  confirmation real counts to show. It isn't transactional past that point — an I/O failure while
  writing leaves whatever had been written (the status line says "failed partway"); staging into a
  temp folder and swapping would close that, and was judged more machinery than a failure that needs
  a full disk or a permissions change mid-operation warrants. A name that appears twice in the zip
  (entries in different folders) keeps the last one. Confirmation is the
  `ConnectionEditorViewModel.ConfirmReplaceAllProfiles` hook, `Func<int, int, bool>` (saved count,
  zip count), same "front end supplies a native dialog, unwired = proceeds" shape as
  `ConfirmDeleteProfiles`. Placement: WPF's Import/Export row (between them), the TUI's
  Browse/Import/Export/Save As row.
- **A zip import's per-name conflict resolution is a `Func<string, ZipImportConflictResolution>`
  hook** (`ConnectionEditorViewModel.ResolveZipImportConflict`), called once per name already in the
  store — same "front end supplies a native dialog, view model has no UI dependency" shape as
  `ConfirmOverwrite`/`ConfirmDiscardChanges`. WPF maps a three-way `MessageBox.Show` (Yes/No/Cancel)
  onto Replace/Rename/Skip; the TUI maps a three-button `MessageBox.Query` the same way. Left
  unwired (e.g. in a test), every conflict defaults to Replace, matching the existing "proceed
  without asking" convention for the other two confirmation hooks.
- **Send as, in the main windows** (not the editor itself, but where the parser is actually used):
  the WPF main window has a "Send as:" `ComboBox` beside the Send button, the TUI a "Send as" menu-bar
  menu with one entry per presenter that can encode input. Both start at the profile's parser and
  switch it for every line typed afterward (the title bar shows the current one, e.g.
  `dev-term — TCP 192.168.0.107:23 (ascii, hex; send as hex)`); switching profiles resets it to the new
  profile's. The plain CLI has no such control — a stdin loop has no non-colliding way to say "this
  line is hex" — so it uses `--parser` for the whole run.

## Open items

Requested but not yet built, prioritized 2026-09-16 (the presenter picker and per-input-line parser both landed 2026-09-18 — see Fields and Per-front-end notes):

- **Serial-port descriptions on Linux/macOS.** The "Detected ports" picker decorates each port with
  the OS's description on Windows only (landed 2026-09-18, see Per-front-end notes); Linux
  (`/sys/class/tty/*/device` → udev/`ID_MODEL`) and macOS (IOKit) still show short names only.
  `ISerialPortDiscovery.GetPortDescriptions()` is the seam — nothing else changes to add them.
