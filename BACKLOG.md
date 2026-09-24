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

- **BLE transport** (`DevTerm.Transports.Ble`), cross-platform by design via a pluggable per-OS
  adapter seam (Windows via `Windows.Devices.Bluetooth` first; Linux/BlueZ and macOS/CoreBluetooth
  addable later, including as community/self-contributed adapters) — see
  `docs/design/transports.md`'s BLE section for the adapter-contract shape and the "BLE Serial"
  (Nordic UART Service) pattern most hobbyist devices actually use. Target hardware identified:
  a [DER EE DE-5000 LCR meter](docs/design/proposals/de5000-lcr-meter-protocol.md), whose optical
  (IR) UART output is bridged to BLE via a custom adapter already built — unblocks that proposal
  once built. Still need: which GATT profile the custom adapter actually exposes (NUS or custom).
- **USBTMC transport** — built on LibUsbDotNet (`DevTerm.Transports.Usbtmc`: `UsbtmcTransport`/
  `SystemUsbtmcDevice`/`UsbtmcCodec`); IVI.NET/VISA was considered and rejected (Windows/.NET-
  Framework-oriented, needs a separate proprietary native runtime, unlike every other dev-term
  transport). Device enumeration/opening verified against real Rigol hardware after a per-machine
  Zadig/WinUSB driver rebind (see `docs/design/usbtmc-transport.md`). The bulk-IN reassembly bug that
  caused intermittent "communication lockups" (DG1022/DS1102E/DM3058E), plus a compounding
  empty-reply-vs-null gap that could silently drop any USBTMC device's reply with no error, are both
  fixed — see
  [`docs/design/features/usbtmc-bulk-in-reassembly-fix.md`](docs/design/features/usbtmc-bulk-in-reassembly-fix.md)
  and `docs/changes/2026-09-24.md`. **Still open:**
  - **DG1022 stuck at the device/USB level**, unresolved by the reassembly fix — every bulk-IN read
    attempt returns zero bytes regardless of retries or power-cycling (power-cycling did clear a
    similar stall once on the DM3058E, but did not reliably fix the DG1022/DS1102E on a later
    attempt — not a dependable recovery step). The `INITIATE_CLEAR`/`CHECK_CLEAR_STATUS`/
    `libusb_clear_halt` recovery pattern
    (`docs/protocols/usbtmc/USBTMC-libusb-winusb-implementation-guide.md` §7.5) is the most likely
    real fix, needing new `IUsbtmcDevice`/`SystemUsbtmcDevice` control-transfer support the
    reassembly fix's own scope explicitly excluded ("Do not change `SystemUsbtmcDevice.cs`") — not
    yet implemented.
  - **DG1062Z's ~6% silently-dropped-reply rate** (found via a 348-command sweep, 2026-09-24) **is
    likely improved but not re-confirmed** — the empty-vs-null gap the reassembly fix closed was the
    most consistent explanation (no exception, no error, the next query's reply just appears one line
    early), but the sweep hasn't been re-run against the fixed code to confirm the drop rate actually
    changed rather than just changing how a drop is reported.
  - The DG1022 and DS1102E share USB PID `0x0588`; the DG1022's own descriptor misreports its series
    as "DG3000" rather than "DG1000" — a serial number, not VID/PID alone, is needed to target one
    specifically (see "USBTMC device identity has no `DevicePath`-equivalent field" below).
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
- Resolve the stateful-presenter-vs-DI-singleton lifetime issue noted in
  `docs/design/presenters.md` before TUI/WPF support more than one concurrent session — today's
  single-session-per-process CLI usage doesn't hit it, but a multi-session front end would.

### Device control modules & hardware profiles

- Declarative command/response schema for device control modules (send template + response
  pattern, `.ksy` reference for binary layouts via [Kaitai Struct](https://kaitai.io/), an SCPI
  baseline for common bench-instrument commands) — see the new section in
  `docs/design/device-control-modules.md`.
- Device control modules (control surface + telemetry decode/plot) — see the declarative-schema
  item above for the command/response definition piece specifically.
  [SCPI instrument control](docs/design/features/scpi-instrument-control.md) is **implemented**
  (`DevTerm.Devices.Scpi`) — the first real declarative-schema instance, needing no new transport
  for its RS-232/USB-CDC/LAN devices (USBTMC-only local-USB devices excepted — see above).
  Real-hardware-confirmed: HP/Agilent/Keysight 34401A, both Korad KA3005P/KA6003P, Rigol DS1102E,
  Rigol DM3058E (full 83-query paced sweep), and Rigol DG1062Z (full 174-query paced sweep) — see
  `docs/changes/2026-09-23.md`/`2026-09-24.md` and `docs/test/` for the session reports.
  **Still open:**
  - **Rigol DG1022 profile remains unconfirmed** — blocked on the USBTMC bulk-IN stall above (this
    unit never answered a single query in any session so far).
  - **DG1062Z profile-syntax defect cluster** (2026-09-24, each isolated and repeated 3-5x to
    confirm): `ROSCillator:SOURce?`, `COUPling:AMPLitude:STATe?/:MODE?/:RATio?`, and every
    `COUNter:CURRent:FREQuency?/:PERiod?/:DUTYcycle?/:PWIDth?/:NWIDth?`, `COUNter:SENSitivity?`,
    `COUNter:HFR?`, and `COUNter:TRIGger:LEVel?` are rejected outright
    (`-113,"Undefined header; keyword cannot be found"`) regardless of the frequency counter's own
    enabled state — genuine wrong syntax against the DG1000Z series manual, not yet fixed. (The
    sibling `COUPling:FREQuency:*`/`COUPling:PHASe:*` families and `COUPling:AMPLitude:DEViation?`/
    `COUNter:STATe?`/`COUNter:COUPling?` are confirmed valid.) Separately, the query immediately
    following any `-113` error reply is reliably swallowed with no reply at all, every time — worth
    checking whether the firmware or the transport is responsible before assuming it's the same bug
    as the DG1062Z drop-rate item above.
  [DE-5000 LCR meter](docs/design/proposals/de5000-lcr-meter-protocol.md) is gated on the BLE
  transport above (adapter hardware already built). [Radex One](docs/design/proposals/radex-one-protocol.md)'s
  transport dependency (USB HID) is now built, but it still needs its HID report-framing question
  resolved (see that proposal's open questions) before implementing the decoder.
  [Favero fencing protocol](docs/design/proposals/favero-fencing-protocol.md) is **deprioritized** —
  no hardware access to test against anymore; kept as a documented proposal only.
  [Zoom H4n remote](docs/design/proposals/zoom-h4n-remote-protocol.md) (plain serial via an
  already-built adapter cable, no new transport needed) remains buildable today, like SCPI was.
- **Tektronix 2230 — decided direction, not yet built**: rather than continuing to reuse
  `ScpiControlSurface`/`ScpiReplyPresenter` as more commands get confirmed, build a separate "Text
  Command" device module (mirroring `DevTerm.Devices.Scpi`'s shape: profile/control-surface/
  reply-presenter) that allows more generic command strings than SCPI's `{Name}`-token templates
  assume. This resolves
  [tektronix-2230-protocol.md](docs/design/features/tektronix-2230-protocol.md)'s own "why this
  isn't (fully) folded into the SCPI module" open question in favor of the separate-module option,
  once real-hardware probing (still needed — see that doc) turns up enough of the 2230's command set
  to justify it.

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
detected serial ports.

- **Detected serial port descriptions on Linux/macOS** — the Windows description landed
  2026-09-18 (`ISerialPortDiscovery.GetPortDescriptions()`, read from the Plug-and-Play registry);
  Linux (udev/sysfs) and macOS (IOKit) still list short names only. Low priority.
- **WPF "not found" hint for a disconnected saved device** — the TUI's `ConfigureMode` shows a
  "(not found)" label next to the Serial port row and the shared HID/USBTMC vendor/product/serial
  row when `ConnectionEditorViewModel.ConnectedDeviceNotFound` is true (see
  `docs/changes/2026-09-24.md`'s DevicePath/SerialNumber fix); `DeviceProfilesWindow.xaml` (WPF)
  has no equivalent yet.
- **USBTMC device identity has no `DevicePath`-equivalent field** — HID's `SerialNumber`/
  `DevicePath` two-tier match (see `docs/changes/2026-09-24.md`) fixed disambiguating multiple
  same-VID/PID, serial-less HID devices; `UsbtmcDeviceDescriptor`/`UsbtmcTransportOptions` have no
  matching field, so a serial-less USBTMC instrument (less likely in practice than a serial-less
  HID gadget, but not ruled out) still can't be uniquely identified the same way.

### Device manifests & shared UI framework

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

- **Device control panel UX polish, from real-hardware use of the SCPI/K8055/Busylight panels
  (Architect notes, 2026-09-23)** — applies to `ControlPanelMode`/`ControlPanelWindow` generically,
  not one device:
  - Collapsible sections with a visual expand/collapse affordance, for every device profile's panel
    (SCPI profiles in particular tend to have many sections/commands).
  - Labels should show at full width without wrapping, aligned within their section/grouping.
  - An info icon (hover/select) on any field that sends a command, showing the exact command text
    that will be sent — useful for verifying a SCPI template's substituted value before sending it
    to real hardware.
  - `ScpiInstrumentProfile.Notes` (rendered via `UiDefinition.Description`) should be its own group,
    moved to the bottom of the panel rather than wherever it currently renders.
  - Extend `DevTerm.UiDefinitions`' parameter metadata to decouple a value's **data type** from its
    **control type** — e.g. a numeric value should be able to declare validation (an input range,
    reusing `ScpiParameterDefinition`'s existing `Minimum`/`Maximum`) independently of *which* widget
    renders it (slider vs. plain numeric field vs. text), rather than the current tight
    `Kind: Numeric|Choice|Text` → fixed-widget coupling. Overlaps with the "consolidate hand-coded
    settings forms" item above — likely the same underlying model extension.
- **Busylight custom-color dialog UX** (Architect notes, 2026-09-23): a "Custom" radio option
  alongside the named presets, with a swatch previewing the currently-configured custom color before
  entering the picker; the picker's last-used values should persist across re-opening it (currently
  reset each time — filed as a bug in `TODO.md`); Enter in the picker should act like clicking Apply.
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
  - **Custom profiles** (a user-defined named palette, not just a light/dark toggle) is the bigger
    ask on top of either — likely wants its own saved-profile mechanism, possibly modeled on how
    `ConnectionProfileStore` already saves/lists/loads named JSON files under `~/.dev-term/`, rather
    than a new storage pattern.

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
