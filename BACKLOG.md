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

- **BLE transport — Linux (BlueZ/D-Bus) and macOS (CoreBluetooth) backends.** Windows landed
  2026-09-25 and is real-hardware verified (`docs/design/transports.md`'s BLE section,
  `docs/changes/2026-09-25.md`/`2026-09-29.md`). The adapter seam supports adding either platform
  independently; neither has been started.
- RFC 2217 server (`Rfc2217ServerBridge`) — expose a local serial connection to the network for a
  remote RFC 2217 client to control. See `docs/design/rfc2217.md`. Note: binds loopback-only by
  default per the security note in that doc.
- **RFC 2217 client real-server verification** — `Rfc2217Transport` (landed 2026-09-30, see
  `docs/changes/2026-09-30.md`) is only unit-tested against a fake server so far. Run it against a
  real RFC 2217 server (`ser2net`, or pyserial's `rfc2217_server.py`) once one is available, per
  `docs/design/rfc2217.md`'s Testing strategy section, and flip its Status note once that's done.
  Deferred 2026-09-30; a ser2net container now exists (`containers/`) and a manual smoke run passed
  2026-10-02. Still open: an automated Integration test against it (skipping when the port is closed).
- UDP transport (target + listener modes). Real target hardware once built:
  [EByte E810-DTU(RS485)](docs/design/proposals/ebyte-e810-dtu-config-protocol.md)'s broadcast
  discovery/config protocol (port 1901) — note the proposal's own byte-count discrepancy needs
  resolving against a fresh capture before implementing, not just the existing notes.

### Plugin architecture, decoders & presenters

- `.ksy` importer gaps (queued in `TODO.md` 2026-10-02; the importer, binary frames and the Radex One layouts are built, see
  `docs/changes/2026-10-02.md`): **bit fields** (the Zoom H4n status `.ksy` fails to import until then), **variable-length
  frames**, and **checksums** (the Radex One reply's is skipped). Kaitai is read/parse-only,
  so it only ever covers the response half; the SCPI baseline in `docs/design/device-control-modules.md` is a
  separate, already-built path.
- Protocol decoders with a human-readable text baseline; composite/channelized decoders;
  mappable presenters.
- Rendering presenters (HPGL/PostScript/PCL, telemetry plots) + export (SVG/PNG/JPG) — the actual
  drawing/rendering half, for the HPGL/PostScript/PCL the Stream Monitor ([proposal](docs/design/features/stream-content-detection.md))
  already captures and saves. HP-GL now converts to SVG, listed in the capture list and drawn in WPF (2026-10-02); PostScript/PCL and TUI drawing remain.
- Stream Monitor leftovers ([feature](docs/design/features/stream-content-detection.md)): direct in-window preview of
  PostScript and PCL (needs the rendering presenter above), SVG drawing in the TUI (it only lists the converted file),
  a CLI-mode "export last N captures", and a retention/cleanup policy for the exports folder (low priority).

### Device control modules & hardware profiles

- Declarative command/response schema for device control modules (send template + response
  pattern, an SCPI baseline for common bench-instrument commands) — see the new section in
  `docs/design/device-control-modules.md`.
  **Still open:**
  - **DE-5000 bench pass.** [DE-5000 LCR meter](docs/design/proposals/de5000-lcr-meter-protocol.md) landed 2026-09-25
  (`DevTerm.Devices.De5000`, see `docs/changes/2026-09-25.md`) but is unverified against real
  hardware (its `.ksy` and decoder are unit-tested only; no meter available 2026-10-02) — deferred, not a dev-term-side blocker: the general BLE/GATT transport is confirmed
  working (`docs/test/2026-09-29-17-12-20.md`), but this meter's custom IR-to-BLE adapter is off the
  bench while its physical interface is rebuilt. Run `RealHardwareDe5000Tests` once it's back and
  `devterm.runsettings` has its device id/UUIDs filled in.

### Tooling

- **A custom `DevTerm.Analyzers` Roslyn project**, for coding standards that are specific to this
  codebase's own semantics and can't be expressed via `.editorconfig`/StyleCop.Analyzers (see
  `docs/coding-standards.md`, landed 2026-09-16) — e.g. a project-specific rule like "every
  `ITransport` implementation must no-op on an empty write, not throw" (see `CLAUDE.md`'s
  constraints list for why that one matters). Deliberately not built yet: no such rule has actually
  been declared that a generic analyzer can't already cover — build it once one is.

### Proposed Ideas

- [Web-accessible host service (WebSocket tunnels + Blazor front end)](docs/design/proposals/web-tunnel-blazor-frontend.md).
- LXI VXI-11 client (not started, by decision): [LXI feature](docs/design/features/lxi-support.md) phase 1 (discovery) is done; build this only when an instrument has no raw SCPI socket.
- [Network device discovery](docs/design/proposals/network-device-discovery.md) and [in-app config editors for network bridges](docs/design/proposals/network-device-config-editors.md) (proposed 2026-10-03): one Detect button for any network device with prefill, and a Device > Configure device menu for the USR-TCP232-302 and EByte E810-DTU.
- [Z-Wave support](docs/design/proposals/z-wave-support.md) — ZStick, Z-Wave RPi hat.

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

- **Dockable MDI layout (WPF).** Sessions and Stream Monitor windows that can be snapped/docked around and
  onto the main window, Visual Studio style, instead of fixed tabs plus floating windows. Needs a docking
  library choice (e.g. AvalonDock) and a layout-persistence story; the dark theme templates would need covering.
- **Watch a session over a named pipe.** Expose a live session's traffic (rx/tx, presenter output) on a named
  pipe so another process can tail it in real time. Needs a read-only vs. read-write decision, a pipe naming
  scheme per session, and a design doc with PlantUML.
- **Project (workspace) state: save and restore all open sessions.** Save the set of open tabs (each one's connection profile, plus as much state as is practical: presenter choices, send history, Stream Monitor/log settings, window layout) as one project file, and reopen it on launch or from a menu so closing the program with several devices attached comes back to the same connections. Builds on the multi-tab sessions; needs a decision on connection-only versus full state, and whether to auto-restore the last project.
- **PCX (and PCL raster) preview in the Stream Monitor.** WPF has no PCX decoder, so a captured PCX is saved but not
  shown. Options: a small built-in PCX decoder (the format is simple RLE; no dependency) or Magick.NET (large native
  package, but also covers other formats). A PCL raster job needs its `ESC*b<n>W` rows decoded to a bitmap.
- **Web host: service-driven connections.** `DevTerm.Web` should need no connection arguments: device enumeration, project create/manage and open-connection services, per-connection tokens and `/ws/{id}` tunnels, a host events stream, a Blazor front end, Scalar (OpenAPI) for the services and AsyncAPI UI for the WebSocket/event channels. Design and open questions: [web-tunnel-blazor-frontend.md](docs/design/proposals/web-tunnel-blazor-frontend.md). Shares a project model with the project-state item above.

- **Message routing proxy over the brokers** (owner direction 2026-10-03, proof of concept, loopback-testable): rules that publish a matching device message to a broker and map an inbound broker message to a device action, with a shared timecode for history across channels. See [message-broker-protocols](docs/design/proposals/message-broker-protocols.md).
- **Add and remove presenters on a live session** without reconnecting the device, and presenters that originate data (a simulation or virtual device). See [architecture](docs/design/architecture.md).
- **Configurable read/write rate limits and connect timeout/retry** as core options (decided 2026-10-03).
- **Confirm before switching profiles on a live connection; validate `ManifestPath` on save and at connect** (decided 2026-10-03).
- **Package-manifest extraction under the app data folder**, offered to the user, instead of a never-cleaned temp folder.
- **Stream Monitor: list export files already on disk; automatic HP-GL to SVG conversion as an option.** The external-tool and web converters are dropped.
