# Open questions

Every genuinely undecided question across `docs/design/`, `docs/specs/`, `TODO.md`, `BACKLOG.md` and `docs/bugs/`,
collected in one place so none gets lost in a doc nobody is reading. **Last swept: 2026-10-03** (all design docs,
proposals, features, specs, TODO, BACKLOG; `docs/bugs/` has no open reports).

How this file works (see CLAUDE.md "Documentation" and the `docs-sync` / `work-docs-audit` skills):

- The **source doc stays the home of the full reasoning**; this file holds a one-line pointer to it, grouped by what is
  needed to answer it. Don't copy the analysis here.
- A question is **answered** by editing the source doc (strike it through, say what was decided and when), then
  **deleting its line here in the same change**. Resolved questions are not kept here; the decision lives in the source
  doc and `docs/changes/`.
- A **new** open question added to any doc gets a line here in the same change.
- Questions that are really tasks ("needs a capture") belong in `TODO.md`/`BACKLOG.md`; they are listed here only when
  the *answer* changes a design decision.

## Needs your decision

Choices only the project owner can make (scope, direction, priorities).

- **Plugin isolation:** a signing/trust model for third-party plugins; plugins written in other languages. Out-of-process
  hosting is wanted over a named pipe or localhost-only web service (2026-10-03); the shape is not designed. —
  [plugin-model](docs/design/plugin-model.md), [architecture](docs/design/architecture.md)
- **Multi-session tabs:** per-tab versus shared `SendHistory` (a shared timecode across channels is wanted); per-tab
  logging and Stream Monitor; whether closing the last tab closes the window; keyboard shortcuts for New/Close/next/prev
  tab. — [multi-session-ui](docs/design/multi-session-ui.md)
- **RFC 2217 server mode:** low priority; if built, project layout, CLI surface, multi-client policy, and whether a
  TCP listener proxy or virtual serial port (WSL) is the real need. — [rfc2217](docs/design/rfc2217.md)
- **Safety/interlocks:** confirming a destructive command, core or per module (rate limits are decided). —
  [device-control-modules](docs/design/device-control-modules.md)
- **Config namespacing for plugin options** and whether named `--profile <name>` profiles are needed. —
  [platform](docs/design/platform.md)
- **Retention policy:** how `~/.dev-term/captures/` and the Stream Monitor export folder are pruned (age, count or size). — [stream-content-detection](docs/design/features/stream-content-detection.md),
  [stream-monitor spec](docs/specs/stream-monitor.md)
- **Single-file manifest** referencing an external `.ksy`: allowed or not. — [device-manifests](docs/design/device-manifests.md)

## Needs hardware or a capture

Blocked on a real device or a deliberate packet capture; the TODO "Manual review" list tracks the bench side.

- **DE-5000:** the BLE adapter's GATT profile (Nordic UART or custom); live-check the DE-5000, K8055 and Zoom H4n
  `.ksy` layouts. — [de5000](docs/design/proposals/de5000-lcr-meter-protocol.md), TODO Manual review #1
- **EByte E810-DTU:** why `FD01` appears instead of `FD00`; what the "short link switch" byte and trailing 6-byte block
  are; whether RS-422 variants share the format; confirm the corrected 203-byte layout with a fresh capture. Read-only
  support first. — [ebyte-e810-dtu-config-protocol](docs/design/proposals/ebyte-e810-dtu-config-protocol.md)
- **USR-TCP232-302 configuration channel** (setup protocol versus HTTP page) and the **USR UDP search packet**. —
  [network-device-config-editors](docs/design/proposals/network-device-config-editors.md),
  [network-device-discovery](docs/design/proposals/network-device-discovery.md)
- **Which discovery probes answer on the bench:** mDNS, SSDP, EByte broadcast, USR search; one row or several for a
  multi-service hit; subnet choice with several adapters. —
  [network-device-discovery](docs/design/proposals/network-device-discovery.md)
- **BYTECC BT-UP01:** does vendor software still run; does it speak standard `usbip`; what is on the wire; is a
  URB-tunnel USB transport a new kind of thing rather than an `ITransport`? —
  [bytecc-bt-up01](docs/design/proposals/bytecc-bt-up01-usb-network-bridge.md)
- **Z-Wave:** declare the node's command classes or query them; how the `.ksy` runtime wires into a device module. —
  [z-wave-support](docs/design/proposals/z-wave-support.md)
- **LXI:** does any owned instrument have a LAN interface needing VXI-11 (Phase 2 is deliberately not started)? —
  [lxi-support](docs/design/features/lxi-support.md)
- **Kuando Busylight:** why the batch/program-mode write has no visible effect; on/off time units; meaning of the
  poll reply's two strings. — [kuando-busylight-protocol](docs/design/features/kuando-busylight-protocol.md)
- **K8055:** units of the two trailing Set bytes; what command `0x06` does; digital-input bit mapping; the constant
  byte 2 (`0x01` versus `0x03`); PID mask support in `HidTransportOptions`. —
  [velleman-k8055-protocol](docs/design/features/velleman-k8055-protocol.md)
- **NMEA/Earthmate BT-20:** exact HID report framing (decoder strips NULs defensively); gate serial/TCP receivers? —
  [nmea-gps-protocol](docs/design/features/nmea-gps-protocol.md)
- **Radex One:** reserved bytes in the Read Serial/Version reply; generic repeat-3x command mode; polled "live"
  Read Data. — [radex-one-protocol](docs/design/features/radex-one-protocol.md)
- **Tektronix 2230:** whether remote setting is possible at all; whether a full
  command reference exists. The 2230 HP-GL fix is also unverified on hardware (a plot must be started on the scope). —
  [tektronix-2230-protocol](docs/design/features/tektronix-2230-protocol.md)
- **Zoom H4n:** encode per-mode LED blink timing; one shared decoder for the H2n/H4n family? —
  [zoom-h4n-remote-protocol](docs/design/features/zoom-h4n-remote-protocol.md)
- **USBTMC:** do the DG1022/DS1102E need a Zadig swap; is USB488 SRQ/serial-poll worth building; does the Zadig step
  belong in `docs/user-guide/`; GPIB is out of scope. —
  [usbtmc-transport](docs/design/usbtmc-transport.md)
- **Stream Monitor against real hardware:** the DG1062Z BMP capture and the TDS2024 `HARDCopy` output are unverified. —
  [stream-monitor spec](docs/specs/stream-monitor.md)
- **Capture correlation:** more than one simultaneous capture, and a detected-but-unsolicited stream. —
  [stream-content-detection](docs/design/features/stream-content-detection.md)

## Design questions (architecture, schema, model)

Answerable by thinking and a prototype; no owner decision or hardware needed.

- **Presenters:** a standard structured-message model all textual decoders emit; shared drawing/canvas/plot models;
  one mapping-file format; where mappings live; whether raster export is a core service. —
  [presenters](docs/design/presenters.md)
- **Cross-session scripting model for the CLI.** (Presenters may originate traffic: decided 2026-10-03.) —
  [architecture](docs/design/architecture.md)
- **Control modules:** reply correlation for interleaved/unsolicited binary telemetry; a manifest command schema
  declaring `ExpectedResponseFormat`; a general Kaitai-backed binary response schema. —
  [device-control-modules](docs/design/device-control-modules.md),
  [stream-content-detection](docs/design/features/stream-content-detection.md)
- **UI definitions:** an indicator format/unit hint versus a pre-formatted decoder value; nested `Sections`;
  *disabled*/interlocked controls and `VisibleWhen` in the panel renderers. —
  [ui-definitions](docs/design/ui-definitions.md)
- **Format schemas:** a stable hosted `$schema` URL; whether SCPI profiles fold into `DeviceManifest`; confirm the
  polymorphic `UiControl` `oneOf` export. — [format-schema-files](docs/design/features/format-schema-files.md)
- **Transports:** the BLE adapter contract before a second OS backend; how a UDP listener's first datagram maps onto
  `Session`; one session per accepted TCP peer. — [transports](docs/design/transports.md)
- **RFC 2217 client:** whether `NOTIFY-LINESTATE`/`NOTIFY-MODEMSTATE` become presenter-visible. —
  [rfc2217](docs/design/rfc2217.md)
- **Network config editors:** `DevTerm.Devices.*` projects versus manifests with a config UI. —
  [network-device-config-editors](docs/design/proposals/network-device-config-editors.md)
- **Session logging:** adding a note to a log still being recorded; very large logs held in memory; backward seek
  replays from record 0 (needs `IPresenter` snapshots); "skip silence"; edit/delete notes. —
  [session-logging](docs/design/session-logging.md), [playback-window spec](docs/specs/playback-window.md)
- **Theming:** color the TUI Connection Editor's "(not found)" hints; offer a terminal-palette theme; make the TUI's
  `system` theme live. — [theming](docs/design/theming.md)
- **Plugin loading:** options-section collision rules (see Needs your decision), moving built-in decoders into plugin
  folders. — [platform](docs/design/platform.md), TODO item 5

## Known gaps in shipped screens

"Open items" from `docs/specs/`; each is a missing capability, not a contested decision.

- **Connection Editor:** `ManifestName` and `ScpiAutoDetectTimeoutMs` are carried over but not shown. —
  [connection-editor](docs/specs/connection-editor.md)
- **Control panel:** expand/collapse not persisted; a profile's `ManifestName` does not open its panel; charts lack
  hover readout, table view and export; chart sizes are fixed. —
  [device-control-panel](docs/specs/device-control-panel.md)
- **Expression picker:** no picking inside a channel segment or a button's parameter fields; no operators/regex
  helpers or CEL-style extension. — [expression-picker](docs/specs/expression-picker.md)
- **Manifest editor:** `.xml` panel forms and binary `.ksy` layouts are not editable. —
  [manifest-editor](docs/specs/manifest-editor.md)
- **Stream Monitor:** no CLI mode or `--presenter`; one capture at a time; only SCPI commands can
  declare a format. — [stream-monitor](docs/specs/stream-monitor.md)
- **Theme builder:** no delete for saved themes; the TUI role list has no scroll indicator; no manual-use verification
  pass. — [theme-builder](docs/specs/theme-builder.md)
- **TUI main screen:** the "Send as" parser menu is not rebuilt per tab. —
  [tui-main-screen](docs/specs/tui-main-screen.md)
- **Playback window:** the TUI has no seek control beyond Rewind/+10s/End/Step. —
  [playback-window](docs/specs/playback-window.md)

## Open bugs

None. `docs/bugs/` holds only open work and is currently empty (see [docs/bugs/README.md](docs/bugs/README.md)).
