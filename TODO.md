# TODO

Active / in-progress work for dev-term. Not-yet-started backlog and research work moved out to
[`BACKLOG.md`](BACKLOG.md) (2026-09-16) to keep this file to what's actually being worked on.
Completed work is logged by date under `docs/changes/`.

## In progress

- **Re-run the real-hardware suites once devices are attached.** `ReplyCollector` (multi-chunk replies) and the
  USBTMC `DevicePath` location landed 2026-09-25 with no hardware attached.
  - Run `dotnet test --settings devterm.runsettings --filter "TestCategory=Hardware"`.
  - Confirm `--listusbtmcdevices` prints a real `at usb:…` location for each Rigol.
- **BLE transport (`DevTerm.Transports.Ble` + Windows backend) needs real-hardware verification.**
  Built and wired end-to-end 2026-09-25 (see `docs/changes/2026-09-25.md`): `IBleAdapter`/
  `IBleAdapterFactory`/`IBleDeviceDiscovery` contract, a `Windows.Devices.Bluetooth`-backed
  implementation loaded at runtime via `BlePlatformAdapterLoader`, and full field wiring through
  `CliOptions`/CLI validation, the TUI Configure screen, and WPF's Device Profiles window, plus a
  `--listbledevices` CLI action. No BLE peripheral was paired/exercised this session — everything
  was verified by build + unit test only. Once a BLE peripheral (the DE-5000's custom IR-to-BLE
  adapter, or any NUS-speaking device) is paired: run `--listbledevices true` to confirm it lists,
  then connect with `--transport ble --bledeviceid <id>` and confirm read/write/notify actually
  round-trip real bytes.
- **DE-5000 LCR meter (`DevTerm.Devices.De5000`) needs real-hardware verification.** Built and
  unit-tested 2026-09-25 (see `docs/changes/2026-09-25.md`): `De5000Framer`/`De5000Decoder` (stream-
  buffering around the fixed 17-byte ES51919 packet), a deliberately no-op `De5000ControlSurface`
  (the meter has no writable commands), `De5000UiDefinition`, and menu wiring in both front ends,
  gated on "any BLE connection". No custom IR-to-BLE adapter was paired this session — its GATT
  profile (Nordic UART Service or custom) is still unconfirmed, per the BLE transport entry above.
  Once the adapter is paired: fill in `devterm.runsettings`' blank `RealBleDe5000DeviceId` (and the
  `RealBleDe5000*CharacteristicUuid` overrides if it turns out not to speak NUS), then run
  `RealHardwareDe5000Tests` (`TestCategory=Hardware`).

### UI batch (started 2026-09-25)

Built on `dev/error-handling-and-todo`, with parallel work in separate git worktrees that are merged and
verified one at a time. Each item is deleted from this file once its detail has landed in `docs/changes/`.

- **Multiple sessions per window** (queued last, since it restructures both main windows; from the TUI/WPF
  main-window specs' Open items). Presenters stopped being the blocker on 2026-09-25, since they're
  per-session now. Nothing builds the UI for it yet.

## Backlog / research

Not-yet-started work, prioritization notes, and early-stage research now live in
[`BACKLOG.md`](BACKLOG.md), including the design-level items from the Architect's 2026-09-23 notes.
