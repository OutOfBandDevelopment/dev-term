# Device Control Modules

## Purpose

Some plugins need to do more than passively decode a stream — they need to actively drive a piece of equipment (set parameters, trigger actions, issue queries) and then decode/present/plot whatever telemetry comes back, as one cohesive unit. This describes that plugin shape, built on top of the existing transport and presenter contracts rather than replacing them.

## Motivating example

Serial-based test equipment: a signal generator, power supply, or similar bench instrument driven by a command set (SCPI-like or vendor-specific) that streams back telemetry/status while running. A user wants one plugin that provides a control surface for the outbound commands and a live decoded/plotted view of the inbound telemetry, instead of wiring generic presenters together by hand every session (which remains possible, and is still the default for one-off/ad hoc use — this is for when a specific instrument warrants a packaged, integrated experience).

## Shape of a device control module

A device control module is a plugin that **composes**, rather than replaces, the existing extension points:

- A **command set** — the outbound side: named commands/parameters (e.g., "Set Voltage" with a numeric parameter and unit; "Trigger" with no parameters; "Query Status" with a decoded reply) that encode to bytes over the session's transport, reusing the same send path presenters already use for sending (`IPresenterInput`, see [presenters.md](presenters.md)).
- One or more **presenters** for the inbound side — typically a protocol/telemetry decoder (with its human-readable text baseline) and a rendering presenter for a live telemetry plot — exactly the presenter types already described in presenters.md, just bundled with the command set instead of assembled ad hoc.
- A **control surface declaration** — a generic, declarative description of the module's commands/parameters (name, type, range/enum/unit, grouping) that front ends render as an actual control panel (buttons, sliders, dropdowns, numeric fields) without the module hand-building UI per front end. This is the control-surface analog of how composite decoders declare channels generically, and mappable presenters declare their mapping schema generically (see [presenters.md](presenters.md)): the module describes *what* controls exist, the front end decides *how* to draw them.

```plantuml
@startuml
!include https://raw.githubusercontent.com/plantuml-stdlib/C4-PlantUML/master/C4_Component.puml

Person(user, "User", "WPF panel / TUI form / CLI flags")

Container_Boundary(module, "Device Control Module (plugin)") {
  Component(surface, "Control Surface", "IControlSurface", "Commands / parameters")
  Component(decoder, "Telemetry Decoder", "IPresenter", "Human-readable text baseline")
  Component(plot, "Telemetry Plot", "IPresenter (rendering)", "Live chart + export")
}

Container(transport, "Session / Transport", "ITransport", "Serial or other")

Rel(user, surface, "Invokes command + args")
Rel(surface, transport, "Encoded bytes out")
Rel(transport, decoder, "Raw bytes in")
Rel(decoder, plot, "Decoded values")
Rel(plot, user, "Live view / export (SVG, PNG/JPG)")
Rel(decoder, user, "Human-readable text baseline")

SHOW_LEGEND()
@enduml
```

## Contract shape (conceptual)

- `IControlSurface` — declares the module's commands/parameters (metadata only: name, type, constraints, grouping) and a way to invoke a command with arguments, which encodes to bytes and sends over the session's transport. Front ends use this metadata to render an appropriate control panel/form generically, the same way they already render `IMappable`/`ICompositeDecoder` metadata generically.
- A device control module registers an `IControlSurface` alongside one or more `IPresenter`s under one plugin manifest entry (see [plugin-model.md](plugin-model.md)), but each half is still just an ordinary DI-registered service — nothing about the core pipeline needs to know "this is a bundled module" versus independently chosen pieces.

## Relationship to transports

A device control module doesn't introduce a new transport — it rides on whatever `ITransport` the session is already using (most commonly serial for bench equipment, but nothing prevents TCP/LXI-style instruments — see [transports.md](transports.md)). The module is transport-agnostic in the same way protocol decoders are.

## Front-end rendering

- **WPF** — a generic control-panel view driven by the `IControlSurface` metadata (sliders/dropdowns/buttons), alongside the module's live telemetry plot/decoded view in the same window.
- **Console (TUI)** — a text-based form (labelled fields, a command palette) for the same commands, degrading gracefully like other rendering presenters (see [frontends.md](frontends.md)).
- **Console (CLI)** — commands become scriptable flags/subcommands (e.g., `dev-term send --command set-voltage --value 5`), so instrument control is automatable the same way telemetry export already is.

## Declarative command/response schema (candidate direction for the "assembled declaratively" question below)

For simple query/response devices (most bench gear — a command string in, a formatted response string back, e.g. this project's own test device answering `ID?\r` with `ID TEK/2230,V81.1,VERS:14;`), a full code plugin is more than necessary. The candidate shape is a small, dev-term-specific schema — not a general-purpose external DSL — describing per command: its name, parameters (name/type/range/unit), the byte template to send, and how to recognize/parse the response (a literal pattern, a delimiter-based split, or, for genuinely binary responses, a reference to a [Kaitai Struct](https://kaitai.io/) (`.ksy`) definition). This reuses the mapping-file precedent already established in [presenters.md](presenters.md) (raw key → name/attributes) rather than inventing a second, unrelated data format:

- **Kaitai Struct** is the right tool specifically for *binary* response layouts (byte-level fields, conditionals, repeats, bit widths) — it's a mature, cross-language DSL with a C# code-generation target and a web IDE that overlays the parsed structure on a real captured hex dump, useful for reverse-engineering an unfamiliar binary protocol from a capture. It has no concept of *sending* a command, though — it's read/parse-only, so it only ever covers the response half.
- For plain ASCII query/response gear, a heavyweight external DSL is unwarranted; a lightweight dev-term-owned schema (send template + response pattern) covers it without a new dependency.
- **SCPI** (Standard Commands for Programmable Instruments) is worth a built-in baseline, not a DSL but a *convention*: most bench instruments answer a common command subset (`*IDN?`, `*RST`, `*CLS`, `*OPC?`) regardless of vendor, so a generic "SCPI baseline" control surface/decoder could work across many devices with zero per-device authoring, falling back to a device-specific schema/plugin only for the vendor-specific command set beyond that baseline.

## Open questions

- How rich the control-surface metadata needs to be (flat parameter list vs. grouped/paged forms, conditional/interlocked parameters).
- Whether commands can declare an expected reply pattern (request/response pairing) so a "Query Status" command can show its answer inline, versus everything staying async/stream-oriented like the rest of the pipeline.
- Whether device control modules can be assembled declaratively (command set + wiring described as data, akin to the mapping files in presenters.md) for simple instruments, reserving a full code plugin for ones needing custom logic — see the candidate direction above (a dev-term-specific schema, with Kaitai Struct as the binary-layout piece and an SCPI baseline as a zero-authoring fallback).
- Safety/interlock concerns specific to controlling real equipment (e.g., confirming a destructive command, rate-limiting) — a core concern, or left to each module?
