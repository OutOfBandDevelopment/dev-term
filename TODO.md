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
