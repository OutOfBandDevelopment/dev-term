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

- **Message brokers:** is MQTT alone enough, or are AMQP and STOMP wanted too? Is there a real broker/device to
  build against first? — [message-broker-protocols](docs/design/proposals/message-broker-protocols.md)
  - I do not have any hardware that support sthis as this time.  I wanted the ability to match on devicves and sent out events.  its mainly a PoC if possible... as long as a message can be detected even from loopback and published to these protocols then this feature can be considered complete. 
  - I guess at the same time having an inbound message beable to trigger a device action would be great too... some wya to map events and message in and out would be nice.  this can be tested over loopback as long as other profiles could be interfaced in the future... do not need to test with actual hardware as that will occure if/when the neede arises
- **Does topic-addressed traffic justify a `Pipeline`/`Session` change** (per-topic presenter routing)? —
  [message-broker-protocols](docs/design/proposals/message-broker-protocols.md)
  - it would be nice to be able to add/remove presenters without having to reconnect to the devices as well as start receving/processing events from the message broker into actual devices... this is more like a routing proxy than a device interface
- **Plugin isolation:** in-process `AssemblyLoadContext` (built) versus out-of-process hosting; a signing/trust model
  for third-party plugins; plugins written in other languages. — [plugin-model](docs/design/plugin-model.md),
  [architecture](docs/design/architecture.md)
  - this would be really cool to support cross process... either a namedpipe or a localhost only webservice would be the perfered IPC channel.
- **Is GPIB ever in scope** (Prologix adapters, or only via a serial/TCP bridge)? —
  [scpi-instrument-control](docs/design/features/scpi-instrument-control.md)
  - while some of my devices have this interface I do not have the hardware to interact over this interface so at this time it can be removed from development... if it ever comes up it can be added back later.
- **Multi-session tabs:** per-tab versus shared `SendHistory`; per-tab logging and Stream Monitor; whether closing the
  last tab closes the window (recommendations are in the doc, none confirmed); keyboard shortcuts for
  New/Close/next/prev tab. — [multi-session-ui](docs/design/multi-session-ui.md)
  - havign the abiltiy to sync history for multiuple channels would be useful... it could be as simple as a unified timecode even just ticks from the clock when message as posted 
- **RFC 2217 server mode:** project layout, CLI surface, multi-client policy; whether a vendor-specific PUSR
  transport is worth building at all. — [rfc2217](docs/design/rfc2217.md)
  - I dont know if this is really needed ... we can check and see if the ebyte also supports this protocols but having the abilty to connect to virtual serial devices in places like wsl may be handy though having a TCP listern proxy would be good too.
- **Web host:** Blazor Server versus WebAssembly (for when Blazor arrives), and whether a session-discovery/attach
  list is ever wanted. Multi-viewer semantics were decided 2026-10-02. —
  [web-tunnel-blazor-frontend](docs/design/proposals/web-tunnel-blazor-frontend.md)
  - I dont understand what you are trying to ask?  yes the blazor interface should work like the wpf and TUI interfaces
- **Safety/interlocks for real equipment** (confirm a destructive command, rate limits): core concern or per module? —
  [device-control-modules](docs/design/device-control-modules.md)
  - should provbably add read/write rate limits. that would make sense
- **Profile switching mid-session:** confirm before tearing down a live connection, now that dirty-field
  confirmation exists? — [connection-profiles](docs/design/connection-profiles.md)
  - yes
- **Does the manifest/profile path validate `ManifestPath`** at save, list, or connect time? —
  [connection-profiles](docs/design/connection-profiles.md)
  - when it mases sense.
- **Reconnect/retry policy:** core or per transport? — [transports](docs/design/transports.md)
- **Config namespacing for plugin options** and whether named `--profile <name>` profiles are needed. —
  [platform](docs/design/platform.md)
  - timeout and retry should probably be configurable
- **Retention/cleanup:** `~/.dev-term/captures/` and the Stream Monitor export folder are never pruned. —
  [stream-content-detection](docs/design/features/stream-content-detection.md),
  [stream-monitor spec](docs/specs/stream-monitor.md)
  - that should be resolved... it would also be nice to have the option to enumerate files that are already on disk
- **Stream-content-detection order:** ship the external-tool/web converters ahead of the internal HP-GL/PostScript
  renderer? — [stream-content-detection](docs/design/features/stream-content-detection.md)
  - web converters should be removed as again a script could front them if ever needed later.  I dont think any converted as requied out of the box at this time.  it would be nice to make it so the hpgl to svg could be an automatic/default conversion optinally 
- **Shared state between front ends** (start in console, attach from WPF) and how much of a rendering presenter the
  TUI should approximate. — [frontends](docs/design/frontends.md)
  - tui should do its best but something like graphic rendering will never be possible.
- **Where manifest zip extracts live** (today a never-cleaned temp folder; a hash-keyed per-user cache would fix it)
  and whether a single-file manifest may reference an external `.ksy`. —
  [device-manifests](docs/design/device-manifests.md)
  - it should be prompted to the user and maybe defaulted to a folder under the applicaiton home folder
- **Rejected fencing protocols:** relative order against Radex One, and whether a live scoreboard renderer is wanted
  (moot while they stay rejected). — [favero](docs/design/rejected/favero-fencing-protocol.md),
  [saint-george](docs/design/rejected/saint-george-fencing-protocol.md)
  - these are rejected and not important... I do not have hardware to test... these are historical only as this point.

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
- **Tektronix 2230:** RS-232 versus GPIB on the owned units; whether remote setting is possible at all; whether a full
  command reference exists. The 2230 HP-GL fix is also unverified on hardware (a plot must be started on the scope). —
  [tektronix-2230-protocol](docs/design/features/tektronix-2230-protocol.md)
- **Zoom H4n:** encode per-mode LED blink timing; one shared decoder for the H2n/H4n family? —
  [zoom-h4n-remote-protocol](docs/design/features/zoom-h4n-remote-protocol.md)
- **USBTMC:** do the DG1022/DS1102E need a Zadig swap; is USB488 SRQ/serial-poll worth building; does the Zadig step
  belong in `docs/user-guide/`; code shared with a future Prologix layer. —
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
- **Can presenters originate traffic** (simulate a device) or are they receive-only? Cross-session scripting model for
  the CLI. — [architecture](docs/design/architecture.md)
  - it would be nice if a presenter could create data to send back in as a simulation or even jsut a virtual device of its own.
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
- **Stream Monitor:** TUI cannot draw SVG; no CLI mode or `--presenter`; one capture at a time; only SCPI commands can
  declare a format. — [stream-monitor](docs/specs/stream-monitor.md)
- **Theme builder:** no delete for saved themes; the TUI role list has no scroll indicator; no manual-use verification
  pass. — [theme-builder](docs/specs/theme-builder.md)
- **TUI main screen:** the "Send as" parser menu is not rebuilt per tab. —
  [tui-main-screen](docs/specs/tui-main-screen.md)
- **Playback window:** the TUI has no seek control beyond Rewind/+10s/End/Step. —
  [playback-window](docs/specs/playback-window.md)

## Open bugs

None. `docs/bugs/` holds only open work and is currently empty (see [docs/bugs/README.md](docs/bugs/README.md)).
