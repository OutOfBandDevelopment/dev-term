# TODO

Active / in-progress work for dev-term. Completed work is logged by date under `docs/changes/`.

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

  **Still not built**: the warn-and-fall-back-to-default-presenter behavior for a `ManifestName`
  that doesn't resolve (the resolution helper exists, nothing calls it yet), and the WPF equivalent
  of the TUI's Configure screen (WPF still hard-fails on invalid config exactly like before — only
  its Device Profiles/Connect-Disconnect menu items landed this round).

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

## Backlog (not started)

Prioritized per direction given 2026-09-15: BLE serial is the next transport to build (ahead of
RFC 2217/UDP), since real target hardware exists. GPIB and USBTMC are newly-scoped, not yet
ordered against the rest.

- **BLE transport** (`DevTerm.Transports.Ble`), cross-platform by design via a pluggable per-OS
  adapter seam (Windows via `Windows.Devices.Bluetooth` first; Linux/BlueZ and macOS/CoreBluetooth
  addable later, including as community/self-contributed adapters) — see
  `docs/design/transports.md`'s BLE section for the adapter-contract shape and the "BLE Serial"
  (Nordic UART Service) pattern most hobbyist devices actually use. Target hardware identified:
  a [DER EE DE-5000 LCR meter](docs/design/proposals/de5000-lcr-meter-protocol.md), whose optical
  (IR) UART output is bridged to BLE via a custom adapter already built — unblocks that proposal
  once built. Still need: which GATT profile the custom adapter actually exposes (NUS or custom).
- **GPIB via Prologix-protocol controllers** — no new `ITransport` needed for the common case
  (cheap eBay adapters, and DIY [AR488](https://github.com/Twilight-Logic/AR488)-firmware boards,
  mostly speak the Prologix `++` ASCII command protocol over a plain serial or TCP connection);
  needs a thin controller layer (GPIB addressing, read-after-write/EOI) on top of the existing
  Serial/TCP transports, not a transport of its own. Explicitly **not** recommended: cheap "NI
  GPIB-USB-HS clone" adapters, which speak NI's proprietary non-serial USB protocol and have
  reported compatibility problems even against genuine NI hardware/drivers — see
  `docs/design/transports.md`'s Extensibility section. Real target hardware once built: the
  **Tektronix TDS2024** (has a GPIB option, currently fitted with a Centronics module instead) and
  the **HP 34401A** (already in the SCPI proposal's device table, GPIB/RS-232).
- **USBTMC transport** — the USB class most bench equipment (Rigol/Keysight/etc.) actually uses for
  local USB control; neither HID nor serial, needs its own raw-USB (WinUSB/LibUsbDotNet)
  implementation. Not yet designed in detail — see `docs/design/transports.md`'s Extensibility
  section. Real target hardware: the plain **Rigol DG1022** (no LAN option, unlike the
  DG1022Z/DG1062Z) and the **Rigol DS1102E** oscilloscope (confirmed to have USB, likely USBTMC for
  this era of Rigol scope but not yet confirmed for this specific unit).
- Declarative command/response schema for device control modules (send template + response
  pattern, `.ksy` reference for binary layouts via [Kaitai Struct](https://kaitai.io/), an SCPI
  baseline for common bench-instrument commands) — see the new section in
  `docs/design/device-control-modules.md`.
- RFC 2217 client (`Rfc2217Transport`, `ITransport`) — connect to a remote serial port (e.g.
  `ser2net`) with full baud/DTR/RTS control over the network. Design done: see
  `docs/design/rfc2217.md`. Build first (server mode depends on the same codec but is a
  differently-shaped bridge, not a transport — build second).
- RFC 2217 server (`Rfc2217ServerBridge`) — expose a local serial connection to the network for a
  remote RFC 2217 client to control. See `docs/design/rfc2217.md`. Note: binds loopback-only by
  default per the security note in that doc.
- UDP transport (target + listener modes). Real target hardware once built:
  [EByte E810-DTU(RS485)](docs/design/proposals/ebyte-e810-dtu-config-protocol.md)'s broadcast
  discovery/config protocol (port 1901) — note the proposal's own byte-count discrepancy needs
  resolving against a fresh capture before implementing, not just the existing notes.
- Dynamic plugin loading (`AssemblyLoadContext`, `IPluginModule`, manifest/versioning) per
  `docs/design/plugin-model.md`. Today's built-in transports/presenters are wired by hand in
  `Program.cs`, not actually loaded as plugins yet, despite already using the same contracts.
- Protocol decoders with a human-readable text baseline; composite/channelized decoders;
  mappable presenters.
- Rendering presenters (HPGL/PostScript/PCL, telemetry plots) + export (SVG/PNG/JPG).
- Device control modules (control surface + telemetry decode/plot) — see the declarative-schema
  item above for the command/response definition piece specifically.
  [SCPI instrument control](docs/design/proposals/scpi-instrument-control.md) is the recommended
  first target — textual, first real declarative-schema candidate, and needs no new transport for
  its RS-232/USB-CDC/LAN devices (USBTMC-only local-USB devices excepted — see above).
  [DE-5000 LCR meter](docs/design/proposals/de5000-lcr-meter-protocol.md) is gated on the BLE
  transport above (adapter hardware already built). [Radex One](docs/design/proposals/radex-one-protocol.md)'s
  transport dependency (USB HID) is now built, but it still needs its HID report-framing question
  resolved (see that proposal's open questions) before implementing the decoder.
  [Favero fencing protocol](docs/design/proposals/favero-fencing-protocol.md) is **deprioritized** —
  no hardware access to test against anymore; kept as a documented proposal only.
  A Tektronix-codes (pre-SCPI) decoder is also in scope — the project's own Tek 2230 test device
  (`ID?` → `ID TEK/2230,V81.1,VERS:14;`) is this "precursor protocol" family; worth its own proposal
  doc when picked up.
  Three more real, HID/serial-only (no new transport needed) targets, sourced from a local prior-art
  decoder library (`dotex/Incoming/BinaryDecoders`), each with a real-hardware-verified or
  cross-referenced protocol: [Kuando Busylight](docs/design/proposals/kuando-busylight-protocol.md)
  (its single-command report format is confirmed working live against real hardware; its
  batch-program format is not — see that proposal's open question), [Velleman K8055](docs/design/proposals/velleman-k8055-protocol.md)
  (already owned, simplest of the binary proposals), and [Zoom H4n remote](docs/design/proposals/zoom-h4n-remote-protocol.md)
  (plain serial via an already-built adapter cable, buildable today like SCPI).
- Resolve the stateful-presenter-vs-DI-singleton lifetime issue noted in
  `docs/design/presenters.md` before TUI/WPF support more than one concurrent session — today's
  single-session-per-process CLI usage doesn't hit it, but a multi-session front end would.

## Research (not backlog-ready)

- [BYTECC BT-UP01 USB-over-network bridge](docs/design/proposals/bytecc-bt-up01-usb-network-bridge.md) —
  **low priority for now**, by choice (2026-09-15): pursuing the "reverse-engineer it directly"
  route (network sniffer + decompiling the vendor client) rather than the boring-but-reliable
  Raspberry-Pi-running-`usbip` fallback, but only once a real capture exists — nothing to act on
  until then. Not a build item yet, unlike everything above: no protocol reverse-engineering has
  been done and
  none exists publicly. Two cheap checks needed before deciding whether this is even a
  reverse-engineering project at all: does the vendor's own client software just make the remote
  USB device appear local (making it a non-issue for dev-term entirely), and does the box happen to
  already speak the open USB/IP protocol. If it turns out to need real protocol work, it's a
  fundamentally bigger kind of thing than any transport/decoder proposal above — tunneling USB
  itself (enumeration, control/bulk/interrupt transfers), not decoding one device's byte protocol.
