# TODO

Active / in-progress work for dev-term. Not-yet-started backlog and research work moved out to
[`BACKLOG.md`](BACKLOG.md) (2026-09-16) to keep this file to what's actually being worked on.
Completed work is logged by date under `docs/changes/`.

## In progress

- **UI Definitions model** (`DevTerm.UiDefinitions`), landed 2026-09-15 — a framework-agnostic,
  JSON/XML-serializable model for declaring a device control panel once (`UiDefinition` →
  `UiSection`s → seven `UiControl` kinds: button/toggle/slider/numeric/choice/textField/indicator),
  so every front end can render it generically instead of hand-coding UI per device per front end.
  Built from real device mockups already written (Kuando Busylight, Velleman K8055, EByte, Zoom
  H4n), not designed in the abstract — see docs/design/ui-definitions.md. Polymorphic serialization
  uses the framework's own support (`System.Text.Json`'s `[JsonDerivedType]`, `XmlSerializer`'s
  `[XmlElement]` per derived type on the collection) rather than hand-rolled discriminator parsing.
  Round-trip tested against a full real panel (the Busylight mockup, reproduced as data).

- **Generic `UiDefinition`/`IControlSurface` renderer, proven against the real K8055**, landed
  2026-09-22 — `IControlSurface` (`DevTerm.Core.Control`) and `IStructuredPresenter`
  (`DevTerm.Core.Presenters`) now exist in code, and both front ends read any `UiDefinition`
  generically and produce real, wired controls: `ControlPanelMode` (TUI, one `FrameView` per
  section, kind→Terminal.Gui-widget mapping — `Button`/`CheckBox`/bounded `TextField` for
  slider+numeric/`OptionSelector`/read-only `Label`, since Terminal.Gui 2.5.0 has no native
  slider/RadioGroup/ComboBox) and `ControlPanelWindow` (WPF, one `GroupBox` per section,
  `Button`/`CheckBox`/real `Slider`/`TextBox`/`RadioButton`s-or-`ComboBox`/read-only `TextBlock`).
  Both resolve the active presenter and subscribe to `IStructuredPresenter.ValuesChanged` for live
  indicator updates when one exists, degrading to static default values (not a failure to open) when
  it doesn't. First concrete device module: `DevTerm.Devices.K8055` (`K8055UiDefinition`,
  `K8055ControlSurface`, `K8055Decoder`), registered as an ordinary presenter (`--presenter k8055`)
  and opened via a new `_Device`/`Device` menu item in each front end, reusing the current session
  rather than opening a second HID connection. `UiControl.Id` is the command id 1:1
  (`ButtonControl.CommandId` overrides it), settling ui-definitions.md's open question. 19 new
  `UNIT` tests (`ControlPanelModeTests`, `ControlPanelWindowTests`) plus 14 in
  `DevTerm.Devices.K8055.Tests` — see `docs/changes/2026-09-22.md`. **Renderer is generic, not
  K8055-specific** — the next real target to prove that against a second device is Busylight (a
  separate future pass); K8055's own mockup only exercises toggle/slider/indicator/button, so
  numeric/choice/textField have render/unit coverage but no real-hardware exercise yet.
  Real-hardware verification against WPF's `ControlPanelWindow` is done, same day
  (`docs/changes/2026-09-22.md`): live indicators matched an independent CLI reading, and digital
  out/analog out/counter reset all round-tripped correctly against the physical board (an apparent
  counter-reset anomaly was root-caused to a real, physically-explainable floating-pin behavior, not
  a bug). Still pending: the same pass against the TUI (`ControlPanelMode`), and the digital-input
  bit-to-channel mapping is still unconfirmed (shipped as a raw `digitalInRaw` byte rather than 5
  guessed channel keys) — both require the user's own physical rewiring/hands-on time, deferred for
  now — see `docs/design/proposals/velleman-k8055-protocol.md`'s open questions.

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

- **Connection Editor, from the 2026-09-15 Architect Notes.** Landing incrementally since
  2026-09-15 — full detail on each increment is in `docs/changes/2026-09-15.md`/
  `docs/changes/2026-09-16.md`, not repeated here. Landed so far: profile save/load/delete/import/
  export (`ConnectionProfileStore`) with the TUI-as-default-mode flip; a real Configure screen in
  the TUI and a "File > Device Profiles..." menu in both front ends, sharing one
  `ConnectionEditorViewModel` (WPF binds to it directly via XAML, no code-behind business logic;
  the TUI copies values to/from it around each button press); a "File > Connect"/"Disconnect" menu
  item (surfaced and fixed a real `Session` read-loop bug along the way); live mid-session profile
  switching (no restart needed) plus a manifest-not-found warning; serial-field dropdowns,
  Delete/Refresh, an overwrite-confirmation prompt, and `docs/specs/` as a new precise-reference doc
  kind; double-click-to-load and a dirty-field discard confirmation; a real profiles-folder
  `FileSystemWatcher` for saved-list auto-refresh; a TUI file picker (Browse...) and a scrollable
  TUI form; "type it or pick from what's attached" pickers for the serial port and HID
  vendor/product ID; a decimal/hex display toggle for HID Vendor/Product ID (a separate `*Display`
  property per field, so the canonical value stays decimal regardless of what's currently shown); a
  "Save As..." button (a real `SaveDialog`/`SaveFileDialog`) next to Browse, so exporting to a
  brand-new filename no longer means hand-typing it; and, most recently, multi-select in the
  saved-profiles list (WPF `ListBox.SelectionMode="Extended"`, the TUI `ListView`'s own
  `MarkMultiple`/`ShowMarks`) plus Export Selected/Export All (a zip, one `{name}.json` per profile)
  and zip-aware Import with per-name Replace/Rename/Skip conflict resolution; and, most recently
  (2026-09-18), a multi-select Presenter picker plus a per-line send format ("parser") —
  `CliOptions.Presenter` is now a list (checkboxes in the editor, `--presenter ascii,hex`), a separate
  `Parser` names the encoder for typed lines, and the TUI ("Send as" menu)/WPF ("Send as:" box) can
  switch it per line. Test automation for
  CLI/TUI/WPF (including Terminal.Gui's own headless testing API) and a `[TestCategory]` coding
  standard, both prerequisites for landing the above with confidence, are also done — see
  `docs/design/testing.md`/`docs/coding-standards.md`.

  Bulk profile removal (a "Delete Selected" button, with a native confirmation naming the profiles)
  and the Architect's live window title (the saved profile's name, else a `tcp://…`/`serial://…`/
  `hid://…` connection string, re-evaluated on every profile switch) landed the same day, as did a
  "Replace All" zip import (delete every saved profile, then import the zip — read and validated
  first, confirmed second), and so did the long/short name for a detected serial port on Windows
  (`COM4 — Prolific USB-to-Serial Comm Port`, from the Plug-and-Play registry; seen returned for a
  live adapter) and a live filter on the HID picker (a non-zero Vendor/Product ID narrows the
  detected-devices list; 0 means any). A real bug was also fixed: double-clicking a saved profile in
  the WPF editor never loaded it (the list's `MouseBinding` never saw the second click; now a per-row
  `MouseDoubleClick`), and a device `TimeoutException` is now reported like any connection failure
  instead of escaping every open/close/switch `catch` (a crash on a device timeout was reported but
  not reproduced against the K8055). Two more real bugs were fixed after being reported: the TUI
  editor's fields couldn't take focus or input at all (broken since the form was made scrollable;
  now focusable, and Tab scrolls a below-the-fold control into view), and closing the WPF window
  threw "...while a Window is closing" (a synchronous-continuation reentrancy in `OnClosing`). A
  third, reported 2026-09-18 and fixed 2026-09-22: switching TUI profiles after a failed attempt
  could silently revert a just-succeeded one (a stale, superseded connect resolving late and
  clobbering the UI) — see `docs/changes/2026-09-22.md`, which also covers a live (not
  automated-test) investigation of a reported WPF connection-error crash that didn't reproduce.
  Nothing functional is left open on the Connection Editor; what
  remains (see [`BACKLOG.md`](BACKLOG.md) and `docs/changes/2026-09-18.md`) is serial-port
  descriptions on Linux/macOS (low priority, explicitly deferred by the user — not needed soon) and
  the double-click fix / timeout hardening, which still haven't been confirmed by hand. The
  presenter-picker/`--parser` `DEV-LOCAL` real-hardware tests *have* now been run (2026-09-22, see
  `docs/changes/2026-09-22.md`) against the two real TCP devices actually available
  (192.168.0.108, 192.168.0.110) — both passed everywhere they're exercised; the third configured
  host (192.168.0.107) wasn't reachable and its `DataRow`s failed as expected, not a regression.

- **`LoopbackTransport` test helper**, added 2026-09-22 — a scripted, deterministic in-process
  `ITransport` (`DevTerm.Console.Tests`) for exercising `Session`/`TuiMode`/`MainWindow` logic
  against realistic request/response and multi-line "event stream" behavior without a real device or
  even a real socket, filling the gap between the dumb `FakeTransport` (manual push/record only) and
  a real `TcpListener` loopback (`INTEGRATION`-tier, and overkill when the network stream itself
  isn't what's under test). See `docs/design/testing.md`'s "Scripted responses without a real
  device" section and `docs/changes/2026-09-22.md`. Not yet used by any TUI/WPF-level test — it's a
  building block, added because real devices aren't always available to test against by hand.

- **Production `loopback` transport**, added 2026-09-22 — promotes the same scripted-fake-device
  idea from the `LoopbackTransport` test helper above into a real, user-selectable transport
  (`DevTerm.Transports.Loopback`) so someone without any hardware attached can pick "loopback" in
  the TUI Configure screen or WPF Device Profiles/Connection Editor and get a working, zero-
  configuration fake device to exercise the UI end-to-end. Deliberately a separate project, not a
  reuse of the test-only prototype in `tests/DevTerm.Console.Tests/` (which stays exactly as-is —
  see `docs/design/testing.md`); this one is wired through the same DI/validation/description path
  every other transport uses (`AddDevTermFrontEnd`, `CliOptionsValidator`, `ConnectionDescription`)
  and both front ends' transport pickers. Same three example commands as the test helper (`hello`,
  `Send Stream: N, ascii`, `Send Events: N`), plus a fourth added the same day: `help`/`?`, which
  prints the command list (`LoopbackScript.HelpLines`); all matching is case-insensitive. No
  user-configurable custom script yet. See `docs/design/transports.md`'s "Loopback" section and
  `docs/changes/2026-09-22.md`.

- **Global unhandled-exception handling**, added 2026-09-22 — an unexpected exception (one that
  slips past every existing, deliberate `ConnectionErrorMessages.IsConnectionFailure` catch) no
  longer takes the whole app down with it. `DevTerm.Wpf`'s `App.xaml.cs` hooks
  `Application.DispatcherUnhandledException` (reports via `MessageBox`, sets `e.Handled = true`) and
  `TaskScheduler.UnobservedTaskException` (a faulted, never-awaited background `Task`; marshaled to
  the UI thread via `Dispatcher.BeginInvoke` since it fires on the finalizer thread). `DevTerm.Console`
  has no `Dispatcher` to intercept a synchronous exception the same way, so its two front ends split
  the equivalent behavior: `Program.cs` hooks `TaskScheduler.UnobservedTaskException` process-wide
  (reports to stderr), and `TuiMode.RunAsync` passes an `errorHandler` to `Application.Run` (a real
  Terminal.Gui v2.5.0 API — reports via `MessageBox.ErrorQuery` and returns `true` to resume the main
  loop instead of exiting). Per Terminal.Gui's own doc comment, that `errorHandler` only takes effect
  in RELEASE builds — a DEBUG build still rethrows so a debugger can break on the original exception.
  `CliMode` needed no change beyond the process-wide `UnobservedTaskException` hook: it already
  catches the same known failure modes per line (`ConnectionErrorMessages.IsConnectionFailure`,
  `TimeoutException`) the other front ends do, and letting anything past that crash with a non-zero
  exit code is the right behavior for a scriptable/automatable mode — swallowing an unanticipated
  exception there would hide a real bug from whatever's driving it via a script/CI pipeline. See
  `docs/changes/2026-09-22.md`.

## Backlog / research

Not-yet-started work, prioritization notes, and early-stage research now live in
[`BACKLOG.md`](BACKLOG.md) — including the one remaining Connection Editor remnant
(serial-port descriptions on Linux/macOS).

