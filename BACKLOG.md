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
- RFC 2217 server (`Rfc2217ServerBridge`): the transparent TCP listener proxy is built (`--sharetcp`); `--sharerfc2217 true` adds the RFC 2217 negotiation (verified with pyserial); the client's baud/DTR/RTS now reach the device through `IComPortControl`. See `docs/design/rfc2217.md`.
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

- **Read-write control channel and out-of-process plugins over both a named pipe and a localhost web service** (decided 2026-10-07): see [cross-process-control-channel](docs/design/proposals/cross-process-control-channel.md) and [out-of-process-plugins](docs/design/proposals/out-of-process-plugins.md).

### Proposed Ideas

- [Web-accessible host service (WebSocket tunnels + Blazor front end)](docs/design/proposals/web-tunnel-blazor-frontend.md) (the host, `/ws` tunnel and `/panel` page are already built and in `TODO.md` item 1; this line is the rest).
- [Network device discovery](docs/design/proposals/network-device-discovery.md) and [in-app config editors for network bridges](docs/design/proposals/network-device-config-editors.md) (proposed 2026-10-03): one Detect button for any network device with prefill, and a Device > Configure device menu for the USR-TCP232-302 and EByte E810-DTU.
- [Z-Wave support](docs/design/proposals/z-wave-support.md) — ZStick, Z-Wave RPi hat.


## Proposals and bugs tracker

Every open bug and every unchecked item in a proposal's "Completion checklist" is listed here, so nothing sits only in a
proposal file. Last swept 2026-10-09.

Decided against: the web host using the project file as its whole connection set (connections stay an extra list); supporting other ES51919 meters beyond "probably works". Keep it current: add a row when a proposal gains an unchecked item, delete the row
when the item is checked off in the proposal (the `work-docs-audit` skill re-sweeps this section).

**Open bugs:** none (`docs/bugs/` holds only closed reports under `resolved/`, 71 in all). A new open bug gets a line here when filed.

| Proposal | Unchecked items | Blocked on |
|---|---|---|
| [network-device-discovery](docs/design/proposals/network-device-discovery.md) | EByte UDP probe; USR UDP search packet (the web-login USR probe is built) |  A fresh capture of each device |
| [network-device-config-editors](docs/design/proposals/network-device-config-editors.md) | WPF/TUI Configure device menu (lists registered editors; the web profile editor already has the section); EByte E810-DTU editor; USR-TCP232-302 editor; spec, guide and a `docs/test/` bench report | The seam is built; the editors need captures and bench time |
| [out-of-process-plugins](docs/design/proposals/out-of-process-plugins.md) | Transport and device-module variants | Design work |
| [message-broker-protocols](docs/design/proposals/message-broker-protocols.md) | A real device or home-automation broker check for MQTT | A real broker |
| [web-tunnel-blazor-frontend](docs/design/proposals/web-tunnel-blazor-frontend.md) | A real device through the page with several browsers | Your setup |
| [de5000-lcr-meter-protocol](docs/design/proposals/de5000-lcr-meter-protocol.md) | Confirm the adapter's GATT profile; run `RealHardwareDe5000Tests` and write the `docs/test/` report | The meter on the bench |
| [ebyte-e810-dtu-config-protocol](docs/design/proposals/ebyte-e810-dtu-config-protocol.md) | Five checklist items (capture and layout confirmation, read-only support first) | A fresh capture of the real unit |
| [bytecc-bt-up01-usb-network-bridge](docs/design/proposals/bytecc-bt-up01-usb-network-bridge.md) | Five checklist items | The hardware |
| [z-wave-support](docs/design/proposals/z-wave-support.md) | Five checklist items | The hardware |

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
- **PCX (and PCL raster) preview in the Stream Monitor (rejected).** WPF has no PCX decoder, so a captured PCX is saved but not
  shown. Options: a small built-in PCX decoder (the format is simple RLE; no dependency) or Magick.NET (large native
  package, but also covers other formats). A PCL raster job needs its `ESC*b<n>W` rows decoded to a bitmap.
  **Decision 2026-10-03: the PCX decoder and the PCL raster preview are both rejected for now** (BMP and TIFF hardcopy already preview); a captured PCX or PCL job stays saved, and PCL still converts through GhostPCL. Revisit only if asked.

