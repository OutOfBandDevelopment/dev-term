# dev-term

A modular, extensible development terminal for talking to devices.

`dev-term` is a developer tool for connecting to, observing, and driving hardware and network devices during embedded/firmware/protocol development. It replaces the usual pile of one-off serial terminals, packet sniffers, and hex dumpers with a single core engine that different **transports**, **presenters**, and **protocol decoders** plug into.

## Why

Working on embedded or protocol-level code usually means switching between several ad-hoc tools depending on the connection type (serial vs. TCP vs. BLE) and the shape of the data (raw ASCII vs. binary vs. a specific device protocol). `dev-term` aims to be the one tool that covers all of that, by keeping the connection layer, the data-interpretation layer, and the UI layer independent and pluggable.

## Goals

- **Modular transports** — serial/UART, TCP (client and listener), USB HID, USBTMC, BLE, RFC 2217, VXI-11 and message brokers out of the box, with UDP planned, with a plugin contract so new transports (CAN, SPI/I2C bridges, custom sockets, etc.) can be added without touching the core.
- **Modular presenters** — view and send raw bytes as ASCII, UTF-8/UTF-16, and other text encodings, or as hexadecimal, decimal, octal, and binary; a decoder plugin contract for protocol-specific decoders (device protocols, network protocols) that always produce a human-readable text rendering; rendering presenters that turn a stream into a drawing or plot (e.g., HPGL, PostScript, PCL, telemetry plots) and export it as SVG/PNG/JPG; composite/channelized decoders for interlaced telemetry where different fields of the same frame are encoded differently (text, binary, hex, decimal, ...); and mappable presenters that use externally-supplied name/label/unit maps so one decoder serves many devices.
- **Device control modules** — plugins that don't just decode a stream but actively drive equipment: a control surface for outbound commands/parameters (e.g., controlling serial-based test equipment) bundled with telemetry decode/present/plot for whatever comes back, built from the same transport/presenter contracts as everything else.
- **Two front ends, one engine** — a console app providing both a scriptable CLI and a full-screen TUI, plus a WPF desktop GUI for richer graphical views (renderers, plots, device control panels) — both built on the same core engine via dependency injection.
- **Extensibility first** — transports, presenters, decoders, and control modules are all plugins against the same core contracts; adding support for a new device or protocol should not require forking the app.

## Technology

.NET 10+, composed throughout with `Microsoft.Extensions.DependencyInjection`/Generic Host, with settings modeled via the `Microsoft.Extensions.Options` pattern. The GUI is WPF (Windows-only); the console app (CLI + TUI) has no such constraint. See [platform.md](docs/design/platform.md) for detail.

## Status

Actively under development; most of what the design docs describe is built. All three front ends work
(scriptable CLI, full-screen TUI, WPF GUI), with a loopback transport so you can try them with no hardware.

- **Transports:** serial, TCP, USB HID, USBTMC, BLE (Windows), RFC 2217 client, VXI-11 (LXI instruments),
  MQTT/AMQP/STOMP brokers, and loopback. UDP, an RFC 2217 server, and BLE on Linux/macOS are not built.
- **Presenters and devices:** text/numeric-base presenters, SCPI, NMEA, and device modules for the K8055,
  Busylight, DE-5000, RadexOne and Zoom H4n; no-code device manifests (JSON/XML, `.ksy` import) with generic control panels.
- **Around the edges:** session logging and playback, a Stream Monitor that captures and previews device output,
  out-of-process and in-process plugins, a loopback-only web host, and OpenTelemetry export.
- **Verified on real hardware:** serial and TCP bench instruments (scopes, a DMM, supplies), HID, USBTMC, BLE, RFC 2217
  against ser2net, and VXI-11 against a Rigol DG1062Z. Each bench session is recorded under [`docs/test/`](docs/test/).

See [`TODO.md`](TODO.md) for work in progress, [`BACKLOG.md`](BACKLOG.md) for what's not started,
[`OPEN_QUESTIONS.md`](OPEN_QUESTIONS.md) for every undecided question, and `docs/changes/` for a dated log of completed work.

## Documentation

- [User guide](docs/user-guide/README.md) — task-oriented walkthroughs with real captured screens
- [Screen specs](docs/specs/README.md) — the precise fields and actions of each screen
- [Design docs index](docs/design/README.md)
- [Architecture overview](docs/design/architecture.md)
- [Transport layer](docs/design/transports.md), [RFC 2217](docs/design/rfc2217.md), [VXI-11](docs/design/vxi11-transport.md)
- [Presenters & encodings](docs/design/presenters.md)
- [Plugin model](docs/design/plugin-model.md)
- [Device control modules](docs/design/device-control-modules.md)
- [Front ends (console CLI/TUI + WPF GUI)](docs/design/frontends.md)
- [Platform, hosting & configuration](docs/design/platform.md)
- [Coding standards](docs/coding-standards.md) — declared, enforced rules (`.editorconfig`/analyzers), not a style guide written in the abstract

## License

[MIT NON-AI License](LICENSE) — MIT-style, with an added restriction against using this software, or its derivatives, in training or improving machine learning / AI models.
