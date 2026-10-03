# TODO

Active / in-progress work for dev-term. Not-yet-started backlog and research work moved out to
[`BACKLOG.md`](BACKLOG.md) (2026-09-16) to keep this file to what's actually being worked on.
Completed work is logged by date under `docs/changes/`.

## In progress

Queue from your 2026-10-02 answers, in the order I'm taking them. Nothing below is built yet.

1. **Message brokers and the web tunnel** (unlocked). MQTT is built and verified (see `docs/changes/2026-10-02.md`); the `DevTerm.Web` host is built too (loopback-only, token auth, `/ws` tunnel; see `docs/changes/2026-10-02.md`). AMQP and STOMP wait for a real target; Blazor rendering of control panels is a later step. Proposals: [message-broker-protocols](docs/design/proposals/message-broker-protocols.md), [web-tunnel-blazor-frontend](docs/design/proposals/web-tunnel-blazor-frontend.md).
2. **`.ksy` gaps**: bit fields, length-prefixed frames and checksums are built (see `docs/changes/2026-10-02.md`). Left: a live check of the Zoom H4n status `.ksy` (needs the recorder on) and editor forms for the new frame settings.
3. **Observability**: built (see `docs/changes/2026-10-02.md`). Left: check it against the Aspire dashboard container, and `ILogger` export if wanted.
4. **TUI-only palette option** for the light theme on 16-color consoles: built (see `docs/changes/2026-10-02.md`). Left: look at it on a real legacy conhost.
5. **Plugin loading**: the loader is built (see `docs/changes/2026-10-03.md`). Left: move the built-in decoders (NMEA, RadexOne, ...) out of the core references into plugin folders, and a way to see loaded plugins in the TUI/WPF.
6. **LXI Phase 1** (discovery picker): the DG1062Z at 192.168.0.87 is the real target (raw SCPI on 5555, VXI-11 portmapper open).

Done from that round: the ser2net RFC 2217 container (`containers/`, see `docs/changes/2026-10-02.md`). `DevTerm.Analyzers` stays parked.

## Manual review

Still waiting on you (hardware or a decision you've deferred). Answer inline after **Your call:**.

1. **[069](docs/bugs/069-tds2024-laserjet-pcx-hardcopy-no-capture.md): TDS2024 LASERJET and PCX hardcopy give no capture.** Deferred: "continue to wait".
   **Your call:** continue to wait
2. **DE-5000 meter:** adapter GATT profile, `RealHardwareDe5000Tests`, and live-checking the DE-5000, K8055 and Zoom H4n `.ksy` layouts. Blocked on the meter being back on the bench. When?
   **Your call:**
3. **EByte E810 / UDP transport:** needs a fresh deliberate capture of the real unit (`FD00`/`FD01` byte-count discrepancy).
   **Your call:** continue to wait
4. **Z-Wave** and **BYTECC BT-UP01**: no hardware, deferred.
   **Your call:** continue to wait
5. **BLE on Linux/macOS:** no machine to test on.
   **Your call:** continue to wait

## Backlog / research

Not-yet-started work, prioritization notes, and early-stage research now live in
[`BACKLOG.md`](BACKLOG.md), including the design-level items from the Architect's 2026-09-23 notes.
