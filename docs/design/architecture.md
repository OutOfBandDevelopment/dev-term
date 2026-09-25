# Architecture Overview

## Purpose

Describes the high-level architecture of dev-term: the core abstractions, how data flows from a device to a screen (and back), and how transports/presenters/front ends stay independent of each other.

## Design principles

- Separate the three concerns cleanly: **transport** (getting bytes to/from a device), **interpretation** (turning bytes into something meaningful), and **presentation** (showing it to the user, in whichever front end they're using).
- Everything a device- or protocol-specific extension point (transports, presenters/decoders) is a plugin against a small, stable contract — the core never has device- or protocol-specific knowledge baked in.
- Core is UI-agnostic and transport-agnostic; it knows about `ITransport` and `IPresenter`, never about "the serial transport" or "the HPGL decoder" specifically.
- Async/streaming first — devices produce bytes at arbitrary times, not in request/response lockstep, and byte-level and message-level views must both stay live.
- Composition is standard .NET dependency injection, all the way down: the core, every plugin, and both front ends are wired together through `Microsoft.Extensions.DependencyInjection`/Generic Host, not through ad hoc `new`s or service locators. See [platform.md](platform.md).

## Core concepts

### Session

A `Session` binds one `ITransport` instance to a pipeline of presenters for a single logical connection to a device. Multiple sessions can be open concurrently (e.g., watching two serial ports, or a serial port and a TCP bridge, side by side).

**Errors and disconnects (2026-09-25).** A connection can end on its own: the read side fails (an unplugged cable, a reset socket, a presenter throwing while rendering), the device or peer closes it, or a send fails. In each case the session closes itself and raises `Session.Disconnected` once, with the failure or `null` for a clean hang-up. It raises it before a failed `SendAsync` rethrows, so the event is the single place a front end reports a lost connection. It isn't raised for a caller's own `CloseAsync`/`DisposeAsync`. `CloseAsync` is safe to call at any time and never throws for a transport-close failure, and `OpenAsync` reconnects the same session afterward. Open/close/fault are serialized, and a fault from an earlier connection is ignored once a newer one exists.

Every front end builds on that the same way:
- Input the parser can't encode is rejected before anything is sent (`DevTerm.Configuration.TypedInput`), leaving the connection alone.
- A device-side failure disconnects and is reported through `Disconnected` (`ConnectionErrorMessages.ForDisconnect`).
- The user resumes from there: File > Connect in the TUI/WPF, or simply the next typed line in the CLI.
- A failed *startup* connect opens the TUI/WPF disconnected instead of exiting.

### Transport

`ITransport` — abstraction over a byte- or message-oriented, full-duplex (or receive-only) connection to a device: connection lifecycle (discover/open/close), device-specific configuration, and streaming bytes in and out. See [transports.md](transports.md).

### Presenter

`IPresenter` — turns a raw byte stream into a viewable (and, where it makes sense, editable/sendable) representation. Presenters range from simple text/numeric-base views to stateful protocol decoders to renderers that produce graphics and export files. See [presenters.md](presenters.md).

### Pipeline

A session's pipeline is a chain of presenters/decoders that a raw byte stream flows through, fanning out to one or more simultaneous views — e.g., raw hex and a decoded protocol view of the same stream at once, or a byte-unframer (SLIP/COBS) feeding an inner decoder.

The pipeline isn't always a straight line: a **composite/channelized decoder** can demultiplex a single stream into several named channels (split by a header/discriminator byte, a time slot, or similar framing), each of which is then handed to its own sub-presenter — which might itself be a different text encoding, a different numeric base, or another nested decoder. See [presenters.md](presenters.md) for detail; this is the model for interlaced telemetry where different fields of the same frame carry differently-encoded data.

### Device control modules

Not every plugin is passive. A **device control module** composes a control surface (outbound commands/parameters that encode to bytes) with one or more presenters (typically a telemetry decoder and a rendering/plot presenter for the response) into one packaged plugin for driving and monitoring a specific piece of equipment — e.g., serial-based test equipment. It's built entirely out of the existing transport/presenter contracts rather than a separate pipeline mechanism. See [device-control-modules.md](device-control-modules.md).

### Plugin host

Transports and presenters are discovered and loaded as plugins against versioned contracts, registering themselves into the shared DI container rather than being compiled into the core or the front ends. See [plugin-model.md](plugin-model.md).

### Front ends

Two deployable applications sharing the same core engine and DI composition: a console app (offering both CLI and TUI modes) and a WPF app (GUI). They share sessions, transports, and presenter plugins, differing only in how they render and how the user interacts. See [frontends.md](frontends.md) and [platform.md](platform.md).

## Architecture diagrams (C4)

Structural diagrams in these docs follow the [C4 model](https://c4model.com) using the standard [C4-PlantUML](https://github.com/plantuml-stdlib/C4-PlantUML) macros (`Person`, `System`, `Container`, `Component`, `Rel`, ...), included from the stdlib rather than drawn as plain boxes.

### System context

```plantuml
@startuml
!include https://raw.githubusercontent.com/plantuml-stdlib/C4-PlantUML/master/C4_Context.puml

Person(developer, "Developer", "Debugs, drives, and monitors hardware/network devices")

System(devterm, "dev-term", "Modular, extensible development terminal")

System_Ext(serialDevice, "Serial Device", "UART-connected board/instrument")
System_Ext(networkDevice, "Network Device / Service", "TCP/UDP endpoint")
System_Ext(hidDevice, "USB HID Device", "Control/debug interface over HID reports")
System_Ext(bleDevice, "BLE Peripheral", "GATT-based device")
System_Ext(testEquipment, "Serial Test Equipment", "Bench instrument driven via a device control module")

Rel(developer, devterm, "Opens sessions, sends commands, views/exports telemetry")
Rel(devterm, serialDevice, "Reads/writes bytes", "Serial/UART")
Rel(devterm, networkDevice, "Reads/writes bytes", "TCP/UDP (client or listener)")
Rel(devterm, hidDevice, "Reads/writes reports", "USB HID")
Rel(devterm, bleDevice, "Reads/writes characteristics", "BLE/GATT")
Rel(devterm, testEquipment, "Sends commands, receives telemetry", "Serial + control surface")

SHOW_LEGEND()
@enduml
```

### Containers

```plantuml
@startuml
!include https://raw.githubusercontent.com/plantuml-stdlib/C4-PlantUML/master/C4_Container.puml

Person(developer, "Developer")

System_Boundary(devterm, "dev-term") {
  Container(consoleApp, "Console App", ".NET 10, Generic Host", "CLI (scriptable) and TUI (full-screen) modes")
  Container(wpfApp, "WPF App", ".NET 10, WPF, Generic Host", "GUI: rendering presenters, plots, device control panels")
  Container(core, "Core Engine", ".NET 10 class library", "Session, Pipeline, Plugin Host, DI composition")
  Container(plugins, "Plugins", ".NET assemblies, DI-registered", "Transports, presenters/decoders, control surfaces")
}

System_Ext(devices, "Devices", "Serial / TCP / UDP / USB HID / BLE / test equipment")

Rel(developer, consoleApp, "Uses", "Terminal")
Rel(developer, wpfApp, "Uses", "Desktop UI")
Rel(consoleApp, core, "Uses", "In-process")
Rel(wpfApp, core, "Uses", "In-process")
Rel(core, plugins, "Discovers, loads, registers into DI")
Rel(plugins, devices, "Reads/writes bytes", "Transport-specific")

SHOW_LEGEND()
@enduml
```

### Core Engine components

```plantuml
@startuml
!include https://raw.githubusercontent.com/plantuml-stdlib/C4-PlantUML/master/C4_Component.puml

Container(consoleApp, "Console App", ".NET 10", "CLI + TUI")
Container(wpfApp, "WPF App", ".NET 10, WPF", "GUI")
Container(plugins, "Plugins", ".NET assemblies", "Transports, presenters, control surfaces")

Container_Boundary(core, "Core Engine") {
  Component(pluginHost, "Plugin Host", "AssemblyLoadContext", "Discovers, validates, and loads plugins; registers them into DI")
  Component(session, "Session", ".NET", "Binds one Transport to a Pipeline for one logical device connection")
  Component(pipeline, "Pipeline", ".NET", "Chains/fans out presenters over a session's byte stream; supports demux for composite decoders")
  Component(options, "Options/Configuration", "Microsoft.Extensions.Options", "Layered settings for transports, plugins, and front ends")
}

Rel(consoleApp, session, "Opens/controls sessions")
Rel(wpfApp, session, "Opens/controls sessions")
Rel(pluginHost, plugins, "Loads, registers into DI")
Rel(session, pipeline, "Feeds raw bytes through")
Rel(pipeline, plugins, "Invokes presenter/transport instances")
Rel(session, options, "Reads configuration")

SHOW_LEGEND()
@enduml
```

## Non-goals (for now)

- Not a full protocol-analysis suite (e.g., not aiming to replace Wireshark for deep packet inspection) — the focus is device development/debugging workflows.
- Not initially targeting mobile platforms.

## Open questions

- Plugin isolation mechanism: in-process (`AssemblyLoadContext`) vs. out-of-process (IPC) plugin hosting.
- Whether presenters/decoders can also *originate* traffic (e.g., simulate a device) or are receive-only in v1.
- Cross-session scripting/automation model for the CLI front end.
