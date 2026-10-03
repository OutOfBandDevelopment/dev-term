# TODO

Active / in-progress work for dev-term. Not-yet-started backlog and research work moved out to
[`BACKLOG.md`](BACKLOG.md) (2026-09-16) to keep this file to what's actually being worked on.
Completed work is logged by date under `docs/changes/`.

## In progress

Nothing in progress. The manifest expression builder, the picker, binary frames, `KsyImporter` and the sample-data generator
all landed (detail in `docs/changes/2026-10-02.md`); what's left is in [`BACKLOG.md`](BACKLOG.md).

## Manual review

Things I can't decide or verify alone. Answer inline after each **Your call:**; I'll act on them and delete the line.

### Bugs

1. **[069](docs/bugs/069-tds2024-laserjet-pcx-hardcopy-no-capture.md): TDS2024 LASERJET and PCX hardcopy give no capture.** To isolate it I'd capture raw bytes with a plain session (no monitor) at 19200 baud, which ties up the scope for ~5 minutes per format. Is it worth it, or close as a scope limitation?
   **Your call:**

### Proposals and backlog: decisions only you can make

2. **Schema files** ([proposal](docs/design/proposals/format-schema-files.md)): the only item that needs no hardware, so I can start now. Open choices: generate at build time or check the schemas in (I'd check them in, with a drift test), and whether the TUI/WPF editors should reference them. Go ahead with those defaults?
   **Your call:**
3. **`.ksy` gaps** (bit fields, variable-length frames, checksums): set aside by you on 2026-10-02. Bit fields unblock the Zoom H4n status `.ksy`. Start, or leave parked?
   **Your call:**
4. **Observability** (OpenTelemetry / app logging): not started, no priority. Build it opt-in behind a flag, or drop it from the backlog?
   **Your call:**
5. **Light TUI theme collapse on 16-color conhost:** fix means either changing the WPF-visible light palette or a TUI-only override. Which, or accept it as is?
   **Your call:**
6. **Plugin loading** (`AssemblyLoadContext`) and **protocol decoders / rendering presenters:** large; do you want any of these next, and in what order?
   **Your call:**
7. **Custom `DevTerm.Analyzers`:** no rule needs it yet. Confirm it stays parked.
   **Your call:**

### Needs hardware or a real capture from you

8. **DE-5000 meter:** adapter GATT profile, `RealHardwareDe5000Tests`, and live-checking the DE-5000, K8055 and Zoom H4n `.ksy` layouts. Blocked on the meter being back on the bench. When?
   **Your call:**
9. **RFC 2217 real server** (`ser2net` or pyserial `rfc2217_server.py`) for client verification, and the server bridge itself. Do you have or want to set up a server? (Previously marked out of scope.)
   **Your call:**
10. **EByte E810 / UDP transport:** the `FD00`/`FD01` byte-count discrepancy needs a fresh deliberate capture of the real unit before any code. Can you take one?
    **Your call:**
11. **LXI:** needs a real LXI instrument on the bench (which one?) before Phase 2; Phase 1 discovery could start without one. Start Phase 1?
    **Your call:**
12. **Z-Wave** (no controller) and **BYTECC BT-UP01** (needs the two cheap checks: does the vendor client make the device look local, does it speak USB/IP): keep, defer, or close each as won't-do?
    **Your call:**

13. **BLE on Linux/macOS:** no machine to test on here. Skip, or do you have one?
    **Your call:**

## In progress

**Message brokers and the web tunnel (unlocked 2026-10-02).** Plan: MQTT first (`DevTerm.Transports.Mqtt`, MQTTnet 5.x, topic-prefixed text convention so no `Session`/`Pipeline` change at first, tested against an in-process fake), then the `DevTerm.Web` host (loopback-only bind by default, Blazor Server, auth and TLS designed before exposing). AMQP and STOMP wait for a real target. Proposals: [message-broker-protocols](docs/design/proposals/message-broker-protocols.md), [web-tunnel-blazor-frontend](docs/design/proposals/web-tunnel-blazor-frontend.md). Nothing built yet.

## Backlog / research

Not-yet-started work, prioritization notes, and early-stage research now live in
[`BACKLOG.md`](BACKLOG.md), including the design-level items from the Architect's 2026-09-23 notes.
