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
  Deferred 2026-09-30 — no such server was reachable this session.
- UDP transport (target + listener modes). Real target hardware once built:
  [EByte E810-DTU(RS485)](docs/design/proposals/ebyte-e810-dtu-config-protocol.md)'s broadcast
  discovery/config protocol (port 1901) — note the proposal's own byte-count discrepancy needs
  resolving against a fresh capture before implementing, not just the existing notes.

### Plugin architecture, decoders & presenters

- `.ksy` binary-response schemas: promoted to in-progress (2026-10-02) — see `TODO.md`. Kaitai is read/parse-only,
  so it only ever covers the response half; the SCPI baseline in `docs/design/device-control-modules.md` is a
  separate, already-built path.
- Dynamic plugin loading (`AssemblyLoadContext`, `IPluginModule`, manifest/versioning) per
  `docs/design/plugin-model.md`. Today's built-in transports/presenters are wired by hand in
  `Program.cs`, not actually loaded as plugins yet, despite already using the same contracts.
- Protocol decoders with a human-readable text baseline; composite/channelized decoders;
  mappable presenters.
- Rendering presenters (HPGL/PostScript/PCL, telemetry plots) + export (SVG/PNG/JPG) — the actual
  drawing/rendering half, for the HPGL/PostScript/PCL the Stream Monitor ([proposal](docs/design/proposals/stream-content-detection.md))
  already captures and saves but doesn't draw yet.

### Device control modules & hardware profiles

- Declarative command/response schema for device control modules (send template + response
  pattern, an SCPI baseline for common bench-instrument commands) — see the new section in
  `docs/design/device-control-modules.md`.
  **Still open:**
  - [DE-5000 LCR meter](docs/design/proposals/de5000-lcr-meter-protocol.md) landed 2026-09-25
  (`DevTerm.Devices.De5000`, see `docs/changes/2026-09-25.md`) but is unverified against real
  hardware — deferred, not a dev-term-side blocker: the general BLE/GATT transport is confirmed
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

### Observability

- **OpenTelemetry / general app-logging support** — `DevTerm.Logging` today is device-session
  transcript recording (`SessionLogger`/`SessionLogWriter`/playback), not application diagnostics.
  There's no structured logging, tracing, or metrics for dev-term's own internals (connection
  lifecycle, transport faults, presenter errors) beyond ad-hoc output-pane messages. Scope: decide
  whether to wire `Microsoft.Extensions.Logging` + an OTel exporter (console/OTLP) through DI
  (`AddDevTermCore`/`AddDevTermFrontEnd`), what's worth instrumenting first (`Session`
  open/close/fault, `ITransport` connect/disconnect), and whether it's opt-in (a CLI flag/config
  section) given most users won't have a collector running. Not started — raised 2026-10-01, no
  priority set yet.

### TUI theming

- **`light`'s `background`/`fieldBackground`/`selectionBackground` all collapse onto the same nearest
  ANSI-16 color ("White") under Terminal.Gui's legacy-conhost 16-color downgrade** — worse than the
  `dark` theme's equivalent collision fixed 2026-09-30 (see `docs/design/theming.md`'s "TUI" section),
  since all three roles collapse here, not just two. Not fixed yet: `light`'s field/selection colors
  are deliberately close to white for the WPF app's look, and darkening them enough to separate under
  16-color legacy conhost would change that look too, for a narrower case (a legacy black-background
  console running the *light* theme, rather than `dark`, dev-term's default). Needs a decision on
  whether to accept a WPF-visible palette change, or scope a TUI-only override instead.

### Proposed Ideas

- [Web-accessible host service (WebSocket tunnels + Blazor front end)](docs/design/proposals/web-tunnel-blazor-frontend.md).
- [LXI support](docs/design/proposals/lxi-support.md).
- [MQTT, AMQP, STOMP protocol support](docs/design/proposals/message-broker-protocols.md) — receive/
  route inbound messages and trigger outbound events to external services.
- [Z-Wave support](docs/design/proposals/z-wave-support.md) — ZStick, Z-Wave RPi hat.
- [Schema files for custom formats](docs/design/proposals/format-schema-files.md) — generated JSON Schemas for manifests,
  UI definitions and profiles (proposed 2026-10-02; spike `JsonSchemaExporter` first).

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
