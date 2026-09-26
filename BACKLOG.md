# Backlog

Not-yet-started work for dev-term, prioritized where a priority has actually been given, plus
early-stage research not ready to be scoped as backlog. Active/in-progress work lives in
[`TODO.md`](TODO.md) instead, kept lean on purpose — this file is where lower-priority and
not-yet-started work lives so it doesn't add scroll-past overhead to checking on active work.
Completed work is logged by date under `docs/changes/`.

## Backlog (not started)

Prioritized per direction given 2026-09-15: BLE serial is the next transport to build (ahead of
RFC 2217/UDP), since real target hardware exists. USBTMC is newly-scoped, not yet ordered against
the rest.

### Transports

- **BLE transport** (`DevTerm.Transports.Ble`), cross-platform by design via a pluggable per-OS
  adapter seam (Windows via `Windows.Devices.Bluetooth` first; Linux/BlueZ and macOS/CoreBluetooth
  addable later, including as community/self-contributed adapters) — see
  `docs/design/transports.md`'s BLE section for the adapter-contract shape and the "BLE Serial"
  (Nordic UART Service) pattern most hobbyist devices actually use. Target hardware identified:
  a [DER EE DE-5000 LCR meter](docs/design/proposals/de5000-lcr-meter-protocol.md), whose optical
  (IR) UART output is bridged to BLE via a custom adapter already built — unblocks that proposal
  once built. Still need: which GATT profile the custom adapter actually exposes (NUS or custom).
- RFC 2217 client (`Rfc2217Transport`, `ITransport`) — connect to a remote serial port (e.g.
  `ser2net`) with full baud/DTR/RTS control over the network. Design done: see
  `docs/design/rfc2217.md`. Build first (server mode depends on the same codec but is a
  differently-shaped bridge, not a transport — build second).
- RFC 2217 server (`Rfc2217ServerBridge`) — expose a local serial connection to the network for a
  remote RFC 2217 client to control. See `docs/design/rfc2217.md`. Note: binds loopback-only by
  default per the security note in that doc.
- UDP transport (target + listener modes). Real target hardware once built:
  [EByte E810-DTU(RS485)](docs/design/proposals/ebyte-e810-dtu-config-protocol.md)'s broadcast
  discovery/config protocol (port 1901) — note the proposal's own byte-count discrepancy needs
  resolving against a fresh capture before implementing, not just the existing notes.

### Plugin architecture, decoders & presenters

- Dynamic plugin loading (`AssemblyLoadContext`, `IPluginModule`, manifest/versioning) per
  `docs/design/plugin-model.md`. Today's built-in transports/presenters are wired by hand in
  `Program.cs`, not actually loaded as plugins yet, despite already using the same contracts.
- Protocol decoders with a human-readable text baseline; composite/channelized decoders;
  mappable presenters.
- Rendering presenters (HPGL/PostScript/PCL, telemetry plots) + export (SVG/PNG/JPG) — the actual
  drawing/rendering half, for the HPGL/PostScript/PCL the Stream Monitor ([proposal](docs/design/proposals/stream-content-detection.md))
  already captures and saves but doesn't draw yet.

### Device control modules & hardware profiles

- Declarative command/response schema for device control modules (send template + response
  pattern, `.ksy` reference for binary layouts via [Kaitai Struct](https://kaitai.io/), an SCPI
  baseline for common bench-instrument commands) — see the new section in
  `docs/design/device-control-modules.md`.
  **Still open:**
  - [DE-5000 LCR meter](docs/design/proposals/de5000-lcr-meter-protocol.md) is gated on the BLE
  transport above (adapter hardware already built). [Radex One](docs/design/proposals/radex-one-protocol.md)'s
  transport dependency (USB HID) is now built, but it still needs its HID report-framing question
  resolved (see that proposal's open questions) before implementing the decoder.
  - [Zoom H4n remote](docs/design/proposals/zoom-h4n-remote-protocol.md) (plain serial via an
  already-built adapter cable, no new transport needed) remains buildable today, like SCPI was.

### Tektronix TDS2024

- **Every `TRIGger:...?` query hangs (never replies) against this specific real TDS2024 unit** —
  real-hardware confirmed 2026-09-25 (`docs/test/2026-09-25-18-57-22.md`): `TRIGger:MAIn:FREQuency?`
  and `TRIGger:STATE?` (a much cheaper status query, ruling out "expensive measurement" as the
  cause) both hung the full step timeout, while every non-`TRIGger` query tried (`*IDN?`, `CH1?`,
  `CH2?`) answered normally, including as the 3rd command in a sequence (ruling out a simple
  "3rd command" positional issue). `tektronix-tds2024.json`'s own `Name` field notes this unit is
  specifically "NOT the TDS2024B" — unconfirmed hypothesis that the `TRIGger` query family needs
  that variant's firmware. `RealHardwareTcpTests`'s TDS2024 test avoids the whole `TRIGger` family
  for now (uses `CH1?`/`CH2?` instead). Not investigated further — needs a packet capture of a
  known-working `TRIGger` query (e.g. from a Tek-provided tool) against this exact unit to compare
  framing, similar to the USBTMC framing bugs below.

### USBTMC

- **DS1102E missing-ZLP at an exact packet boundary (pyvisa-py #472, not reproduced)** — pyvisa-py reports that the
  device omits the terminating zero-length packet when a reply ends exactly on a 64-byte boundary. The rework would
  wait one `ReadTimeoutMs` for it and then raise an error. A normal-mode 600-sample `:WAV:DATA?` (610 bytes plus 10
  padding) never hits a boundary, so this needs a reply that does (a long-memory/RAW-mode read, for example) to check.
  The same issue's other claim ("TransferSize is 10 bytes short") did **not** match this unit: TransferSize was exact and
  the 10 extra bytes were trailing padding, which the rework correctly drops (see the 2026-09-25 bench report).

### Connection Editor

- **Show the hidden connection settings** (DTR, RTS, read/write timeouts, ASCII max line length). The
  fields are generated from `ConnectionEditorViewModel`'s annotations since 2026-09-25, so this is now
  just annotating the view-model properties (and adding the view-model properties where missing).

### WPF layout review follow-ups (from 2026-09-25)

- **Light theme Accent/Warning are below 4.5:1 as text colors** (4.1:1 and 3.3:1; Playback's `[tx]` and
  `[note]` lines). Needs a palette decision covering both front ends and `docs/design/theming.md`; allow-listed
  in `UiLayoutReviewTests` until then.
- **The Manifest Editor preview's fixed-size charts need a sideways scroll at the default 1180px.** Letting
  charts shrink to the column would fix it.
- **Cap field widths on wide windows.** At 1600px, text boxes and combos in Device Profiles and the
  manifest editor stretch across the whole window.
- **The Manifest picker's empty error area leaves ~24px of blank space** above the buttons.
- **Busylight's unlabeled Apply row isn't aligned** with the section label columns above it.
- **The Manifest Editor's pane title repeats its first section header** ("Identity" / "Identity").
- **No review at 125/150% DPI,** and no keyboard-focus-visual review; the layout review runs at 96 DPI only.
- **Not every review PNG was opened by eye:** most large-size captures, SCPI panels other than DS1102E and
  Generic, most manifest-editor node kinds, and the menus in the second theme.

### TUI layout review follow-ups (from 2026-09-25)

- **Control-panel button rows repeat their label** ("Apply: [Apply]", "Custom...: [Custom...]"). It's how
  the label column lines up; a design call.
- **Ctrl+Q in a nested TUI panel or dialog closes that window** rather than quitting the app. Decide which
  it should be.
- **The layout matrix made `DevTerm.Console.Tests` ~2 min** (was ~16 s). Reuse one app per class, or trim
  the matrix to 80x25 plus 200x60.
- **A scrolled form can show a lone button-shadow row** at the viewport's top edge (correct, odd look).
- **The startup editor looks unthemed in legacy conhost** (16-color downgrade of the truecolor theme:
  invisible field backgrounds, faint focus).
- **Busylight's panel says "Not decoding — connect with the matching --presenter"** when opened without a
  structured source; check whether that message suits an output-only device.
- **Not reviewed yet:** the Terminal.Gui file dialogs (Browse, Save As); the manifest editor's New/empty
  state and its "Create panel from commands" hint; Playback, the Stream Monitor and the SCPI panels in Dark;
  the K8055 with live data; the main window's menus while disconnected.

### Forms engine and manifest editor (follow-ups from 2026-09-25)

- **Control panels ignore `VisibleWhen`** and show a `ChoiceStyle.CheckList` as a single choice (a
  dropdown); only the form renderers handle both.
- **The manifest editor can't edit a control's own `VisibleWhen`, and has no undo.**

### Tooling

- **A custom `DevTerm.Analyzers` Roslyn project**, for coding standards that are specific to this
  codebase's own semantics and can't be expressed via `.editorconfig`/StyleCop.Analyzers (see
  `docs/coding-standards.md`, landed 2026-09-16) — e.g. a project-specific rule like "every
  `ITransport` implementation must no-op on an empty write, not throw" (see `CLAUDE.md`'s
  constraints list for why that one matters). Deliberately not built yet: no such rule has actually
  been declared that a generic analyzer can't already cover — build it once one is.

## Research (not backlog-ready)

- [BYTECC BT-UP01 USB-over-network bridge](docs/design/proposals/bytecc-bt-up01-usb-network-bridge.md) —
  **low priority for now**, by choice (2026-09-15): pursuing the "reverse-engineer it directly"
  route (network sniffer + decompiling the vendor client) rather than the boring-but-reliable
  Raspberry-Pi-running-`usbip` fallback, but only once a real capture exists — nothing to act on
  until then. Not a build item yet, unlike everything above: no protocol reverse-engineering has
  been done and none exists publicly. Two cheap checks needed before deciding whether this is even a
  reverse-engineering project at all: does the vendor's own client software just make the remote
  USB device appear local (making it a non-issue for dev-term entirely), and does the box happen to
  already speak the open USB/IP protocol. If it turns out to need real protocol work, it's a
  fundamentally bigger kind of thing than any transport/decoder proposal above — tunneling USB
  itself (enumeration, control/bulk/interrupt transfers), not decoding one device's byte protocol.
