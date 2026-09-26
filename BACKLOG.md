# Backlog

Not-yet-started work for dev-term, prioritized where a priority has actually been given, plus
early-stage research not ready to be scoped as backlog. Active/in-progress work lives in
[`TODO.md`](TODO.md) instead, kept lean on purpose — this file is where lower-priority and
not-yet-started work lives so it doesn't add scroll-past overhead to checking on active work.
Completed work is logged by date under `docs/changes/`.

## Backlog (not started)

Prioritized per direction given 2026-09-15: BLE serial is the next transport to build (ahead of
RFC 2217/UDP), since real target hardware exists. USBTMC is newly-scoped, not yet ordered against
the rest.

### Transports

- **BLE transport landed 2026-09-25** (`DevTerm.Transports.Ble` + Windows backend
  `DevTerm.Transports.Ble.Windows`, wired through `DevTerm.Configuration`/CLI/TUI/WPF) — see
  `docs/design/transports.md`'s BLE section and `docs/changes/2026-09-25.md`. Unverified against
  real hardware yet (see `TODO.md`). Still open, not gated on any further transport work:
  - Linux (BlueZ/D-Bus) and macOS (CoreBluetooth) backends — the adapter seam supports adding
    either independently; neither has been started.
  - **Live BLE device picker.** Both front ends only take a typed device id today (copied by hand
    from a `--listbledevices` run) — unlike HID/USBTMC's "Detect..." pickers, neither the TUI nor
    WPF has one for BLE yet (promised by `ConnectionEditorViewModel.BleDeviceId`'s doc comment).
  - Which GATT profile the DE-5000's custom IR-to-BLE adapter actually exposes (NUS or custom) is
    still unconfirmed — needed before `DevTerm.Devices.De5000` (landed 2026-09-25, see "Device
    control modules & hardware profiles" below) can be verified against real hardware.
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

### Plugin architecture, decoders & presenters

- Dynamic plugin loading (`AssemblyLoadContext`, `IPluginModule`, manifest/versioning) per
  `docs/design/plugin-model.md`. Today's built-in transports/presenters are wired by hand in
  `Program.cs`, not actually loaded as plugins yet, despite already using the same contracts.
- Protocol decoders with a human-readable text baseline; composite/channelized decoders;
  mappable presenters.
- Rendering presenters (HPGL/PostScript/PCL, telemetry plots) + export (SVG/PNG/JPG). A concrete
  consumer of this now has its own design:
  [stream content detection & rendering window](docs/design/proposals/stream-content-detection.md)
  (2026-09-23) — an optional "Stream Monitor..." window that recognizes HPGL/PostScript/PCL/binary-
  image replies (via a declared per-command response-format hint or by sniffing known signatures)
  and captures/exports them, phased so a graphics-free capture-and-auto-save capability (TUI: save
  as `{device}_{timestamp}.{ext}`; WPF: same, plus a free live preview for image formats WPF can
  already decode natively) ships ahead of the harder HPGL/PostScript/PCL rendering work above.

### Device control modules & hardware profiles

- Declarative command/response schema for device control modules (send template + response
  pattern, `.ksy` reference for binary layouts via [Kaitai Struct](https://kaitai.io/), an SCPI
  baseline for common bench-instrument commands) — see the new section in
  `docs/design/device-control-modules.md`.
  **Still open:**
  - [DE-5000 LCR meter](docs/design/proposals/de5000-lcr-meter-protocol.md) landed 2026-09-25
  (`DevTerm.Devices.De5000`, see `docs/changes/2026-09-25.md`) but is unverified against real
  hardware — the custom IR-to-BLE adapter's GATT profile (NUS or custom) is still unconfirmed. Run
  `RealHardwareDe5000Tests` once the adapter is on the bench and `devterm.runsettings` has its
  device id/UUIDs filled in.
  - [Radex One](docs/design/proposals/radex-one-protocol.md) landed 2026-09-25 (`DevTerm.Devices.RadexOne`,
  see `docs/changes/2026-09-25.md`) but is unverified against real hardware — no device was found
  attached during a live enumeration pass. Re-run `RealHardwareRadexOneTests` once the device is
  available and `devterm.runsettings` has its VendorId/ProductId filled in (see `TODO.md`).
  - [Zoom H4n remote](docs/design/proposals/zoom-h4n-remote-protocol.md) landed 2026-09-25
  (`DevTerm.Devices.ZoomH4n`, see `docs/changes/2026-09-25.md`) but is unverified against real
  hardware — no `h4n2rs485` adapter was attached. Re-run `RealHardwareZoomH4nTests` once it's
  available and `devterm.runsettings` has its `RealSerialZoomH4nPort` filled in (see `TODO.md`).

### Tektronix TDS2024

- **Every `TRIGger:...?` query hangs (never replies) against this specific real TDS2024 unit** —
  real-hardware confirmed 2026-09-25 (`docs/test/2026-09-25-18-57-22.md`): `TRIGger:MAIn:FREQuency?`
  and `TRIGger:STATE?` (a much cheaper status query, ruling out "expensive measurement" as the
  cause) both hung the full step timeout, while every non-`TRIGger` query tried (`*IDN?`, `CH1?`,
  `CH2?`) answered normally, including as the 3rd command in a sequence (ruling out a simple
  "3rd command" positional issue). `tektronix-tds2024.json`'s own `Name` field notes this unit is
  specifically "NOT the TDS2024B" — unconfirmed hypothesis that the `TRIGger` query family needs
  that variant's firmware. `RealHardwareTcpTests`'s TDS2024 test avoids the whole `TRIGger` family
  for now (uses `CH1?`/`CH2?` instead). Not investigated further — needs a packet capture of a
  known-working `TRIGger` query (e.g. from a Tek-provided tool) against this exact unit to compare
  framing, similar to the USBTMC framing bugs below.

### USBTMC

- **DS1102E missing-ZLP at an exact packet boundary (pyvisa-py #472, not reproduced)** — pyvisa-py reports that the
  device omits the terminating zero-length packet when a reply ends exactly on a 64-byte boundary. The rework would
  wait one `ReadTimeoutMs` for it and then raise an error. A normal-mode 600-sample `:WAV:DATA?` (610 bytes plus 10
  padding) never hits a boundary, so this needs a reply that does (a long-memory/RAW-mode read, for example) to check.
  The same issue's other claim ("TransferSize is 10 bytes short") did **not** match this unit: TransferSize was exact and
  the 10 extra bytes were trailing padding, which the rework correctly drops (see the 2026-09-25 bench report).

### Connection Editor

**Connection Editor, from the 2026-09-15 Architect Notes** (see `TODO.md`'s "In progress" entry
for a summary of what already landed, and `docs/changes/2026-09-15.md`/`2026-09-16.md` for full
detail on each increment). Still open — prioritized per direction given 2026-09-16, with the
serial-port naming item moved to lower priority. ~~Export-selected/export-all as a zip~~ landed
2026-09-16: multi-select in the profiles list (WPF `ListBox.SelectionMode="Extended"`, TUI
`ListView.MarkMultiple`/`ShowMarks`), Export Selected/Export All (one `{name}.json` per profile in
a zip), and zip-aware Import with per-name Replace/Rename/Skip conflict resolution
(`ConnectionEditorViewModel.ResolveZipImportConflict`) — see `docs/changes/2026-09-16.md` and
`docs/specs/connection-editor.md`. Its two follow-ups (bulk profile removal and a wholesale "delete
all, then import" option) both landed 2026-09-18, as did the Windows half of a long/short name for
detected serial ports; the Linux/macOS half and the WPF "not found" hint landed 2026-09-25
(`docs/changes/2026-09-25.md`). Nothing from those notes is still open.

### Device manifests & shared UI framework

- **Wire a loaded `DeviceManifest` to an actual live `IControlSurface`/decoder pair and connection** —
  `device-manifests.md` and `connection-profiles.md` both still describe this as unwired ("nothing yet
  turns a loaded `DeviceManifest` into an actual `IControlSurface`/decoder pair or opens a connection
  from one"), but that reasoning predates `IControlSurface` actually landing (2026-09-22, proven by
  K8055/Busylight/SCPI). The blocker is no longer "the interface doesn't exist" — it's that nothing
  builds one from a manifest's declarative command/response schema + `UiDefinition` at load time. This
  is the actual payoff of the no-code device-manifest path (device-control-modules.md's "assembled
  declaratively" goal); today a profile with a `ManifestName` only makes the `UiDefinition` available,
  not a working control panel.
- Once device manifest support is further along, build an editor for it — at least a default
  render for request/response messages, ideally a presentation editor. New field types this implies
  beyond `DevTerm.UiDefinitions`' current seven: bar graph (one bar per channel), strip/roll chart
  recorder (1+ channels), and the vector/coordinate families x/y/z/h/s/v, x/y/h/s/v, r/theta,
  r/theta/h/s/v.
- **Low priority: consolidate hand-coded settings forms onto `DevTerm.UiDefinitions`' own model,
  driven by metadata on the model class itself, instead of maintaining separate ad-hoc forms per
  screen.** Today there are two disconnected ways dev-term describes "a form": `DevTerm.UiDefinitions`'
  `UiSection`/`UiControl` vocabulary (built for device manifests, not yet wired to any renderer),
  and the `[Category]`/`[DisplayName]` `System.ComponentModel` attributes added to `CliOptions` this
  session (pure documentation metadata today — nothing reads them). Meanwhile the Connection
  Editor's actual fields are hand-built twice, once per front end (`ConfigureMode`'s Terminal.Gui
  controls, `DeviceProfilesWindow`'s XAML), with no shared declarative source at all. The idea:
  define a small set of attributes (reusing or extending `UiDefinitions`' existing control-kind
  vocabulary — button/toggle/slider/numeric/choice/textField/indicator — rather than inventing a
  second one) that can annotate *any* model's properties (`CliOptions` included), a reflection-based
  generator that turns an annotated model into the same `UiDefinition` a device manifest already
  produces, and exactly one render engine per front end (Terminal.Gui, WPF) that turns a
  `UiDefinition` into real controls regardless of whether it came from a device manifest's JSON or
  from reflecting over `CliOptions`. Design once, render everywhere — the render engine work this
  unlocks is also the actual blocker on `UiDefinitions`' own "step one only" status and on the
  device-manifest editor item above, so it isn't purely a Connection Editor cleanup. Real scope
  questions before starting: whether `UiDefinition`'s current one-level-of-grouping shape (built
  from device mockups, not a settings form) covers the Connection Editor's transport-conditional
  field groups without extension, and whether the Connection Editor's existing
  `ConnectionEditorViewModel`/`RelayCommand` binding layer sits *under* the render engine (rendered
  controls still bind to the same view model) or gets subsumed by it.

### Device control panel UX & theming

- **Low priority: theming — light/dark mode plus custom, user-defined theme profiles, for both
  front ends.** Neither has any theme support today; both currently just take whatever colors their
  framework defaults to. Two separate investigations before designing anything, per this project's
  own "check before assuming" habit:
  - **WPF**: .NET's newer Fluent theme for WPF (`ThemeMode` = Light/Dark/System) may already cover
    light/dark for free on `net10.0-windows` — needs confirming against this project's actual TFM/
    styles before assuming it's available, not assumed from memory of the feature's announcement.
  - **TUI**: Terminal.Gui v2.5.0 has its own `Scheme`/`Attribute` system with real RGB colors (not
    just 16 named ones — confirmed this session via `Cell.Attribute.Foreground`/`Background` while
    building `TuiScreenshot`) and a `Color.Colors16`/`ColorName16` palette; check whether it already
    ships swappable named schemes before building light/dark switching from scratch.

### Tooling

- **A custom `DevTerm.Analyzers` Roslyn project**, for coding standards that are specific to this
  codebase's own semantics and can't be expressed via `.editorconfig`/StyleCop.Analyzers (see
  `docs/coding-standards.md`, landed 2026-09-16) — e.g. a project-specific rule like "every
  `ITransport` implementation must no-op on an empty write, not throw" (see `CLAUDE.md`'s
  constraints list for why that one matters). Deliberately not built yet: no such rule has actually
  been declared that a generic analyzer can't already cover — build it once one is.

### Logging

- A logger mode — capture every sent/received message with a direction prefix and a sequence
  number, for later review (not the same as the rendering-presenter export formats above).

## Research (not backlog-ready)

- [BYTECC BT-UP01 USB-over-network bridge](docs/design/proposals/bytecc-bt-up01-usb-network-bridge.md) —
  **low priority for now**, by choice (2026-09-15): pursuing the "reverse-engineer it directly"
  route (network sniffer + decompiling the vendor client) rather than the boring-but-reliable
  Raspberry-Pi-running-`usbip` fallback, but only once a real capture exists — nothing to act on
  until then. Not a build item yet, unlike everything above: no protocol reverse-engineering has
  been done and none exists publicly. Two cheap checks needed before deciding whether this is even a
  reverse-engineering project at all: does the vendor's own client software just make the remote
  USB device appear local (making it a non-issue for dev-term entirely), and does the box happen to
  already speak the open USB/IP protocol. If it turns out to need real protocol work, it's a
  fundamentally bigger kind of thing than any transport/decoder proposal above — tunneling USB
  itself (enumeration, control/bulk/interrupt transfers), not decoding one device's byte protocol.
- A logger-playback mode (realtime/fast/slow/rewind/fast-forward/pause, plus trim/markup) for the
  logger-mode capture above — speculative, depends on logger mode existing first and on a concrete
  file format for the captured log, neither of which exist yet.
