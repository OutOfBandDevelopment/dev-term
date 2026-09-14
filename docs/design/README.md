# Design Documents

Living design docs for dev-term, written during the pre-implementation design phase. Each doc covers one concern and cross-links to the others; "Open questions" sections track decisions not yet made.

- [Architecture overview](architecture.md) — core abstractions (session, transport, presenter, pipeline, plugin host, front ends) and how data flows between them.
- [Transport layer](transports.md) — the `ITransport` contract and the initial transports: serial, TCP (client + listener), UDP (target + listener), USB HID, BLE.
- [Presenters & encodings](presenters.md) — text/numeric-base views; protocol decoders (device and network protocols) that always produce a human-readable text baseline; rendering presenters (HPGL/PostScript/PCL, telemetry plots) with export to SVG/PNG/JPG; composite/channelized decoders for interlaced telemetry; and mappable presenters (external name/label/unit maps, e.g. Modbus register maps) so one decoder plugin serves many devices.
- [Plugin model](plugin-model.md) — how transports and presenters are packaged, discovered, versioned, loaded, and registered into the DI container.
- [Device control modules](device-control-modules.md) — plugins that bundle an outbound control surface (commands/parameters, e.g. for driving test equipment) with telemetry decode/present/plot for the response, composed from the existing transport/presenter contracts.
- [Front ends](frontends.md) — the console app (CLI + TUI modes) and the WPF GUI app, and how they share the core engine.
- [Platform, hosting & configuration](platform.md) — .NET 10+, dependency injection/Generic Host as the composition root, and the Options pattern for settings.

These documents describe intent and direction, not a finished spec — update them as design decisions are made or revisited, rather than letting the code and the docs drift apart.

## Diagrams

Diagrams are embedded directly in the markdown as fenced ` ```plantuml ` code blocks. Structural/architecture diagrams (system context, containers, components) follow the [C4 model](https://c4model.com) using the standard [C4-PlantUML](https://github.com/plantuml-stdlib/C4-PlantUML) macros, pulled in via `!include https://raw.githubusercontent.com/plantuml-stdlib/C4-PlantUML/master/...` — rendering these requires a PlantUML setup that allows remote `!include`s (the public PlantUML server does; a fully offline renderer needs a local copy of the C4-PlantUML stdlib instead). Sequence diagrams use plain `@startuml`/`@enduml`, and UI wireframes use `@startsalt`/`@endsalt` — neither needs the C4 include.
