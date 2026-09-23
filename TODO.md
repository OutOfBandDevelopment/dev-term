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
  `DevTerm.Devices.K8055.Tests` — see `docs/changes/2026-09-22.md`.
  Real-hardware verification against WPF's `ControlPanelWindow` is done, same day
  (`docs/changes/2026-09-22.md`): live indicators matched an independent CLI reading, and digital
  out/analog out/counter reset all round-tripped correctly against the physical board (an apparent
  counter-reset anomaly was root-caused to a real, physically-explainable floating-pin behavior, not
  a bug). Still pending: the same pass against the TUI (`ControlPanelMode`), and the digital-input
  bit-to-channel mapping is still unconfirmed (shipped as a raw `digitalInRaw` byte rather than 5
  guessed channel keys) — both require the user's own physical rewiring/hands-on time, deferred for
  now — see `docs/design/proposals/velleman-k8055-protocol.md`'s open questions.
  **Renderer's genericness now proven against a second device, same day**: `DevTerm.Devices.Busylight`
  (`BusylightUiDefinition`, `BusylightControlSurface`, `BusylightDecoder`) needed zero renderer
  changes in either front end — only a new device module plus the same one-line
  `AddDevTermFrontEnd`/menu-item wiring K8055 used. Registered as `--presenter busylight`, opened via
  a new "Busylight Control Panel..." item alongside K8055's. Unlike K8055, every control except
  "apply" only mutates in-memory state (color/blink timing/mute/track/volume); "apply" is the one
  command that actually sends the confirmed-working 9-byte single-command frame, matching the
  mockup's explicit `[Apply]` button. No `IndicatorControl`s and hence no `IStructuredPresenter` —
  `BusylightDecoder` just renders the device's ASCII poll-reply as text. 12 new `UNIT` tests
  (`DevTerm.Devices.Busylight.Tests`). **Not yet verified against a real physical Busylight** — this
  landed as a software-only pass; the user will do the physical hardware review later, the same way
  the K8055's remaining digital-in/TUI checklist items are deferred to them.

- **Live user feedback on the K8055/Busylight panels, addressed same day (2026-09-22)** — after the
  above landed, real hands-on use of the K8055 and Busylight panels surfaced five real issues, all
  fixed the same day (see `docs/changes/2026-09-22.md` for the full write-up):
  - **K8055 digital inputs never read as anything but 0** — `K8055Decoder` read `digitalInRaw` from
    byte 0 of the 9-byte input frame, but byte 0 is the same leading HID report-ID byte confirmed
    elsewhere in this codebase (always `0x00`, never real device data), so it could never have
    carried digital-input state. Fixed to read byte 1 instead. Code-inspection fix, not yet
    reconfirmed against real hardware.
  - **Busylight's "Custom..." button did nothing** — added `ButtonControl.ColorPickerTargetCommandId`,
    a new generic, device-agnostic field on the UI-definitions model (any device's button can declare
    "open a modal RGB/HSV color picker; on confirm, send `\"r,g,b\"` to this command id" without the
    renderer hardcoding Busylight-specific ids). Implemented in both front ends: WPF `ColorPickerWindow`
    (sliders + hex box for RGB, sliders for HSV) and the TUI's `ControlPanelMode.PickColor` (a nested
    `Dialog` with text fields for both). `BusylightControlSurface.SetColor` now accepts either a named
    preset or an `"r,g,b"` triple.
  - **Busylight's "Program Sequence..." button did nothing** — already a confirmed, real-hardware-
    tested no-op (see `docs/design/proposals/kuando-busylight-protocol.md`'s open questions); removed
    from `BusylightUiDefinition` rather than investigated further, since a button with no effect on
    real hardware is worse than no button. `programSequence` is still accepted as a no-op command for
    backward compatibility.
  - Also brought the TUI's output pane in line with the WPF main window's existing capped-output rule
    (`MainWindow.MaxOutputLines`, 1000): the TUI's `TuiMode.AppendOutput` grew an unbounded
    concatenated string forever, and WPF itself had a handful of raw `OutputList.Items.Add` call sites
    that bypassed its own cap. Both fixed: WPF now routes every output line through the capped
    `AppendOutput` helper, and TUI gained the same bounded-buffer rule at a shorter cap (300, not
    1000) since its output pane rebuilds one `TextView.Text` string on every trim rather than using a
    virtualized items list.
  - **K8055 live indicators re-rendered on every streamed report, even unchanged ones** — the board
    streams its input report continuously and unprompted, and most consecutive frames repeat the same
    reading, but `K8055Decoder.ValuesChanged` fired the full 5-key dictionary on every frame
    regardless ("runaway data streaming"). Fixed by tracking the last published value per key and only
    including (and only raising the event for) keys that actually changed since the previous frame;
    both control panels already update indicators by per-key lookup, so a partial dictionary needed no
    consumer-side change.
  - **Control-panel indicators never updated when the device was selected via the Device Profiles/
    Configure Connection GUI, only via `--presenter` on the command line** — reported as "I can now
    set the outputs but the input values dont capture the data." `ConnectionEditorViewModel.PresenterOptions`
    (the checkbox list backing both front ends' presenter picker) was hardcoded to the six built-in
    text presenters and never included `k8055`/`busylight`, so picking a device through the editor
    could get the transport right but had no way to also enable its decoder — outbound commands still
    worked (they write to the session directly), but the decoder was never wired into the session's
    `Pipeline` and its indicators never received data. Fixed by adding both names to
    `PresenterOptions`.

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

- **SCPI instrument control module** (`DevTerm.Devices.Scpi`), landed 2026-09-23 — a data-driven
  profile mechanism for SCPI bench instruments rather than one hardcoded module per device, per
  `docs/design/proposals/scpi-instrument-control.md`. `ScpiInstrumentProfile`/
  `ScpiCommandDefinition`/`ScpiParameterDefinition` (JSON, `System.Text.Json`) declare a device's
  command set as data; `ScpiProfileCatalog` loads bundled `Profiles/*.json` plus a drop-in
  `ScpiProfiles/` folder next to the executable, so adding an instrument later needs a new JSON
  file, not a rebuild — six curated starter profiles ship: HP/Agilent/Keysight 34401A, Rigol
  DM3058E, Rigol DG1022/DG1022Z, Rigol DS1105E, Korad KA3005P, Korad KA6003P, plus a code-built
  Generic fallback (`*IDN?`/`*RST`/`*CLS`/`*OPC?`). `ScpiControlSurface` does `{Name}`-token
  template substitution, numeric clamping, and a `sendCustom` verbatim passthrough escape hatch.
  `ScpiReplyPresenter` (`IPresenter`/`IStructuredPresenter`/`IScpiReplyTracker`) line-buffers ASCII
  and FIFO-correlates each query to its reply via `QuerySent`, resolving
  device-control-modules.md's "expected reply pattern" open question for the plain synchronous
  case. `ScpiUiDefinitionBuilder` maps a profile onto the existing generic `UiDefinition` model
  (one section per `Category`, a button for a 0-parameter command, a button+`IndicatorControl` for
  a query, parameter fields + a button for an N-parameter command) reusing the same
  `ControlPanelMode`/`ControlPanelWindow` renderers proven against K8055/Busylight — needing one
  small, generic, non-SCPI-specific addition to the shared model,
  `ButtonControl.ParameterFieldIds` (a button that reads named sibling fields' current values,
  joins them with `,`, and invokes `CommandId ?? Id` with the result). Both front ends get a
  generic "SCPI Instrument..." menu item (not one per device, unlike K8055/Busylight) opening a
  small profile picker (`Auto-detect (*IDN?)` / `Generic (manual)` / one of the six named
  profiles); auto-detect sends `*IDN?` and regex-matches the reply against each profile's
  `IdnPattern` (`ScpiProfileCatalog.TryMatchByIdn`), honestly scoped as an identification shortcut
  since no universal "list supported commands" SCPI query exists. 37 new `UNIT` tests
  (`DevTerm.Devices.Scpi.Tests`) plus `ParameterFieldIds` cases added to both existing
  `ControlPanelModeTests`/`ControlPanelWindowTests`, and `"scpi"` added to
  `ConnectionEditorViewModel.PresenterOptions` (and both its checkbox-list test assertions) up
  front, avoiding the exact `k8055`/`busylight` omission bug fixed earlier the same week.
  **Not yet verified against any real hardware** — none of the six curated command sets has been
  confirmed against an actual instrument; only a code-review/unit-test pass so far. The Tektronix
  2230 remains explicitly out of scope (pre-SCPI, doesn't speak this protocol at all) — see the new
  `docs/design/proposals/tektronix-2230-protocol.md` for what's known and what real-hardware
  probing it still needs.

- **SCPI follow-ups from real-hardware use against a physical HP/Agilent/Keysight 34401A**, landed
  2026-09-23 — driving the 34401A over real RS-232 surfaced correct settings (9600/8/2/None,
  **no hardware handshake**, **LF** terminator) and a real root cause for "device beeps, no
  measurement shown": RS-232 on this instrument never auto-enters remote mode the way GPIB does, so
  `SYSTem:REMote` must be sent before any query or the instrument answers with SCPI error `+550
  "Command not allowed in local"`. All four queued follow-ups are done — see
  `docs/changes/2026-09-23.md`: a `Notes` field on `ScpiInstrumentProfile` (rendered via
  `UiDefinition.Description` in both `ControlPanelWindow`/`ControlPanelMode`); `CliOptions.Handshake`
  exposed in both configuration UIs (a Handshake row after Stop bits in `DeviceProfilesWindow`/
  `ConfigureMode`); a new `CliOptions.ScpiProfile` persists the chosen instrument-profile/auto-detect
  choice onto a saved connection, so both front ends' "SCPI Instrument..." picker is skipped when it
  still resolves to a real choice (also exposed as its own editor row, shown only when the `scpi`
  presenter is selected); and `ScpiProfileCatalog` now also loads from a per-user
  `~/.dev-term/scpi-profiles` folder, mirroring the existing per-user connection-profile/device-
  manifest storage convention. **Still open, not yet root-caused**: `docs/changes/2026-09-23.md`'s
  separate report of a Measure button beeping the 34401A with no reply shown anywhere in the UI —
  suspected to be the `scpi` presenter not actually being active in the session's `Pipeline` for
  that connection, not yet confirmed or fixed.

- **`CliOptions.ExportDirectory`**, landed 2026-09-23 — a configurable destination for
  not-yet-built auto-saved captures (see the Stream Monitor proposal in `BACKLOG.md`), defaulting to
  `~/.dev-term/exports` (`DevTermUserDataPaths.ExportsDirectory`) via `EffectiveExportDirectory`.
  Bound through the same `DevTermConfiguration` command-line/environment-variable/settings-file
  layering every other `CliOptions` property already gets — no special-case binding code needed for
  a plain nullable string, unlike the `Presenter` array property. No editor UI row yet (same as the
  existing, also-editorless `ManifestName`) — this is prep for the Stream Monitor feature itself,
  not a user-facing setting on its own yet.

- **Send-line history recall (Up/Down)**, landed 2026-09-23 — a new, shared `SendHistory`
  (`DevTerm.Configuration`), a bounded (100-entry), most-recent-first, in-memory-only history of
  lines sent from the live session screen's send field, so pressing Up recalls the last line sent
  (and repeatedly, older ones), Down steps back toward the newest, matching ordinary shell-history
  semantics. WPF's `MainWindow.SendBox` changed from a plain `TextBox` to an editable `ComboBox`
  (`ItemsSource` bound directly to `SendHistory.Items` for a live drop-down), with Enter/Up/Down
  handled via `PreviewKeyDown` rather than the bubbling `KeyDown` (the same "don't trust unverified
  native widget key routing" precedent as Ctrl+Q), pulled into a directly-testable
  `HandleSendBoxKey(Key)` method rather than only reachable through the routed event. Terminal.Gui
  2.5.0 has no combo box, so the TUI's `sendField` stays a plain `TextField`; its existing `KeyDown`
  handler gained `Key.CursorUp`/`Key.CursorDown` branches doing the same recall, with no visible
  drop-down. Both front ends record a line via `SendHistory.Add` at the same point they already
  clear the field on Enter/Send, regardless of whether the send itself succeeds. Not persisted
  across restarts — in-memory for the life of the process only. 17 new `UNIT` tests: 12 in
  `DevTerm.Configuration.Tests` (`SendHistoryTests`) covering bounding/trimming/cursor semantics, 3
  in `DevTerm.Wpf.Tests` (`MainWindowTests`), 2 in `DevTerm.Console.Tests` (`TuiModeTests`) — see
  `docs/changes/2026-09-23.md`.

## Backlog / research

Not-yet-started work, prioritization notes, and early-stage research now live in
[`BACKLOG.md`](BACKLOG.md) — including the one remaining Connection Editor remnant
(serial-port descriptions on Linux/macOS).

