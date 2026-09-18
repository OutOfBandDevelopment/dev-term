# Backlog

Not-yet-started work for dev-term, prioritized where a priority has actually been given, plus
early-stage research not ready to be scoped as backlog. Active/in-progress work lives in
[`TODO.md`](TODO.md) instead, kept lean on purpose — this file is where lower-priority and
not-yet-started work lives so it doesn't add scroll-past overhead to checking on active work.
Completed work is logged by date under `docs/changes/`.

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
- **Connection Editor, from the 2026-09-15 Architect Notes** (see `TODO.md`'s "In progress" entry
  for a summary of what already landed, and `docs/changes/2026-09-15.md`/`2026-09-16.md` for full
  detail on each increment). Still open:
  Prioritized per direction given 2026-09-16, with the serial-port naming item moved to lower
  priority. ~~Export-selected/export-all as a zip~~ landed 2026-09-16: multi-select in the profiles
  list (WPF `ListBox.SelectionMode="Extended"`, TUI `ListView.MarkMultiple`/`ShowMarks`), Export
  Selected/Export All (one `{name}.json` per profile in a zip), and zip-aware Import with per-name
  Replace/Rename/Skip conflict resolution (`ConnectionEditorViewModel.ResolveZipImportConflict`) —
  see `docs/changes/2026-09-16.md` and `docs/specs/connection-editor.md`. Its two follow-ups (bulk
  profile removal and a wholesale "delete all, then import" option) both landed 2026-09-18, as did
  the Windows half of a long/short name for detected serial ports.
  - **Detected serial port descriptions on Linux/macOS** — the Windows description landed
    2026-09-18 (`ISerialPortDiscovery.GetPortDescriptions()`, read from the Plug-and-Play registry);
    Linux (udev/sysfs) and macOS (IOKit) still list short names only. Low priority.
  - ~~TCP: named hostnames as well as IPv4/IPv6~~ — already works: `SystemTcpConnectionSource`
    connects via `TcpClient.ConnectAsync(string, int, ...)`, which resolves a hostname, IPv4, or
    IPv6 literal natively. Confirmed by reading the code, not by guessing; no change needed.
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
- A logger mode — capture every sent/received message with a direction prefix and a sequence
  number, for later review (not the same as the rendering-presenter export formats above).
- **A custom `DevTerm.Analyzers` Roslyn project**, for coding standards that are specific to this
  codebase's own semantics and can't be expressed via `.editorconfig`/StyleCop.Analyzers (see
  `docs/coding-standards.md`, landed 2026-09-16) — e.g. a project-specific rule like "every
  `ITransport` implementation must no-op on an empty write, not throw" (see `CLAUDE.md`'s
  constraints list for why that one matters). Deliberately not built yet: no such rule has actually
  been declared that a generic analyzer can't already cover — build it once one is.

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
