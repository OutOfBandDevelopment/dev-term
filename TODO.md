# TODO

Active / in-progress work for dev-term. Not-yet-started backlog and research work moved out to
[`BACKLOG.md`](BACKLOG.md) (2026-09-16) to keep this file to what's actually being worked on.
Completed work is logged by date under `docs/changes/`.

## In progress

- **TUI + WPF front ends.** Landed: the shared CLI-config pieces (`CliOptions`,
  `CliOptionsValidator`, `DevTermConfiguration`, `ConnectionErrorMessages`, `LineEnding`) moved out
  of `DevTerm.Console` into `DevTerm.Configuration`, plus a new `AddDevTermFrontEnd`/
  `ConnectionDescription` shared by every front end — a saved `appsettings.Local.json` profile now
  works from any of them. `DevTerm.Console` gained a `--tui <bool>` flag dispatching to a new
  `TuiMode` (Terminal.Gui v2.5.0: a `Window` with a `TextView` output pane and a `TextField` send
  box) alongside the existing `CliMode`. A new `DevTerm.Wpf` project (WPF, `net10.0-windows`) mirrors
  this with a `ListBox` output + send box, composing the DI graph itself in `App.xaml.cs` (WPF has
  no `Main`/host-builder entry point of its own) and linking the console app's
  `appsettings(.Local).json` into its own output so the same saved profile applies. Terminal.Gui
  2.5.0's static `Application` API (`Init`/`Run`/`Invoke`/`Shutdown`) is marked obsolete in favor of
  an instance-based `IApplication` — left as-is for this stub since the static API still works and
  the replacement is a bigger, unproven-in-this-project API surface; revisit if/when Terminal.Gui
  actually removes it. **Update**: `MainWindow` now has real test coverage (`DevTerm.Wpf.Tests`),
  including opt-in automation against real hardware — see the test-automation entry below. `TuiMode`
  still has none; Terminal.Gui's own headless-testing support hasn't been investigated yet. The
  multi-session-lifetime issue below is also still open.

- **UI Definitions model** (`DevTerm.UiDefinitions`), landed 2026-09-15 — a framework-agnostic,
  JSON/XML-serializable model for declaring a device control panel once (`UiDefinition` →
  `UiSection`s → seven `UiControl` kinds: button/toggle/slider/numeric/choice/textField/indicator),
  so every front end can render it generically instead of hand-coding UI per device per front end.
  Built from real device mockups already written (Kuando Busylight, Velleman K8055, EByte, Zoom
  H4n), not designed in the abstract — see docs/design/ui-definitions.md. Polymorphic serialization
  uses the framework's own support (`System.Text.Json`'s `[JsonDerivedType]`, `XmlSerializer`'s
  `[XmlElement]` per derived type on the collection) rather than hand-rolled discriminator parsing.
  Round-trip tested against a full real panel (the Busylight mockup, reproduced as data). **This is
  step one only**: nothing yet reads this model to produce real Terminal.Gui or WPF controls, and
  it isn't wired to `IControlSurface` (still design-only) or any live device. Next real targets to
  build the actual TUI/WPF renderers against, per explicit plan: the K8055 (plugged in) and the
  Busylight (already verified both directions) — both already have `@startsalt` mockups this model
  needs to be able to reproduce as real, working controls.

- **Device Manifests** (`DevTerm.DeviceManifests`), landed 2026-09-15 — a no-code `DeviceManifest`
  (identity, a transport hint, the declarative command/response schema already sketched in
  device-control-modules.md, and a `UiDefinition`) plus a `DeviceManifestLoader` handling all three
  shapes from docs/design/device-manifests.md: a single JSON file, a folder (`device.json` at its
  root, referenced files resolved relative to it), or a `.zip` of one (extracted, then loaded
  exactly like a folder). Found and fixed a real bug immediately via testing: `XmlSerializer`
  can't serialize `Dictionary<string,string>` at all (throws at reflection time) — switched
  `TransportHint.Options` to a `List<TransportOption>` Key/Value pair list, which both JSON and XML
  handle natively; noted in `CLAUDE.md` as a constraint for any future XML-round-tripped type.
  7 tests (JSON/XML round-trip with an inline UI, folder-mode with an external UI file, zip-mode,
  missing-referenced-file failures) — 147 tests across the solution now. **Step one only, same as
  UI Definitions**: the manifest only *references* a Kaitai `.ksy` file by path, doesn't parse one;
  nothing turns a loaded manifest into a working `IControlSurface`/decoder pair or opens a
  connection from it.

- **Connection profiles + default-mode flip**, landed 2026-09-15 — see
  docs/design/connection-profiles.md. Landed today: `CliOptions.ManifestName` (a device manifest
  *name*, resolved via `DevTermUserDataPaths.ResolveManifestDirectory` — checks
  `~/.dev-term/manifests/{name}` first, then `./manifests/{name}` for pre-packaged ones — not a
  literal path, so a saved profile stays portable); `ConnectionProfileStore` (save/list/load/delete
  named, `CliOptions`-shaped JSON profiles under `~/.dev-term/profiles/`, reusing the same
  `Microsoft.Extensions.Configuration.Json` + `Bind()` pipeline that already loads
  `appsettings.Local.json` rather than a parallel type); `DevTermConfiguration.SaveLocalProfile`/
  `ToProfileJson` (projects just the connection-relevant fields, excluding one-shot/mode flags).
  Also: **the TUI is now the console app's default mode** — `--cli true` (or `--tui false`) forces
  the plain scriptable loop; verified live against real hardware both ways. 160 tests across the
  solution now.

  **Update, 2026-09-15**: the TUI's Configure screen landed — `DevTerm.Console.ConfigureMode`, a
  real Terminal.Gui form (transport/connection fields, a saved-profiles `ListView` with Load, a
  Save-as-profile field, Connect/Quit) shown instead of hard-failing when `CliOptions` doesn't
  validate; `Program.cs` now binds+validates `CliOptions` *before* building the DI host so it can
  make that call ahead of ever wiring a transport. Both front ends also got a real "File > Device
  Profiles..." menu item (TUI: a new `MenuBar` in `TuiMode.BuildWindow`, reusing `ConfigureMode` as
  a nested modal; WPF: a new `DeviceProfilesWindow`) — landed at a reduced scope from the design
  doc's live mid-session switching: picking a profile saves it as the default and asks for a
  restart, since live-swapping the running session's transport needs the DI-composed transport
  rebuilt, a bigger change on its own. 5 new `ConfigureModeTests` (`UNIT`) needed a third distinct
  Terminal.Gui test-automation technique beyond the two `TuiTestRunner` already has —
  `View.InvokeCommand(Command.Accept)` — after both `SetFocus()`-then-inject and Tab-navigation
  proved unreliable for simulating a button click; see docs/design/testing.md and CLAUDE.md's
  constraints list for the full account, including the two separate bugs found and fixed landing
  the TUI's menu: Ctrl+Q was advertised in the title bar since it was first added but never actually
  wired to anything, and a `MenuItem`'s `Key` argument turned out to only label the shortcut for
  display, not register it. 182 tests across the solution now (178 pass by default).

  **Update, 2026-09-15**: a separate "File > Connect"/"Disconnect" menu item landed in both front
  ends (`TuiMode.ToggleConnectionAsync`/`MainWindow.ToggleConnectionAsync`) — closes or reopens the
  *same* session/transport, distinct from the Device Profiles menu above (no profile switching
  involved). Found and fixed a real bug in `Session` itself along the way: it created its read-loop
  `CancellationTokenSource` once, in the constructor, reused for the object's whole lifetime, but a
  CTS can only be cancelled once — so re-opening after a close silently never restarted the read
  loop. Now creates a fresh one per `OpenAsync`; see `CLAUDE.md`'s constraints list and the new
  `SessionTests.OpenAsync_AfterClose_RestartsTheReadLoopForRealIncomingData` (confirmed to fail
  without the fix). Sending while disconnected is guarded in both front ends. 187 tests across the
  solution now (183 pass by default).

  **Update, 2026-09-15**: WPF's connection editor is now a full, editable form (it previously only
  let you pick from existing profiles) — and, per explicit direction, its logic was factored out
  into a new shared `DevTerm.Configuration.ConnectionEditorViewModel` (validation, load/save/import/
  export, `RelayCommand`-based commands) rather than living in code-behind, so WPF's
  `DeviceProfilesWindow` is now real XAML `{Binding ...}`/`Command="{Binding ...}"` with no business
  logic of its own, and the TUI's `ConfigureMode` builds the *same* view model, syncing Terminal.Gui
  field values to/from it around each button press since Terminal.Gui has no data-binding system to
  do that automatically. Also landed: import/export a profile as a standalone JSON file in both
  front ends (`ConnectionProfileStore.ExportToFile`/`LoadFromFile`); WPF's startup flow now mirrors
  the TUI's — an invalid `CliOptions` opens the connection editor instead of showing an error and
  exiting (`App.xaml.cs` restructured the same way `Program.cs` was earlier today). Two real WPF
  gotchas found doing this: `{Binding ...}` doesn't populate a never-`Show()`n window's controls
  synchronously from a constructor-assigned `DataContext` (needs one dispatcher pump first), and
  `Window.DialogResult` throws unless the window was shown via `ShowDialog()` (relevant since tests
  drive the view model directly without ever showing the window) — see CLAUDE.md's constraints list.
  203 tests across the solution now (199 pass by default).

  **Update, 2026-09-15**: both remaining items above landed. `ManifestNameWarning.For(CliOptions)`
  (new, `DevTerm.Configuration`) calls the existing `DevTermUserDataPaths.ResolveManifestDirectory`
  resolution helper and returns a warning string when a set `ManifestName` doesn't resolve — wired
  into all three front ends (`CliMode`'s startup banner, `TuiMode.BuildWindow`'s initial output
  text, `MainWindow`'s constructor appending to `OutputList`), each showing the same message rather
  than each front end resolving/formatting it separately. Live mid-session profile *switching* also
  landed: new `DevTermSessionBuilder.Build(CliOptions)` composes a fresh `Session`+`IPresenter` pair
  through its own small throwaway `IServiceProvider` (the same `AddDevTermFrontEnd` wiring the app's
  real host uses, just built again for the new options — the running host has no API to
  re-register a transport into itself). `MainWindow.SwitchProfileAsync`/a new `SwitchProfileAsync`
  local function inside `TuiMode.BuildWindow` (exposed via `TuiWindowParts` for tests) both close +
  dispose the old session, swap in the new one, and reopen it — the "File > Device Profiles..." menu
  item now calls this instead of saving-as-default-and-asking-for-a-restart. Deliberately does *not*
  dispose the throwaway `IServiceProvider` itself (see `DevTermSessionBuilder.Result`'s own doc
  comment for why that's safe, not an oversight). Verified end-to-end against a real local TCP
  loopback socket in both front ends (`DevTerm.Wpf.Tests.MainWindowSwitchProfileTests`,
  `DevTerm.Console.Tests.TuiModeSwitchProfileTests`, both `INTEGRATION` — a real transport, not a
  `FakeTransport`, since the builder always composes a real one) — not yet verified against real
  hardware. 236 tests across the solution now (231 pass by default).

- **Test automation for CLI/TUI/WPF + test categorization**, landed 2026-09-15 — see
  docs/design/testing.md. Every test class now carries `[TestCategory("UNIT"|"INTEGRATION"|"DEV-LOCAL")]`
  (`dotnet test --filter "TestCategory=..."` runs a subset — matters more once a CI/CD pipeline
  exists, since it could run `UNIT`+`INTEGRATION` and skip `DEV-LOCAL` entirely). New:
  `DevTerm.Console.Tests.ConsoleAppCliTests` (`INTEGRATION` — spawns the real built console app
  against a real local TCP loopback socket); `DevTerm.Wpf.Tests` (new test project — `MainWindowTests`,
  `UNIT`, drives a real `MainWindow` via its testable `ConnectAsync`/`SendCurrentInputAsync` entry
  points against a `FakeTransport`); `RealHardwareCliTests`/`RealHardwareMainWindowTests`
  (`DEV-LOCAL` — opt-in via `devterm.runsettings` at the repo root, verified live against the real
  Tek 2230 over both `.107` and `.108`). Found and fixed two real WPF/async bugs building this (see
  CLAUDE.md's constraints list and docs/design/testing.md): a missing `DispatcherSynchronizationContext`
  sends `await` continuations to the wrong thread for real (not faked) async I/O; showing a
  `MainWindow` that's already been connected manually double-opens the session and corrupts the
  single-reader `PipeReader`. Also found real WPF cross-test parallelism flakiness, fixed with
  `[DoNotParallelize]` on the WPF test classes (confirmed stable across several repeated runs).
  172 tests across the solution now (4 more — the `DEV-LOCAL` ones — run and pass with `--settings devterm.runsettings` against the real device; they report Skipped/Inconclusive without it, not counted as failures).

  **Update, 2026-09-15**: Terminal.Gui (TUI) automation landed — `DevTerm.Console.Tests.TuiModeTests`
  (4 tests, `UNIT`) drives a real `TuiMode` window (split out via a new `TuiMode.BuildWindow`, the
  same seam WPF's `ConnectAsync`/`SendCurrentInputAsync` provide) using Terminal.Gui v2.5.0's own
  official `Terminal.Gui.Testing` API (`IInputInjector`, real screen-buffer readback via
  `IOutputBuffer`) — no OS-level UI Automation, no real terminal needed. Needed two different run
  modes (`DevTerm.Console.Tests.TuiTestRunner.RunHeadless`/`RunWithLoop`) because key injection and
  cross-thread `Application.Invoke` turned out not to work at the same time — see
  `docs/design/testing.md` and `CLAUDE.md`'s constraints list for both real gotchas found building
  this. 176 tests across the solution now (172 pass by default; the remaining 4 `DEV-LOCAL` ones need
  real hardware via `devterm.runsettings`, reporting Skipped without it).

  **Update, 2026-09-15**: `docs/user-guide/` landed for CLI and TUI — `cli.md` embeds real
  stdin/stdout transcripts from the built app, `tui.md` embeds real Terminal.Gui screen buffers via
  `TuiTestRunner.DumpBuffer()`, neither hand-typed. `docs/user-guide/wpf.md` is still a stub,
  deferred by choice: its screenshot generation just needs `RenderTargetBitmap` against
  `MainWindowTests`' existing real, laid-out `MainWindow`, not a new investigation like the TUI
  needed. Also not done: a `RealHardwareCliTests`-style test for the Tektronix TDS2024 now reachable
  at 192.168.0.110:23 (reserved as `RealTcpDeviceHost3` in `devterm.runsettings`) — it's SCPI-based
  and answers `*IDN?`, not the pre-SCPI `ID?` the existing 2230-specific assertion expects, so it
  needs its own test rather than a third `DataRow` on the existing one.

  **Update, 2026-09-15**: several Architect Notes items landed on the Connection Editor — serial
  fields (data bits, parity, stop bits, all now real dropdowns/`OptionSelector`s), a free-text
  Description field, Delete/Refresh commands, an overwrite-confirmation prompt on Save (via a new
  `ConnectionEditorViewModel.ConfirmOverwrite` hook, wired to a native dialog per front end), Load
  now sets "Save as profile named" to the loaded name, and WPF's saved-profiles list grows/shrinks
  with the window instead of a fixed height. Transport/Presenter/Line ending/Parity/Stop bits are
  now real pickers in both front ends (WPF `ComboBox`es bound via `{Binding}`; TUI
  `OptionSelector<TEnum>`, which required two TUI-only enums since it needs a real `enum` and the
  shared view model's `Transport`/`Presenter` are plain strings) — selecting a Transport shows only
  that transport's field group. `CliOptions` gained `[Category]`/`[DisplayName]` attributes
  documenting the same field groupings (metadata only, not yet consumed by the editor via
  reflection). New: [`docs/specs/`](docs/specs/README.md) — one spec per screen/flow
  (`connection-editor.md`, `tui-main-screen.md`, `wpf-main-window.md` all written), the
  precise field/action/state reference `docs/design/`/`docs/user-guide/` don't try to be. Also new:
  a `.claude/skills/docs-sync/` skill codifying the "update the spec/user-guide/changelog in the
  same change" workflow.

  **Update, 2026-09-15**: `docs/user-guide/` reorganized from one file per front end to one file per
  user flow (`connecting.md`, `managing-profiles.md`, `sending-and-receiving.md`,
  `connect-disconnect.md`), each showing every applicable front end's real screenshot side by side
  rather than splitting them across separate pages — `cli.md`/`tui.md`/`wpf.md`/the old
  `connection-editor.md` draft are gone, superseded. The WPF screenshot stub finally landed:
  `DevTerm.Wpf.Tests.ScreenshotTests`/`WpfScreenshot` renders a real `Window` to PNG via
  `RenderTargetBitmap` — confirmed the hard way that a `Window` only `Measure`d/`Arrange`d (never
  shown) renders completely blank, so it moves the window off-screen and calls a real `Show()`
  instead. The TUI also moved from plain-text buffer dumps to real PNGs:
  `DevTerm.Console.Tests.ScreenshotTests`/`TuiScreenshot` renders each cell's actual color via
  `System.Drawing.Common` — found and worked around a real headless-driver limitation doing this,
  where any cell still on the default color scheme reports `fg=(255,255,255) bg=(255,255,255)`
  (invisible white-on-white) since there's no real terminal behind headless mode to resolve an
  actual theme; only cells with a real highlight (menu bar, a focused field) report a usable color,
  so unresolved cells now render as plain black-on-white instead. Also found and fixed a real
  cross-test interference bug while building this: the Terminal.Gui headless key injector
  (`TuiTestRunner.TypeText`) degrades after enough `Application.Init`/`Shutdown` cycles in one
  process — a new screenshot test using it left a *later, different* test's own injected keystrokes
  unable to reach the focused field; fixed by setting the field's `.Text` directly for the
  screenshot instead of injecting keys it doesn't need to actually simulate. See `CLAUDE.md`.

  **Update, 2026-09-16**: two more Connection Editor Architect Notes items landed. Double-clicking a
  row in the saved-profiles list now loads it, same as pressing Load — WPF via a pure
  `<MouseBinding MouseAction="LeftDoubleClick" Command="{Binding LoadCommand}" />` (no code-behind),
  the TUI via `ListView`'s `Accepting` event (confirmed via reflection that a double-click maps to
  `Command.Accept`, not `Activate`/`OpenSelectedItem` as the v1-era names might suggest) calling the
  same local function the Load button's own handler does. Dirty-field confirmation also landed:
  `ConnectionEditorViewModel.IsDirty` flips true on any field edit (excluding transient state like
  `StatusMessage`) and clears on a successful Load/Save/Connect/Import; a new `ConfirmDiscardChanges`
  hook (same "left null, always proceeds" convention as `ConfirmOverwrite`) backs a `ConfirmClose()`
  check that both Close/Quit and Load now call before discarding unsaved edits — Connect doesn't
  need it, since connecting doesn't discard anything. Found and fixed a real self-inflicted test hang
  doing this: `ConfigureModeTests.Quit_SetsResultToNull` edited fields (incidentally, not for its own
  assertion) before clicking Quit, which now makes the view model dirty and triggers
  `ConfigureMode`'s real, blocking `Terminal.Gui.Views.MessageBox.Query` — nothing in headless test
  mode can click that dialog, so the run hung; fixed by not editing fields there and adding a
  dedicated test that stubs `ConfirmDiscardChanges` instead of hitting the real dialog (same
  convention `ConfirmOverwrite` already needed). 249 tests across the solution now (244 pass by
  default).

  **Update, 2026-09-16**: a real `FileSystemWatcher`-backed auto-refresh landed for the
  saved-profiles list. `ConnectionEditorViewModel` (now `IDisposable`) watches
  `ConnectionProfileStore.ProfilesDirectory` (new public property) and raises
  `ProfilesChangedExternally` on add/remove/rename; each front end marshals that onto its own UI
  thread and calls the same `RefreshCommand` the Refresh button does — the button itself stays as a
  manual fallback. Found and fixed a real crash doing this, not just a test artifact: the watcher
  fires on a background thread, and if that fires *after* `Application.Shutdown()` has already run
  (confirmed via a real crash: the window's own profiles directory being deleted by test cleanup
  after the window closed), `Application.Invoke` throws `NotInitializedException` uncaught on that
  background thread — fatal to the whole process, not just one test, since nothing was there to
  catch it. Fixed with a try/catch around the TUI's `Application.Invoke` call specifically for that
  exception; a regression test reproduces the exact sequence (build a window, shut down the
  Application, then trigger a real filesystem event) and confirmed stable across repeated runs. 258
  tests across the solution now (253 pass by default).

  **Update, 2026-09-16**: the TUI's two remaining "confirmed possible, not wired up" items both
  landed. A real "Browse..." button opens `Terminal.Gui.Views.OpenDialog` (`Application.Run(dialog)`,
  then reads `dialog.FilePaths` if not `Canceled`) next to the import/export path field, mirroring
  WPF's own `OpenFileDialog` — including its same limitation (an open-style picker, so a
  not-yet-existing export filename still needs hand-typing). The TUI's whole form now scrolls: every
  control moved into a new `formContent` container `View` with a real Terminal.Gui viewport/scrollbar
  (`SetContentSize` + `ViewportSettings |= AllowNegativeY | HasVerticalScrollBar` — confirmed via a
  real headless probe against the installed package that this actually scrolls, and separately that
  `View` has no built-in `Command.ScrollDown`/`PageDown` implementation to invoke instead, so
  PageUp/PageDown and the mouse wheel are wired by hand). PageUp/PageDown are bound on
  `Application.KeyDown` but deliberately skipped whenever the saved-profiles `ListView` has focus:
  checked directly that `ListView` already binds both those keys (and the arrow keys) for its own
  item navigation, so a naive global intercept would have stolen them from it entirely.

  Adding the Browse button surfaced a real layout bug, caught by actually looking at a captured
  screenshot rather than assumed to fit: with Browse crowded onto the path label's row alongside
  Import/Export, the row exceeded 80 columns and clipped Export's button text off the visible window
  entirely. Fixed by giving Browse/Import/Export their own row below the path field. New tests: a
  `PageDown` scrolling test (checks a control below the fold appears only after scrolling), a
  `BrowseButton` wiring smoke test (deliberately never clicks it — doing so opens a real modal
  dialog with nothing able to dismiss it headlessly), plus a new scrolled-state screenshot
  (`tui-configure-scrolled.png`). `docs/specs/connection-editor.md` updated: both "confirmed
  possible, not wired up" Open items are gone, replaced with per-front-end notes on how each Browse
  button/scroll mechanism actually works, and a narrower new Open item for the still-missing
  save-style export picker.

  **Update, 2026-09-16**: Serial port and HID Vendor/Product ID both got a "type it, or pick from
  what's actually attached" picker. `ConnectionEditorViewModel` gained `SerialPortOptions`
  (`ISerialPortDiscovery.GetPortNames()`, the same enumeration `--listports` uses) and
  `HidDeviceOptions` (`IHidDeviceDiscovery.GetDevices()`, same as `--listhiddevices`, formatted via
  the new `HidDeviceOption` record as `"{VID:X4}:{PID:X4}  {ProductName}"`), both captured once at
  construction and tolerant of discovery failing outright (empty list, not a construction failure —
  same reasoning as the profiles-folder `FileSystemWatcher`). New `SelectedSerialPort`/
  `SelectedHidDevice` properties copy a picked value into `Port`/`HidVendorId`+`HidProductId`
  respectively, deliberately separate from those fields themselves (typing stays simple, and a WPF
  editable combobox's `Text` doesn't share one format cleanly with a richer display string like
  `"046D:C08B  G502 HERO Gaming Mouse"`). WPF renders the picker as a second, non-editable
  `ComboBox` next to each field; the TUI adds a "Detect..." button that opens a small modal
  `Dialog`+`ListView` picker (`Application.Run(dialog)`, same nested-modal pattern as the Browse
  button's `OpenDialog` — Terminal.Gui has no built-in combobox widget). Decimal/hex display toggle
  and a long/short serial-port name are still open — see `docs/specs/connection-editor.md`.

  New tests: 9 in `ConnectionEditorViewModelTests` (discovery populating both lists, a failing
  discovery leaving the list empty rather than failing construction, both `Selected*` properties
  copying into the right field(s) and the dirty-tracking around that), 2 in
  `DeviceProfilesWindowTests` (the WPF comboboxes are genuinely bound to the right properties), 2 in
  `ConfigureModeTests` (the two Detect buttons exist and are wired — deliberately never clicked, same
  "native/nested dialogs are exercised structurally" convention as `BrowseButton`'s own test).

  **Update, 2026-09-16**: per direct feedback, "every `[TestMethod]` has a `[TestCategory]` of the
  correct type" is now a declared, *enforced* coding standard, not just an already-true convention.
  Enforcement couldn't be an `.editorconfig`/StyleCop rule (a missing or typo'd `[TestCategory]`
  isn't a compile-time concern — MSTest just silently excludes that test from
  `--filter TestCategory=...`, nothing else would ever catch it), so this is the first rule to reach
  for a real code-level check instead: a new `tests/DevTerm.CodingStandards.Tests` project reflects
  over every other test assembly (via `ProjectReference`, one per test project) and asserts every
  `[TestClass]` carries a `[TestCategory]` from `{UNIT, INTEGRATION, DEV-LOCAL}`, and separately that
  what MSTest actually resolves per test (class-level plus method-level combined) is never empty or
  unrecognized. Verified the checks actually catch a violation, not just pass vacuously: temporarily
  typo'd one class's category, confirmed both new tests failed with a clear message naming the exact
  class/method, then reverted. `docs/coding-standards.md` gained a Testing section documenting the
  rule; `CLAUDE.md`'s own Testing section now says this is enforced, not just conventional.

## Backlog / research

Not-yet-started work, prioritization notes, and early-stage research now live in
[`BACKLOG.md`](BACKLOG.md) — including the still-open Connection Editor items (decimal/hex toggle,
export-as-zip, multi-select Presenter, per-input-line parser selection, save-style export picker)
and a new "Window title, from the Architect" item.