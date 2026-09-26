# Presenters & Encodings

## Purpose

Defines how raw bytes from a device are turned into something a developer can read, act on, and export — and, where applicable, turned back into bytes to send.

## Three categories

### 1. Raw/text & numeric-base presenters

Always available, need no knowledge of the connected device. This is what makes dev-term useful for "just talk to the device" scenarios even before any protocol-specific decoder exists:

- Text encodings: ASCII, UTF-8, UTF-16 (LE/BE), Latin-1, and other codepages as needed.
- Numeric-base views: hexadecimal, decimal, octal, and binary, both for viewing incoming bytes and for composing outgoing bytes (e.g., type `0xFF 0x02` or `11111111 00000010` and send it).

**Implemented:** a text presenter renders whatever arrived in a single read by default (hex/decimal/octal/binary/UTF-8), but a line-oriented presenter (the ASCII presenter) instead *buffers* internally until a line terminator (CR, LF, or CRLF treated as one terminator) or a configurable maximum length is reached, rather than rendering byte-by-byte. This matters in practice: a real serial read routinely delivers one byte at a time, so without buffering, a device's one-line reply renders as one output line per character. The maximum length is configurable (0 means unbounded — wait for a terminator no matter how long the line gets), via the Options pattern like everything else configurable in the app (see [platform.md](platform.md)). This is also why `IPresenter.Render` returns zero or more renderings per call rather than exactly one — see "Presenter contract shape" below.

### 2. Protocol decoders (structured, textual)

Stateful, pluggable parsers that interpret a raw byte stream as a specific device/protocol's framing and message structure (e.g., a custom binary protocol, NMEA 0183, Modbus RTU/TCP, SLIP/COBS-framed streams, or an application-layer network protocol such as MQTT or a custom TCP/UDP wire format). A decoder is transport-agnostic: a Modbus TCP decoder doesn't care whether the bytes arrived over the TCP transport or were replayed from a capture file — it only depends on `ITransport` producing bytes, never on which transport plugin produced them. A decoder:

- Consumes bytes (and/or already-framed messages from an upstream decoder) and produces structured, named messages/fields for display.
- Always produces a **human-readable text rendering** as a baseline — a formatted line or block (e.g., `FC=03 addr=0x0010 value=1024 "PumpSpeed"`) that reads sensibly on its own, not just a bag of raw field/value pairs. Structured (tree/table) or graphical output, where a decoder also provides it, is additive on top of this baseline, not a replacement for it — so even a minimal or partially-understood decoder is useful, and every decoder works reasonably in a plain-text-only front end (e.g., piped CLI output).
- May optionally support encoding structured input back into bytes to send, for interactive protocol testing — though a decoder can be receive-only if that doesn't make sense for the protocol.
- Can be chained: e.g., a COBS/SLIP unframer feeds a higher-level decoder.

### 3. Rendering presenters (graphical output & export)

Some presenters interpret a byte stream as a *drawing* or a *plot* rather than as text/fields, and need to offer a "save this" action, not just a live view. The motivating example is an **HPGL presenter**: it displays the raw HPGL command stream as text (like any protocol decoder), *and* renders the plotter commands to a graphical canvas, *and* lets the user export what's been received as either a vector file (SVG) or a raster image (PNG/JPG).

This category is deliberately broader than one plotter language. It's expected to cover, over time:

- Other page-description / printer command languages — **PostScript**, **PCL**, and similar — which share the same shape as HPGL: a command stream that both displays as text and renders as a page/drawing, exportable as vector (SVG, or a page-native vector format) or raster (PNG/JPG).
- **Telemetry plots** — a presenter that reads a raw numeric stream (e.g., ADC samples, sensor readings) and renders it as a time-series line/scatter chart rather than a static drawing, with the same expectation of exporting a snapshot as a raster image (and potentially the plotted data as a vector/SVG chart too).

Both are "rendering presenters" in the same sense, they just target different renderable shapes (a static vector drawing vs. a live/scrolling chart), so the contract should describe *what kind* of renderable a presenter produces rather than assuming it's always a fixed drawing:

- A presenter declares what representation(s) it produces — plain text, structured tree/table, and/or a renderable (drawing, or plot/chart) — so front ends can pick an appropriate way to display it without needing decoder-specific code. A CLI front end might only surface the text view and the export command; a GUI or TUI-with-graphics front end can show the live canvas/chart too.
- A renderable presenter exposes an **export** capability: one or more output formats (e.g., SVG for vector, PNG/JPG for raster) that the current rendered state can be saved to, independent of which front end triggered it.
- Rendering presenters are still fed by the same raw byte stream as any other presenter, and can sit alongside a plain hex/ASCII view of the same session for cross-checking.

```plantuml
@startuml
!include https://raw.githubusercontent.com/plantuml-stdlib/C4-PlantUML/master/C4_Component.puml

Container_Boundary(pipeline, "Session Pipeline") {
  Component(raw, "Raw Byte Stream", "Session", "Bytes from the transport")
  Component(render, "Rendering Presenter", "Plugin", "HPGL / PostScript / PCL / telemetry plot")
  Component(text, "Text View", "Presenter output", "Human-readable baseline")
  Component(canvas, "Drawing / Plot Canvas", "Presenter output", "Renderable scene")
  Component(svg, "SVG Export", "Exporter", "Vector output")
  Component(raster, "PNG/JPG Export", "Exporter", "Raster output")
}

Rel(raw, render, "Feeds")
Rel(render, text, "Always produces")
Rel(render, canvas, "Where supported")
Rel(canvas, svg, "Exports as")
Rel(canvas, raster, "Exports as")

SHOW_LEGEND()
@enduml
```

### 4. Composite / channelized decoders

Not every device sends one homogeneous stream. Telemetry in particular is often **interlaced**: a single stream (or a single frame) carries multiple channels that are each encoded differently — e.g., a header byte selects which channel follows, or channels are assigned fixed time slots — and one channel's payload might be plain text while another is binary, hex-encoded, decimal, or something else entirely.

A composite decoder handles this by:

- **Demultiplexing** the incoming stream/frame into named channels, keyed by whatever discriminates them for that device (a header/id byte, a fixed time slot, a length-prefixed field, etc.) — this splitting logic is itself device-specific and lives in the composite decoder.
- **Delegating** each channel's payload to its own sub-presenter, reusing the existing presenter set — a channel can be rendered as ASCII text, as hex/decimal/binary, or handed to another nested decoder (which could itself be a composite decoder, for deeply structured frames).
- **Recombining** the per-channel output into one view (e.g., a table of channels-over-time, or a synchronized multi-pane view), so the user sees the whole frame/stream coherently rather than as disjoint fragments.

```plantuml
@startuml
!include https://raw.githubusercontent.com/plantuml-stdlib/C4-PlantUML/master/C4_Component.puml

Container_Boundary(pipeline, "Session Pipeline") {
  Component(raw, "Raw Byte Stream", "Session", "Bytes from the transport")
  Component(composite, "Composite Decoder", "Plugin", "Demultiplexes by header/time-slot")
  Component(ch1, "Channel: ASCII Text", "Sub-presenter")
  Component(ch2, "Channel: Hex/Decimal/Binary", "Sub-presenter")
  Component(ch3, "Channel: Nested Decoder", "Sub-presenter", "May itself be composite")
  Component(combined, "Combined Channel View", "Pipeline output", "Recombined per-channel output")
}

Rel(raw, composite, "Feeds")
Rel(composite, ch1, "Demuxes to")
Rel(composite, ch2, "Demuxes to")
Rel(composite, ch3, "Demuxes to")
Rel(ch1, combined, "Contributes to")
Rel(ch2, combined, "Contributes to")
Rel(ch3, combined, "Contributes to")

SHOW_LEGEND()
@enduml
```

This makes presenter composition recursive by design: a composite decoder is just a presenter that happens to be implemented in terms of other presenters, rather than a separate mechanism bolted on top.

### 5. Mappable presenters (external name/label/unit mapping)

Rather than hardcoding every field name, enum label, unit, or channel meaning inside a compiled decoder, a presenter can be **mappable**: it accepts an external, user-editable mapping that translates raw identifiers/values into human-readable names — without needing a new plugin per device. This is what makes one generic decoder reusable across many similar devices instead of forking a plugin for each:

- A **Modbus** decoder is generic to the protocol, but a *register map* (address → name, e.g. `0x0010 → "PumpSpeed"`, plus optional scale/unit) is what turns raw register numbers into something meaningful for a specific device — supplied as mapping data, not code.
- A **composite/channelized decoder**'s channel map (see above: which byte/time-slot is which channel, and which sub-presenter/encoding applies) is itself a mapping, and can also carry per-channel names and units (e.g., channel 2 → "Temperature (°C)" → decimal presenter).
- Enum/status codes (a mode byte, an error code) map to short human labels the same way (e.g., `0x02 → "Calibrating"`), regardless of which decoder or channel they appear in.
- A rendering presenter can use a mapping too — e.g., an HPGL pen number → a color name/RGB value for on-screen rendering and export.

A mapping is loaded per-session (or per saved device profile) rather than compiled in, so the same decoder plugin serves many devices, and users can build/edit/share a mapping for a new device without writing code. Mappings are plain data (not logic), so they don't need the plugin isolation/versioning machinery that code plugins do — see [plugin-model.md](plugin-model.md).

## Presenter contract shape (conceptual)

- `IPresenter` — byte stream in → zero or more rendered representations out, for display (**implemented**: `Render` returns a list, not a single value — a presenter is free to buffer internally and emit nothing until it has something complete, or several items if more than one boundary arrived in a single read; see the ASCII line-buffering behavior above). Declares its representation kind(s) (text / structured / renderable drawing / renderable plot).
- `IPresenterInput` (where applicable) — user-facing representation in → bytes out, for sending. **Implemented**, and used as the *parser* (send format): a connection profile lists any number of display presenters (`CliOptions.Presenter`, an array) and, separately, one `Parser` naming which `IPresenterInput` encodes typed lines — defaulting to the first presenter, switchable per line in the TUI ("Send as" menu) and WPF ("Send as:" box). The two are independent: a hex-encoding parser can sit alongside ascii+hex display.
- `IExportable` (where applicable) — the presenter's current state can be saved to one or more file formats (e.g., an HPGL or PostScript/PCL presenter offering SVG/PNG/JPG export, a telemetry presenter offering a chart snapshot); front ends surface this uniformly (a "save/export" action) without knowing the specific formats ahead of time — the presenter advertises them.
- `ICompositeDecoder` (where applicable) — declares the set of channels it demultiplexes a stream/frame into, and the (possibly per-channel-configurable) sub-presenter used for each, so a front end can show per-channel structure generically instead of the composite decoder needing custom UI.
- `IMappable` (where applicable) — accepts an external mapping (raw identifier/value → name, and optionally unit/scale/color/sub-presenter) that a front end can present as an editable table, independent of the specific decoder; a decoder that doesn't implement it simply always shows raw identifiers.

## Composability

A single session can run more than one presenter over the same stream at once (e.g., raw hex + ASCII side-by-side + an HPGL render), since debugging often means cross-checking the raw bytes against the interpreted or rendered view. Composite decoders extend this: each channel they expose is, from the pipeline's point of view, just another presenter output that can be viewed, cross-checked, or further decoded.

## Open questions

- Standard structured-message model that all textual protocol decoders emit into, so front ends can render any decoder generically (vs. decoders bringing their own rendering).
- Standard drawing/canvas and plot/chart models that rendering presenters target (so the GUI/TUI/export path is shared, e.g. an internal scene graph that both on-screen views and the SVG/PNG/JPG exporters consume), rather than each rendering presenter drawing and exporting independently.
- A common **mapping file format** (e.g., JSON/YAML) and schema shared across decoder types — a Modbus register map, a composite decoder's channel map, and an HPGL pen-color map are all instances of the same underlying "raw key → name/attributes" mapping concept, and ideally use one format/tool rather than each decoder inventing its own. See [device-control-modules.md](device-control-modules.md)'s declarative command/response schema discussion for a related candidate direction (a dev-term-owned schema for command+response definitions, [Kaitai Struct](https://kaitai.io/) for binary response layouts specifically).
- Where mappings/device profiles live and how they're shared (per-session file, a project-level profile directory, an importable/exportable single file) — and whether a mapping can be scoped to reuse (e.g., the same status-code table referenced from two different decoders).
- Whether raster export (PNG/JPG) is a core service (render the shared scene graph at a given size/DPI) or something each rendering presenter implements itself.
- **Recognizing that a reply *is* renderable content in the first place** (vs. plain text/hex) — either from a declared per-command hint (a device profile's own command definition knows its query returns a bitmap or an HPGL stream) or by sniffing known signatures (HPGL mnemonics, PostScript's `%!` header, PCL escape sequences, common image magic bytes) — **built 2026-09-25 as the Stream Monitor** (phase 1 of the [stream content detection](proposals/stream-content-detection.md) proposal; `DevTerm.Core.StreamContent`, [docs/specs/stream-monitor.md](../specs/stream-monitor.md)): both front ends auto-save each capture under a `{device}_{timestamp}.{ext}` name, and WPF previews the native image formats. Rendering HP-GL/PostScript/PCL captures still needs the rendering presenter above.
- **Stateful presenter lifetime vs. DI registration: resolved 2026-09-25.** A buffering presenter (like ASCII's line accumulator, or the SCPI presenter's pending-query queue) holds state across calls. Every `IPresenter` registration and `PresenterCatalog` itself are now **transient**, so each resolved catalog builds its own presenter instances and one catalog is resolved per session: the host at startup, and `DevTermSessionBuilder` on a profile switch. Two sessions in one process can therefore never interleave partial frames through a shared instance. The rule for callers is to resolve the catalog once per session and pass that instance around. Resolving it again gives a separate, unconnected set, so a control panel must use the catalog belonging to its session, as the TUI/WPF already do. Covered by `ServiceCollectionExtensionsTests.AddDevTermFrontEnd_EachCatalogGetsItsOwnStatefulPresenterInstances`.
