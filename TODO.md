# TODO

Active / in-progress work for dev-term. Completed work is logged by date under `docs/changes/`.

## In progress

- [ ] Rework the transport read path to use `System.IO.Pipelines` (`PipeReader`/`PipeWriter`)
      end-to-end instead of per-chunk `byte[]` copies.
  - [ ] `DevTerm.Core`: `ITransport.Input` as `PipeReader`, `IPresenter.Render(ReadOnlySequence<byte>)`,
        a shared `StreamToPipePump`, `Session` read loop pumping over `PipeReader`.
  - [ ] `DevTerm.Presenters.Text`: update all presenters to `ReadOnlySequence<byte>` with a
        zero-copy single-segment fast path.
  - [ ] `DevTerm.Transports.Serial`: simplify `ISerialPort`/`SerialTransport` to stream + pump.
  - [ ] `DevTerm.Transports.Tcp`: simplify `ITcpConnection`/`TcpTransport` to stream + pump.
  - [ ] Update all affected unit tests (Pipe-based simulation instead of event-raising).

## Backlog (not started)

- UDP transport (target + listener modes).
- USB HID transport.
- BLE transport.
- Dynamic plugin loading (`AssemblyLoadContext`, `IPluginModule`, manifest/versioning) per
  `docs/design/plugin-model.md`.
- Protocol decoders with a human-readable text baseline; composite/channelized decoders;
  mappable presenters.
- Rendering presenters (HPGL/PostScript/PCL, telemetry plots) + export (SVG/PNG/JPG).
- Device control modules.
- TUI mode (console app).
- WPF GUI app.
