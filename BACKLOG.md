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
- **USBTMC transport** — the USB class most bench equipment (Rigol/Keysight/etc.) actually uses for
  local USB control; neither HID nor serial, needed its own raw-USB implementation — IVI.NET/VISA was
  considered and rejected (Windows/.NET-Framework-oriented, plus a separate proprietary native
  runtime install, unlike every other dev-term transport). Built on LibUsbDotNet
  (`DevTerm.Transports.Usbtmc`: `UsbtmcTransport`/`SystemUsbtmcDevice`/`UsbtmcCodec`), device
  enumeration/opening/stall-recovery all verified against real Rigol hardware (2026-09-23) after a
  per-machine Zadig/WinUSB driver rebind (see [`docs/design/usbtmc-transport.md`](docs/design/usbtmc-transport.md)
  for the full setup). **Known, deprioritized gap**: reading a query's reply (`*IDN?` etc.) stalls
  the bulk-IN endpoint (`Error.Pipe`) against at least two real Rigol multimeters (DM3000, DM3058E) —
  confirmed NOT a firmware limitation (the same units work fine under Rigol's own Ultra
  Sigma/NI-VISA), so it's specifically something about generic WinUSB/libusb access these Rigol units
  don't like; the actual fix needs a USB packet capture of a working NI-VISA exchange to diff against,
  which hasn't been done — reconfirmed still unchanged 2026-09-24 (`docs/changes/2026-09-24.md`).
  USBPcap + Wireshark are now installed on the bench PC; the next concrete step is capturing a
  working NI-VISA `*IDN?` exchange and comparing it against both `LibUsbDotNet`'s and an independent
  Python `pyusb`/`python-usbtmc` exchange (a second non-.NET reference makes it easier to tell
  "LibUsbDotNet-specific" apart from "any generic libusb/WinUSB binding") — not yet done. Parked
  rather than chased further with more blind guesses — see
  `docs/design/usbtmc-transport.md`'s "Open questions" and `docs/changes/2026-09-23.md` for the full
  investigation. Real target hardware confirmed reachable over USBTMC 2026-09-24: the plain
  **Rigol DG1022** and the **Rigol DS1102E** oscilloscope both enumerate
  (`--listusbtmcdevices true`), but share the same USB PID (`0x0588`) and the DG1022 unit's own
  descriptor misreports its series as "DG3000" rather than "DG1000" — a serial number, not VID/PID
  alone, would be needed to target one specifically; neither responds to a query yet, same stall as
  above. **Power-cycling is an unreliable workaround, not a fix (2026-09-24)**: during today's
  hardware review, the DM3058E wedged mid-session (a hung bulk-IN read) and a physical power-cycle
  cleared it — `*IDN?` and a full 83-query profile sweep both worked cleanly afterward. Encouraged by
  that, all four bench Rigol units (DM3058E, DG1022, DS1102E, DG1062Z) were power-cycled together,
  but this did **not** reliably fix the other two USBTMC devices sharing PID `0x0588`: after the
  restart, both the DG1022 and the DS1102E (the latter previously confirmed working earlier the same
  day, before the restart) still enumerate correctly (`--listusbtmcdevices true` lists both with
  correct serials) but return zero bytes for `*IDN?` — no exception, no timeout message, the CLI just
  prints nothing and exits cleanly, repeatable across multiple retries and settle-time waits. The
  DM3058E (PID `0x09C4`, no serial-sharing) kept working the whole time. So a restart cleared it once
  for one device and not at all for two others in the same session, even on a second restart attempt
  (the instruments' own front-panel displays confirmed both entered remote mode both times, so the
  control-transfer/`SetRemote` path is fine — it's specifically the bulk-IN reply path that stays
  stalled) — not a dependable recovery step.
  Separately, this surfaced a real gap in `UsbtmcTransport.WriteAsync`/`ReadReply`
  (`DevTerm.Transports.Usbtmc\UsbtmcTransport.cs`): when both the initial `ReadBulkIn` and its one
  stall retry return a zero/negative count, `ReadReply` returns an **empty** `List<byte[]>`, not
  `null` — `WriteAsync`'s `replyChunks is null` check then falls through to an empty `foreach` and a
  no-op flush, so a fully-failed query looks identical to a query that legitimately returned nothing,
  with no exception or log line surfaced anywhere. Worth fixing regardless of the root cause below:
  a failed reply read should be distinguishable from an empty one (e.g. throw, or return a sentinel
  distinct from `null`/empty). Worth investigating whether a *software*-only recovery is possible
  instead of requiring physical power-cycling every time: `docs/protocols/usbtmc/USBTMC_1_00.md`
  section 4.2.1.6 documents a class-specific `INITIATE_CLEAR` control request
  (`bmRequestType=0xA1`, `bRequest=5`, distinct from a raw USB `CLEAR_FEATURE`/`ENDPOINT_HALT`, which
  is all `SystemUsbtmcDevice`'s existing `ClearHalt()` on `Open()` actually does today) plus
  `CHECK_CLEAR_STATUS` to poll it to completion — neither is currently sent anywhere in
  `DevTerm.Transports.Usbtmc`. Also worth checking whether LibUsbDotNet/WinUSB expose an equivalent of
  a USB port reset (`libusb_reset_device` in libusb proper) as a fallback if `INITIATE_CLEAR` alone
  doesn't unwedge a truly stalled endpoint. Neither has been tried yet — this is a research/prototype
  item, not a confirmed fix.
  **This is a general transport reliability defect, not just a cause of total stalls on two "stuck"
  instruments (2026-09-24)**: a controlled 348-command sweep against the DG1062Z (`SYSTem:ERRor?`
  interleaved after every one of 174 real queries, to sync-check each reply) — a unit that otherwise
  answered every query correctly all session — still silently dropped roughly 6% of replies (22 of
  348), with no exception, no error, and no way to tell from the CLI's output alone that anything was
  lost. Confirmed via an unambiguous alignment anchor (the `SYSTem:COMMunicate:LAN:MAC?` reply,
  `00-19-AF-04-B4-70`, which can only be one specific line): a `SYSTem:ERRor?` sent immediately after
  that anchor never produced its `0,"No error"` reply at all — the very next line in the log was
  already the *following* query's answer. Ruled out command pacing as an alternative explanation: the
  same 15-command probe dropped replies at both 0.6s and 2s inter-command gaps (2/15 and 3/15
  respectively). This means `ReadReply`'s empty-list-vs-null gap above isn't a special case of two bad
  instruments — it can silently corrupt any long real-hardware session on any USBTMC device, which
  should raise this fix's priority.
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
  [SCPI instrument control](docs/design/proposals/scpi-instrument-control.md) is **implemented**
  (2026-09-23, `DevTerm.Devices.Scpi`) — the first real declarative-schema instance, needing no new
  transport for its RS-232/USB-CDC/LAN devices (USBTMC-only local-USB devices excepted — see above).
  Real-hardware-confirmed for the HP/Agilent/Keysight 34401A, both Korad KA3005P/KA6003P curated
  profiles (2026-09-23, see `docs/changes/2026-09-23.md`), and the Rigol DS1102E (renamed from the
  wrong-model DS1105E, 2026-09-24, see `docs/changes/2026-09-24.md`). Also real-hardware-confirmed
  2026-09-24: the **Rigol DM3058E** (full 83-query paced sweep, clean) and the **Rigol DG1062Z**
  (full 174-query paced sweep, `*IDN?` plus the rest of the profile) — see `docs/test/` for the
  session report. The DG1062Z sweep found a confirmed profile-syntax defect cluster — each isolated
  and repeated (3-5x) individually to be sure, rather than trusting the original interleaved sweep's
  line-by-line attribution: `ROSCillator:SOURce?`, `COUPling:AMPLitude:STATe?`,
  `COUPling:AMPLitude:MODE?`, `COUPling:AMPLitude:RATio?`, `COUNter:CURRent:FREQuency?`,
  `COUNter:CURRent:PERiod?`, `COUNter:CURRent:DUTYcycle?`, `COUNter:CURRent:PWIDth?`,
  `COUNter:CURRent:NWIDth?`, `COUNter:SENSitivity?`, `COUNter:HFR?`, and
  `COUNter:TRIGger:LEVel?` are all rejected outright by the real firmware
  (`-113,"Undefined header; keyword cannot be found"`), every time, regardless of the frequency
  counter's own enabled state (`COUNter:STATe ON` first didn't unlock them either) — genuine wrong
  syntax, not a state-gating quirk, needs fixing against the DG1000Z series manual. The sibling
  `COUPling:FREQuency:*`/`COUPling:PHASe:*` families and `COUPling:AMPLitude:DEViation?`/
  `COUNter:STATe?`/`COUNter:COUPling?` are confirmed **valid** (consistent non-error replies across
  repeats). Separately, a new deterministic pattern (not the ~6% random drop described in the
  USBTMC transport item above): isolating single invalid queries showed the query *immediately
  following* a `-113` reply is reliably swallowed with no reply at all, every time — worth a closer
  look at whether the firmware or the transport is responsible before assuming it's the same bug as
  the random-drop one. The **Rigol DG1022** profile
  remains unconfirmed; verifying it is blocked on the
  USBTMC bulk-IN stall noted above (this unit never answered a single query all session, unlike
  DS1102E which worked earlier in the day before also becoming stuck — see that note for detail). No
  `DevTerm.Transports.Usbtmc.Tests` project exists yet either — worth adding given the transport code
  (`UsbtmcTransport.IsQuery`, `SystemUsbtmcDevice`'s NUL-stripping helpers) has already needed two
  real-hardware-discovered fixes with no unit coverage of its own.
  [DE-5000 LCR meter](docs/design/proposals/de5000-lcr-meter-protocol.md) is gated on the BLE
  transport above (adapter hardware already built). [Radex One](docs/design/proposals/radex-one-protocol.md)'s
  transport dependency (USB HID) is now built, but it still needs its HID report-framing question
  resolved (see that proposal's open questions) before implementing the decoder.
  [Favero fencing protocol](docs/design/proposals/favero-fencing-protocol.md) is **deprioritized** —
  no hardware access to test against anymore; kept as a documented proposal only.
  A minimal Tektronix 2230 profile (`Profiles/tektronix-2230.json`, one confirmed command, `ID?`)
  was added to `DevTerm.Devices.Scpi` directly on 2026-09-23 — the 2230 predates SCPI and doesn't
  speak it, but `ScpiControlSurface`'s plain template substitution didn't need SCPI syntax to send
  one confirmed command. See
  [tektronix-2230-protocol.md](docs/design/proposals/tektronix-2230-protocol.md) for what's known
  (`ID?` → `ID TEK/2230,V81.1,VERS:14;`, confirmed live against the project's own two owned units),
  its revised "Status" section for what reusing the SCPI plumbing as-is doesn't yet cover (unconfirmed
  reply framing, no auto-detect), and the real-hardware probing still needed before more commands
  can be curated.
  Three more real, HID/serial-only (no new transport needed) targets, sourced from a local prior-art
  decoder library (`dotex/Incoming/BinaryDecoders`), each with a real-hardware-verified or
  cross-referenced protocol: [Kuando Busylight](docs/design/proposals/kuando-busylight-protocol.md)
  (its single-command report format is confirmed working live against real hardware; its
  batch-program format is not — see that proposal's open question), [Velleman K8055](docs/design/proposals/velleman-k8055-protocol.md)
  (already owned, simplest of the binary proposals), and [Zoom H4n remote](docs/design/proposals/zoom-h4n-remote-protocol.md)
  (plain serial via an already-built adapter cable, buildable today like SCPI).
- **Tektronix 2230 — decided direction, not yet built**: rather than continuing to reuse
  `ScpiControlSurface`/`ScpiReplyPresenter` as more commands get confirmed, build a separate "Text
  Command" device module (`DevTerm.Devices.TextCommand`? — mirroring `DevTerm.Devices.Scpi`'s shape:
  profile/control-surface/reply-presenter) that allows more generic command strings than SCPI's
  `{Name}`-token templates assume. This resolves
  [tektronix-2230-protocol.md](docs/design/proposals/tektronix-2230-protocol.md)'s own "why this
  isn't (fully) folded into the SCPI module" open question in favor of the separate-module option,
  once real-hardware probing (still needed — see that doc) turns up enough of the 2230's command set
  to justify it. (The reported terminator correction, `\r` not `\n`, was already applied and
  reconfirmed 2026-09-23 — see `docs/changes/2026-09-23.md`.)

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
- ~~TCP: named hostnames as well as IPv4/IPv6~~ — already works: `SystemTcpConnectionSource`
  connects via `TcpClient.ConnectAsync(string, int, ...)`, which resolves a hostname, IPv4, or
  IPv6 literal natively. Confirmed by reading the code, not by guessing; no change needed.

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
