# TODO

Active / in-progress work for dev-term. Not-yet-started backlog and research work moved out to
[`BACKLOG.md`](BACKLOG.md) (2026-09-16) to keep this file to what's actually being worked on.
Completed work is logged by date under `docs/changes/`.

## In progress

- **Re-run the real-hardware suites once devices are attached.** `ReplyCollector` (multi-chunk replies) and the
  USBTMC `DevicePath` location landed 2026-09-25 with no hardware attached.
  - Run `dotnet test --settings devterm.runsettings --filter "TestCategory=Hardware"`.
  - Confirm `--listusbtmcdevices` prints a real `at usb:…` location for each Rigol.
- **BLE transport reconnect fails after the first successful connection this session
  (2026-09-29, needs further investigation).** Verified against a real SH-HC-08 BLE-to-serial
  bridge module (device id `BluetoothLE#BluetoothLEe0:c2:64:f3:4d:95-34:14:b5:37:0d:ac`, other side
  on COM4 9600 8N1): GATT enumeration (`--listblecharacteristics <id>`, new this session) correctly
  found its non-NUS profile (service `0000ffe0-0000-1000-8000-00805f9b34fb`, single Read+
  WriteWithoutResponse+Notify characteristic `0000ffe1-...` serving as both write and notify — set
  both `--blewritecharacteristicuuid`/`--blenotifycharacteristicuuid` to the same value), and a
  full BLE-to-serial byte round-trip succeeded (`hello-from-ble` sent over BLE arrived correctly on
  COM4). The *first* connection of the session worked immediately. Every reconnect attempted after
  it (three tries, after 5s/15s/30s cooldowns) failed identically with "Could not open the
  connection: The operation was canceled." — a generic cancellation, not `BleTransport`'s own
  `TimeoutException` (ruling out its `ConnectTimeoutMs` wrapper) and not `CliMode`, which never
  cancels its `OpenAsync` call. `WindowsBleAdapter.Cleanup()` was re-checked and already disposes
  `_service`/`_device` and unsubscribes both event handlers on every close, so there's no obvious
  dev-term-side leak to point to. Left undiagnosed rather than filed as a bug: no concrete root
  cause was found in dev-term's own code, so this may be a Windows BLE stack reconnect-timing quirk
  external to dev-term. Needs either a longer cooldown, checking Windows' own
  Bluetooth/GATT event logs during a repro, or comparing against a second real BLE peripheral before
  concluding either way; the reverse (serial-to-BLE) direction of the byte round-trip is still
  unverified because of this.
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
