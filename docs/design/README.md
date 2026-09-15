# Design Documents

Living design docs for dev-term, written during the pre-implementation design phase. Each doc covers one concern and cross-links to the others; "Open questions" sections track decisions not yet made.

- [Architecture overview](architecture.md) — core abstractions (session, transport, presenter, pipeline, plugin host, front ends) and how data flows between them.
- [Transport layer](transports.md) — the `ITransport` contract and the initial transports: serial, TCP (client + listener), UDP (target + listener), USB HID (HidSharp), BLE (cross-platform via a pluggable per-OS adapter seam, "BLE Serial"/Nordic UART Service as the common device pattern). Also covers why USBTMC and GPIB aren't in that list — USBTMC needs its own raw-USB transport; most GPIB-via-cheap-adapter hardware needs no new transport at all (Prologix protocol over existing serial/TCP), except NI-protocol clones, which do and aren't recommended.
- [RFC 2217](rfc2217.md) — client (connect to a remote serial port, e.g. `ser2net`) and server (expose a local serial connection to the network) support for the Telnet Com Port Control Option; not yet built. Flags that "RFC2217" in a vendor's marketing copy isn't proof of standard compliance — confirmed against a real target device.
- [Presenters & encodings](presenters.md) — text/numeric-base views; protocol decoders (device and network protocols) that always produce a human-readable text baseline; rendering presenters (HPGL/PostScript/PCL, telemetry plots) with export to SVG/PNG/JPG; composite/channelized decoders for interlaced telemetry; and mappable presenters (external name/label/unit maps, e.g. Modbus register maps) so one decoder plugin serves many devices.
- [Plugin model](plugin-model.md) — how transports and presenters are packaged, discovered, versioned, loaded, and registered into the DI container.
- [Device control modules](device-control-modules.md) — plugins that bundle an outbound control surface (commands/parameters, e.g. for driving test equipment) with telemetry decode/present/plot for the response, composed from the existing transport/presenter contracts.
- [Front ends](frontends.md) — the console app (CLI + TUI modes) and the WPF GUI app, and how they share the core engine.
- [Platform, hosting & configuration](platform.md) — .NET 10+, dependency injection/Generic Host as the composition root, and the Options pattern for settings.

These documents describe intent and direction, not a finished spec — update them as design decisions are made or revisited, rather than letting the code and the docs drift apart.

## Proposals

[`proposals/`](proposals/) holds concrete feature proposals for specific transports, decoders, or
device control modules — narrower and more actionable than the docs above, which describe the
general contracts these proposals build on, and each grounded in an actual piece of target
hardware rather than a hypothetical. Each proposal notes where it came from at the top.

- [SCPI bench instrument control module](proposals/scpi-instrument-control.md) — a device control module for SCPI-compatible bench test equipment (HP 34401A, Rigol DM3058E/DG1022(Z), Korad power supplies), sourced from a planning-stage project in `mwwhited-notes/shared`. Textual rather than binary, needs no new transport (serial + TCP already work), and a candidate first real instance of the declarative command/response schema discussed in [device-control-modules.md](device-control-modules.md) — the best first target of the four.
- [DER EE DE-5000 LCR meter protocol](proposals/de5000-lcr-meter-protocol.md) — a fully-specified, checksum-free 17-byte binary decoder (Cyrustek ES51919 chipset) reverse-engineered by the hobbyist community. Physically unusual: the meter's own optical/IR UART output is bridged to BLE via a custom-built adapter, so this is gated on the not-yet-built BLE Serial transport rather than a decoder gap — but that transport already has real target hardware pushing for it. A second real `ICompositeDecoder`/bitfield candidate now that Favero is shelved.
- [Radex One geiger counter protocol](proposals/radex-one-protocol.md) — a fully-specified binary protocol (framing, checksum, four command types; decoder + control surface) for a USB geiger counter, sourced from a finished reverse-engineering writeup in `mwwhited-notes/shared`. The device turned out to enumerate as USB HID, not a virtual COM port as the source doc assumed — so this is now gated on the not-yet-built USB HID transport, not just decoder work.
- [Favero fencing apparatus protocol](proposals/favero-fencing-protocol.md) — a continuous, unidirectional, bitfield-packed telemetry stream (score/time/lamps/match/penalty cards) from a fencing scoring apparatus, sourced from a production project (ScoreMachine, deployed 2018–present) in `mwwhited-notes/shared`. Would have been a strong first `ICompositeDecoder` candidate, but is **deprioritized** — no hardware access to test against anymore.

## Diagrams

Diagrams are embedded directly in the markdown as fenced ` ```plantuml ` code blocks. Structural/architecture diagrams (system context, containers, components) follow the [C4 model](https://c4model.com) using the standard [C4-PlantUML](https://github.com/plantuml-stdlib/C4-PlantUML) macros, pulled in via `!include https://raw.githubusercontent.com/plantuml-stdlib/C4-PlantUML/master/...` — rendering these requires a PlantUML setup that allows remote `!include`s (the public PlantUML server does; a fully offline renderer needs a local copy of the C4-PlantUML stdlib instead). Sequence diagrams use plain `@startuml`/`@enduml`, and UI wireframes use `@startsalt`/`@endsalt` — neither needs the C4 include.
