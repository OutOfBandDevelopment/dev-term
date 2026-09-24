# TODO

Active / in-progress work for dev-term. Not-yet-started backlog and research work moved out to
[`BACKLOG.md`](BACKLOG.md) (2026-09-16) to keep this file to what's actually being worked on.
Completed work is logged by date under `docs/changes/`.

## In progress

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

- **SCPI instrument control module** (`DevTerm.Devices.Scpi`), landed 2026-09-23 — a data-driven
  profile mechanism for SCPI bench instruments rather than one hardcoded module per device, per
  `docs/design/proposals/scpi-instrument-control.md`. `ScpiInstrumentProfile`/
  `ScpiCommandDefinition`/`ScpiParameterDefinition` (JSON, `System.Text.Json`) declare a device's
  command set as data; `ScpiProfileCatalog` loads bundled `Profiles/*.json` plus a drop-in
  `ScpiProfiles/` folder next to the executable, so adding an instrument later needs a new JSON
  file, not a rebuild — six curated starter profiles ship: HP/Agilent/Keysight 34401A, Rigol
  DM3058E, Rigol DG1022/DG1022Z, Rigol DS1102E, Korad KA3005P, Korad KA6003P, plus a code-built
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
  **Real-hardware verification status** (see the later same-day entries in
  `docs/changes/2026-09-23.md`): the HP/Agilent/Keysight 34401A (RS-232 remote-mode root cause) and
  both Korad KA3005P/KA6003P (including a real load test) are now confirmed working end-to-end. The
  Rigol DM3058E/DG1022 curated profiles remain unconfirmed against real hardware. The Rigol
  DS1102E profile (renamed 2026-09-24 from the wrong-model DS1105E) is now confirmed against real
  hardware too — see `docs/changes/2026-09-24.md` and `docs/test/2026-09-24-09-54-39.md`. The
  Tektronix 2230 was originally out of scope for this module entirely (pre-SCPI, doesn't speak this
  protocol at all) but got a minimal one-command profile anyway — see
  `docs/design/proposals/tektronix-2230-protocol.md` — now confirmed live over TCP, as has a
  same-day `tektronix-tds2024.json` profile.

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
  manifest storage convention. The separate report of a Measure button beeping the 34401A with no
  reply shown anywhere in the UI is now fixed at the code level — confirmed root cause: the `scpi`
  presenter resolves fine from `PresenterCatalog` regardless of whether it was ever part of the
  session's actual `Pipeline` (fixed at session-build time from `CliOptions.EffectivePresenters`), so
  replies were never decoded/correlated. Fix, see `docs/changes/2026-09-23.md`: `Pipeline`/`Session`
  gained `AddPresenter`, mutating the session's live pipeline in place; both front ends' "SCPI
  Instrument..." menu handlers now bind the presenter in before opening the panel instead of just
  detecting/warning about the gap. Covered by new `UNIT` tests; **not yet re-verified against the
  physical 34401A** (no real-hardware access this session).

- **Real-hardware/real-usage bugs reported by the Architect (2026-09-23), not yet fixed.** Raw notes
  triaged into this file and `BACKLOG.md` the same day — design-level items (collapsible groups,
  per-field data-type/control-type metadata, an info icon showing the underlying command, Busylight
  custom-color UX, the Tektronix 2230 direction) moved to `BACKLOG.md`; these are concrete bugs
  against already-shipped screens:
  - **HP 34401A SCPI panel** (now on COM5): changing "Range" under "Configure" throwing an exception
    was fixed by the multi-parameter-field commit earlier today; "Configure DC Voltage Range doesn't
    appear to do anything" has a plausible fix (the profile's Range choice offered the illegal keyword
    "AUTO" instead of "DEF" — see `docs/changes/2026-09-23.md`) but is not yet re-verified against the
    real instrument.
  - **Device Profiles / Connection Editor**: selecting "BBL Lamp" under HID "detected devices" throws
    an out-of-index exception; a blank/`0`/unparsable HID Vendor or Product ID value isn't validated;
    switching profiles should disconnect the existing connection and only reconnect when "Connect" is
    pressed (not automatically); loading a saved profile should populate "Save as profile named" with
    that profile's name; "Save as profile named" is incorrectly cleared after "Save profile"; the
    detected-devices/detected-ports lists need a Refresh button.
  - **Busylight panel**: the custom-color dialog's values don't persist between openings (see also the
    related UX item moved to `BACKLOG.md`).
  - **Terminal screen (both front ends)**: the window title reportedly doesn't include the loaded
    profile name even though this was believed already landed (2026-09-18's "Architect's live window
    title" — needs re-checking, may be a regression); the send-line history (2026-09-23's Up/Down
    recall) adds a duplicate entry when the same line is sent twice in a row. **The File menu's
    Connect/Disconnect item's connection-state reflection, reported broken above, was re-checked by
    the user 2026-09-23 and now looks correct** — no code change was made for it in today's session,
    so this is a confirmed-by-re-check resolution, not a root-caused fix; flagged here in case it
    regresses.
  - **Device presenter "Custom Command" section** (SCPI and any device profile using the always-present
    custom-command escape hatch): pressing Enter in the "Command" field throws an exception; clicking
    "Send" with a value typed in "Command" also throws. **Likely fixed by the same-day
    `ScpiControlSurface.InvokeAsync` change** (recognizes the custom-command field's own id, same as a
    multi-parameter command's own field, and no-ops instead of throwing "Unknown SCPI command" — see
    `docs/changes/2026-09-23.md`), but this specific repro hasn't been re-run to confirm.
  - **Remaining real-hardware verification opportunity**: Korad KA3005P/KA6003P, the HP/Agilent/
    Keysight 34401A, and now the Rigol DS1102E are confirmed (see the SCPI module entry above); the
    Rigol DM3058E/DG1022 profiles are still unconfirmed. The newly-available Rigol bench units
    (DG1000Z, DG3000, DM3000, DS1000) are different specific models than these bundled profiles, so
    verifying likely means adding sibling profiles rather than confirming the existing ones
    unmodified — and, for any of them reachable only over USB rather than RS-232/LAN, is blocked on
    the USBTMC transport's own parked bulk-IN stall issue (see `BACKLOG.md`).

## Backlog / research

Not-yet-started work, prioritization notes, and early-stage research now live in
[`BACKLOG.md`](BACKLOG.md) — including the one remaining Connection Editor remnant
(serial-port descriptions on Linux/macOS) and the design-level items from the Architect's
2026-09-23 notes (see above).


## Notes from the Architect

We need a better way to ensure the selected device for USB (both USBHID and USBTMC) select the same hardware instance over and over.  It seems that some devices could share the same vendor id and product id so looking them just just by those two values can not ensure the corret device is selected.