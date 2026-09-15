# TODO

Active / in-progress work for dev-term. Completed work is logged by date under `docs/changes/`.

## In progress

- **TUI + WPF front ends, stubbing out.** Plan: extract the shared CLI-config pieces (`CliOptions`,
  `CliOptionsValidator`, `DevTermConfiguration`, `ConnectionErrorMessages`, `LineEnding`) out of
  `DevTerm.Console` into the new `DevTerm.Configuration` project so both front ends bind the same
  profiles/env-vars/CLI-args config, rather than duplicating it — this is what makes a saved
  `appsettings.Local.json` profile usable from either front end. Then: add a TUI mode to
  `DevTerm.Console` (candidate library: Terminal.Gui v2), and scaffold a new `DevTerm.Wpf` project
  reusing the same `DevTerm.Configuration` bootstrapping. Status so far: `DevTerm.Configuration` and
  `DevTerm.Configuration.Tests` projects scaffolded and wired into the solution, but the actual file
  moves/DI wiring/TUI/WPF code haven't landed yet.

## Backlog (not started)

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
- USB HID transport.
- BLE transport.
- Dynamic plugin loading (`AssemblyLoadContext`, `IPluginModule`, manifest/versioning) per
  `docs/design/plugin-model.md`. Today's built-in transports/presenters are wired by hand in
  `Program.cs`, not actually loaded as plugins yet, despite already using the same contracts.
- Protocol decoders with a human-readable text baseline; composite/channelized decoders;
  mappable presenters.
- Rendering presenters (HPGL/PostScript/PCL, telemetry plots) + export (SVG/PNG/JPG).
- Device control modules (control surface + telemetry decode/plot) — see the declarative-schema
  item above for the command/response definition piece specifically. Three concrete, real-hardware
  proposals ready to build against, in rough suggested order: [Radex One](docs/design/proposals/radex-one-protocol.md)
  (fully-specified binary protocol, good first decoder), [SCPI instrument control](docs/design/proposals/scpi-instrument-control.md)
  (textual, first real declarative-schema candidate), [Favero fencing protocol](docs/design/proposals/favero-fencing-protocol.md)
  (continuous bitfield-packed stream, first real `ICompositeDecoder` candidate).
- Resolve the stateful-presenter-vs-DI-singleton lifetime issue noted in
  `docs/design/presenters.md` before TUI/WPF support more than one concurrent session — today's
  single-session-per-process CLI usage doesn't hit it, but a multi-session front end would.
