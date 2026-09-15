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
  actually removes it. Not yet done: real-hardware verification of either UI (only smoke-tested
  that they launch without crashing), a `TuiMode`/`MainWindow` test project (Terminal.Gui/WPF UI
  code is awkward to unit test — headless/automation approach TBD), and the multi-session-lifetime
  issue below.

- **USB HID transport** (`DevTerm.Transports.Hid`), landed 2026-09-15 — [HidSharp](https://www.nuget.org/packages/HidSharp)
  2.6.4, cross-platform. `HidTransport`/`SystemHidDevice`/`IHidDeviceFactory`/`IHidDeviceDiscovery`
  mirror the serial transport's shape exactly. The interesting piece is `HidReadStream`: HidSharp
  gives no event analogous to `SerialPort.DataReceived`, and its `Read`/`Write` are plain
  `BeginRead`/`EndRead`-backed, which (like `SerialPort.BaseStream.ReadAsync`) doesn't reliably
  honor a `CancellationToken` on an in-flight read — same class of problem already hit for serial.
  Fixed with a dedicated background thread (not a `Task.Run`-wrapped call per read, which was
  already rejected for serial for the same "orphaned unobserved background work" reason) that
  blocks on `HidStream.Read` bounded by `ReadTimeout`, handing completed reports to a
  `Channel<byte[]>` — the actual async-facing `ReadAsync` only ever awaits the channel, so
  cancellation is immediate and correct regardless of the background thread's own (bounded,
  best-effort) shutdown. **Not verified against real hardware for the read path specifically**
  (opened/wrote to/closed a real device — a mouse's RGB control interface — successfully, but
  never got real inbound reports flowing to confirm the read-thread/channel handoff end-to-end);
  the open/write/close path *did* surface and fix a real bug: a zero-length write (an empty typed
  line with no line ending) throws a raw Win32 `IOException` on Windows HID (unlike serial/TCP,
  where it's a harmless no-op) — fixed as a no-op in `HidTransport.WriteAsync`, and separately
  widened `CliMode`/`TuiMode`'s send-path exception handling (previously only caught
  `TimeoutException`) to catch any device I/O failure and report it instead of crashing, since a
  wrong *non-zero* length (a real device-specific framing constraint, not something a generic text
  presenter can guess) still legitimately fails and needs to fail cleanly. Wired end-to-end: new
  `--transport hid --hidvendorid <n> --hidproductid <n> [--hidserialnumber <sn>]` CLI options (plus
  `--listhiddevices true` discovery, mirroring `--listports`), `CliOptionsValidator`,
  `ConnectionDescription`, `ConnectionErrorMessages` hint, `AddDevTermFrontEnd` wiring — all with
  test coverage (136 tests across the solution now). Next: real hardware verification of the read
  path against an actual report-based device (Radex One, once its HID report framing is
  understood — see that proposal's open questions).

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
- UDP transport (target + listener modes).
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
- Resolve the stateful-presenter-vs-DI-singleton lifetime issue noted in
  `docs/design/presenters.md` before TUI/WPF support more than one concurrent session — today's
  single-session-per-process CLI usage doesn't hit it, but a multi-session front end would.
