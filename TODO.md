# TODO

Active / in-progress work for dev-term. Not-yet-started backlog and research work moved out to
[`BACKLOG.md`](BACKLOG.md) (2026-09-16) to keep this file to what's actually being worked on.
Completed work is logged by date under `docs/changes/`.

## In progress

Queue from your 2026-10-02 answers. Each item below is partly built; what is left is stated per item.

1. **Message brokers and the web tunnel** (unlocked). MQTT, AMQP and STOMP are built and verified (see `docs/changes/2026-10-02.md` and `2026-10-03.md`); the `DevTerm.Web` host is built too (loopback-only, token auth, `/ws` tunnel). The Blazor `/panel` page is built too (see `docs/changes/2026-10-03.md`). Indicator values on `/panel` update live. Web parity with the desktop apps is under way (2026-10-08): send format, echo, clear, logging, theme, history and the per-session Device menu are built; Playback, XON/XOFF, the Stream Monitor and Routing, the Theme builder and the converter tools editor and Stream Monitor Convert and the Manifest editor (pickers, live preview, Save As, recordings, .ksy import) are built too; project save/open and Stream Monitor over extra connections are built too (2026-10-09); left: Configure device. Also left: a real home-automation broker check. Proposals: [message-broker-protocols](docs/design/proposals/message-broker-protocols.md), [web-tunnel-blazor-frontend](docs/design/proposals/web-tunnel-blazor-frontend.md).
2. **Observability**: built (see `docs/changes/2026-10-02.md`). Metrics now export every 10 s (see `docs/changes/2026-10-03.md`). Traces confirmed in the Aspire dashboard container (see `docs/changes/2026-10-03.md`). Left: confirm the metric instruments show in its Metrics page. `ILogger` export has nothing to export yet (no code logs through `ILogger`), so it waits until something does.
3. **TUI-only palette option** for the light theme on 16-color consoles: built (see `docs/changes/2026-10-02.md`). Left: look at it on a real legacy conhost.

Plugin loading is finished: every built-in device, SCPI included, loads from `plugins/` and core references no device project (`docs/changes/2026-10-08.md`).

Done from that round: the ser2net RFC 2217 container (`containers/`, see `docs/changes/2026-10-02.md`). `DevTerm.Analyzers` stays parked.

## Suggested order

Re-ranked 2026-10-09 after a docs audit. Do these in order; each is buildable now unless noted.

1. **Web controls against live hardware.** The 2026-10-09 pass opened the DG1062Z panel from the browser (found and fixed the missing SCPI instrument picker, see `docs/changes/2026-10-09.md`); driving its controls, `/monitor` captures and `/routing` against the unit is next.
2. **Observability leftover:** confirm the metric instruments show on the Aspire Metrics page (needs the container up).
3. **Configure device on the web.** Needs your design call first: an inline connection editor on `/profiles`, or reuse of the profile picker. The `IDeviceConfigEditor` seam is built; the EByte and USR editors themselves wait on captures.
4. **Blocked, not worth starting:** the Manual review items below, the MQTT home-automation broker check, multi-browser, the 16-color conhost look.

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
