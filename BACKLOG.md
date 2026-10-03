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
- UDP transport (target + listener modes). Real target hardware once built:
  [EByte E810-DTU(RS485)](docs/design/proposals/ebyte-e810-dtu-config-protocol.md)'s broadcast
  discovery/config protocol (port 1901) — note the proposal's own byte-count discrepancy needs
  resolving against a fresh capture before implementing, not just the existing notes.

### Plugin architecture, decoders & presenters

- Protocol decoders with a human-readable text baseline; composite/channelized decoders;
  mappable presenters.
- Rendering presenters (HPGL/PostScript/PCL, telemetry plots) + export (SVG/PNG/JPG) — the actual
  drawing/rendering half, for the HPGL/PostScript/PCL the Stream Monitor ([proposal](docs/design/features/stream-content-detection.md))
  already captures and saves. HP-GL now converts to SVG, listed in the capture list and drawn in WPF (2026-10-02); PostScript/PCL and TUI drawing remain.
- Stream Monitor leftovers ([feature](docs/design/features/stream-content-detection.md)): direct in-window preview of
  PostScript and PCL (needs the rendering presenter above), and SVG drawing in the TUI (it only lists the converted file).

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
- [Network device discovery](docs/design/proposals/network-device-discovery.md) and [in-app config editors for network bridges](docs/design/proposals/network-device-config-editors.md) (proposed 2026-10-03): one Detect button for any network device with prefill, and a Device > Configure device menu for the USR-TCP232-302 and EByte E810-DTU.
- [Z-Wave support](docs/design/proposals/z-wave-support.md) — ZStick, Z-Wave RPi hat.

### Decided 2026-10-03 (owner interview), not started

- **Multi-session tabs:** the merged time-ordered view of logging and the Stream Monitor across tabs (per-tab history, logging, empty window and Alt+Left/Right already built; [multi-session-ui](docs/design/multi-session-ui.md)).
- **Remember a dismissed/used control-panel hint per profile** (the hint itself shipped 2026-10-03) ([device-control-panel spec](docs/specs/device-control-panel.md)).
- **Stream Monitor filter, search and sort in the TUI window** (WPF has it; `StreamCaptureView` is shared) ([stream-monitor spec](docs/specs/stream-monitor.md)).
- **Per-module destructive-command confirmation**, declared in a manifest or profile ([device-control-modules](docs/design/device-control-modules.md)).
- **Chart hover readout, table view and history export** ([spec](docs/specs/device-control-panel.md)).
- **TUI terminal-palette theme, and a live-following `system` theme** ([theming](docs/design/theming.md)).
- **Third-party plugins out of process, with user approval** (optionally once per hash) ([plugin-model](docs/design/plugin-model.md)); **optional structured-message model** ([presenters](docs/design/presenters.md)); **manifest-declared reply correlation** ([device-control-modules](docs/design/device-control-modules.md)); **indexed, streamed log playback** ([session-logging](docs/design/session-logging.md)).

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
- **Cross-process session channel, remaining work:** the read-write channel and the localhost web-service variant. The read-only pipe, `--pipe <name>` (all three front ends) and the `--attach <name>` tail client are built; see [cross-process-control-channel](docs/design/proposals/cross-process-control-channel.md).
- **Project (workspace) state: save and restore all open sessions.** Save the set of open tabs (each one's connection profile, plus as much state as is practical: presenter choices, send history, Stream Monitor/log settings, window layout) as one project file, and reopen it on launch or from a menu so closing the program with several devices attached comes back to the same connections. Builds on the multi-tab sessions; needs a decision on connection-only versus full state, and whether to auto-restore the last project.
- **PCX (and PCL raster) preview in the Stream Monitor (rejected).** WPF has no PCX decoder, so a captured PCX is saved but not
  shown. Options: a small built-in PCX decoder (the format is simple RLE; no dependency) or Magick.NET (large native
  package, but also covers other formats). A PCL raster job needs its `ESC*b<n>W` rows decoded to a bitmap.
  **Decision 2026-10-03: the PCX decoder and the PCL raster preview are both rejected for now** (BMP and TIFF hardcopy already preview); a captured PCX or PCL job stays saved, and PCL still converts through GhostPCL. Revisit only if asked.
- **Web host: service-driven connections.** `DevTerm.Web` should need no connection arguments: device enumeration, project create/manage and open-connection services, per-connection tokens and `/ws/{id}` tunnels, a host events stream, a Blazor front end, Scalar (OpenAPI) for the services and AsyncAPI UI for the WebSocket/event channels. Design and open questions: [web-tunnel-blazor-frontend.md](docs/design/proposals/web-tunnel-blazor-frontend.md). Shares a project model with the project-state item above.

- **Routing proxy follow-ups:** wire `MessageRouter` to a real MQTT/AMQP/STOMP connection (an `IMessageSink` plus feeding `OnBrokerMessage`), a rule editor in the front ends, and loading rules from a profile or manifest. The proof of concept is built; see [message-broker-protocols](docs/design/proposals/message-broker-protocols.md).
