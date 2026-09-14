# Design Documents

Living design docs for dev-term, written during the pre-implementation design phase. Each doc covers one concern and cross-links to the others; "Open questions" sections track decisions not yet made.

- [Architecture overview](architecture.md) — core abstractions (session, transport, presenter, pipeline, plugin host, front ends) and how data flows between them.
- [Transport layer](transports.md) — the `ITransport` contract and the initial transports: serial, TCP (client + listener), UDP (target + listener), USB HID, BLE.
- [Presenters & encodings](presenters.md) — text/numeric-base views; protocol decoders (device and network protocols) that always produce a human-readable text baseline; rendering presenters (HPGL/PostScript/PCL, telemetry plots) with export to SVG/PNG/JPG; composite/channelized decoders for interlaced telemetry; and mappable presenters (external name/label/unit maps, e.g. Modbus register maps) so one decoder plugin serves many devices.
- [Plugin model](plugin-model.md) — how transports and presenters are packaged, discovered, versioned, loaded, and registered into the DI container.
- [Front ends](frontends.md) — the console app (CLI + TUI modes) and the WPF GUI app, and how they share the core engine.
- [Platform, hosting & configuration](platform.md) — .NET 10+, dependency injection/Generic Host as the composition root, and the Options pattern for settings.

These documents describe intent and direction, not a finished spec — update them as design decisions are made or revisited, rather than letting the code and the docs drift apart.

## Diagrams

Diagrams are embedded directly in the markdown as fenced ` ```plantuml ` code blocks (`@startuml`/`@enduml` for structure/sequence diagrams, `@startsalt`/`@endsalt` for UI wireframes). View them with a PlantUML-aware Markdown renderer/plugin, or through the PlantUML online server, if your viewer doesn't render them inline.
