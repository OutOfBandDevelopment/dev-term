# TODO

Active / in-progress work for dev-term. Not-yet-started backlog and research work moved out to
[`BACKLOG.md`](BACKLOG.md) (2026-09-16) to keep this file to what's actually being worked on.
Completed work is logged by date under `docs/changes/`.

## In progress

Queue from your 2026-10-02 answers. Each item below is partly built; what is left is stated per item.

1. **Message brokers and the web tunnel** (unlocked). MQTT, AMQP and STOMP are built and verified (see `docs/changes/2026-10-02.md` and `2026-10-03.md`); the `DevTerm.Web` host is built too (loopback-only, token auth, `/ws` tunnel). The Blazor `/panel` page is built too (see `docs/changes/2026-10-03.md`). Indicator values on `/panel` update live. Left: a real home-automation broker check. Proposals: [message-broker-protocols](docs/design/proposals/message-broker-protocols.md), [web-tunnel-blazor-frontend](docs/design/proposals/web-tunnel-blazor-frontend.md).
3. **Observability**: built (see `docs/changes/2026-10-02.md`). Metrics now export every 10 s (see `docs/changes/2026-10-03.md`). Traces confirmed in the Aspire dashboard container (see `docs/changes/2026-10-03.md`). Left: confirm the metric instruments show in its Metrics page. `ILogger` export has nothing to export yet (no code logs through `ILogger`), so it waits until something does.
4. **TUI-only palette option** for the light theme on 16-color consoles: built (see `docs/changes/2026-10-02.md`). Left: look at it on a real legacy conhost.
5. **Plugin loading**: the loader is built (see `docs/changes/2026-10-03.md`). Left: move the built-in decoders (NMEA, RadexOne, ...) out of the core references into plugin folders. Not a mechanical move: `ServiceCollectionExtensions` registers them, and `TuiMode`, `MainWindow` and `WebHost` reference the device projects directly for their menu items and control panels (K8055, Busylight, SCPI, De5000, ZoomH4n, RadexOne, NMEA), so it needs a plugin-contributed menu/panel contract first. The contract (`IDevicePanelContribution`) and the TUI/WPF menu entries are built; web `Web:Panel` also accepts a contributed id; K8055 and Busylight register contributions and the web host uses them; left: replacing the TUI/WPF hand-written items with them, then moving K8055/Busylight and the rest into plugin folders. See [plugin-contributed-panels](docs/design/proposals/plugin-contributed-panels.md).

Done from that round: the ser2net RFC 2217 container (`containers/`, see `docs/changes/2026-10-02.md`). `DevTerm.Analyzers` stays parked.

## Manual review

Still waiting on you (hardware or a decision you've deferred). Answer inline after **Your call:**.

1. **DE-5000 meter:** adapter GATT profile, `RealHardwareDe5000Tests`, and live-checking the DE-5000, K8055 and Zoom H4n `.ksy` layouts. Blocked on the meter being back on the bench. When?
   **Your call:**
2. **EByte E810 / UDP transport:** needs a fresh deliberate capture of the real unit (`FD00`/`FD01` byte-count discrepancy).
   **Your call:** continue to wait
3. **Z-Wave** and **BYTECC BT-UP01**: no hardware, deferred.
   **Your call:** continue to wait
4. **BLE on Linux/macOS:** no machine to test on.
   **Your call:** continue to wait

## Backlog / research

Not-yet-started work, prioritization notes, and early-stage research now live in
[`BACKLOG.md`](BACKLOG.md), including the design-level items from the Architect's 2026-09-23 notes.
