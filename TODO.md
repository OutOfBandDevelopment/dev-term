# TODO

Active / in-progress work for dev-term. Not-yet-started backlog and research work moved out to
[`BACKLOG.md`](BACKLOG.md) (2026-09-16) to keep this file to what's actually being worked on.
Completed work is logged by date under `docs/changes/`.

## In progress

- **Re-check the window-title fix on the machine the 2026-09-23 report came from.** The report was that the
  title doesn't show the loaded profile's name. A real cause was found and fixed on 2026-09-25: the Connection
  Editor's Connect reset settings the form doesn't show, so the result no longer matched the saved profile the
  title looks up. That cause is covered by a test, but the reporting machine's own profiles weren't available.
  Confirm there that loading a profile in Device Profiles and connecting shows its name in the title (TUI and
  WPF). See `docs/changes/2026-09-25.md`, "Front ends never crash on errors…".
- **Re-run the real-hardware suites once devices are attached.** `ReplyCollector` (multi-chunk replies) and the
  USBTMC `DevicePath` location landed 2026-09-25 with no hardware attached.
  - Run `dotnet test --settings devterm.runsettings --filter "TestCategory=Hardware"`.
  - Confirm `--listusbtmcdevices` prints a real `at usb:…` location for each Rigol.
- **Radex One (`DevTerm.Devices.RadexOne`) needs a real-hardware re-run after a protocol fix.** A
  real device turned up on COM8 (2400 8N1 serial, not HID as an earlier draft wrongly assumed) but
  never replied to queries. Root-caused and fixed 2026-09-25 (see `docs/changes/2026-09-25.md`): the
  outer header's Type field was assumed to be a per-command code when it's actually a constant
  marker (the real command code lives in the Extension's own first word), and the checksum was a
  byte-sum instead of the real word-sum with a required modulo. Framer, a new
  `RadexOneExtensionCodec`, decoder, and control surface were all rewritten and checksum-verified
  byte-for-byte against the source doc's real trace examples; `RealHardwareRadexOneTests`' baud rate
  was also fixed (was 9600, device is 2400). Run
  `dotnet test --settings devterm.runsettings --filter "TestCategory=Hardware&TestCategory=Radex_One"`
  against the COM8 device to confirm it now replies — the actual point of this fix, not yet
  empirically confirmed.
- **Zoom H4n remote (`DevTerm.Devices.ZoomH4n`) needs real-hardware verification.** Built and
  unit-tested 2026-09-25 (see `docs/changes/2026-09-25.md`): decoder, control surface (including
  the init handshake's wake-byte watcher), `UiDefinition`, and menu wiring in both front ends. No
  `h4n2rs485` adapter/Zoom H4n was attached this session, so the handshake timing and status-bitmask
  semantics are unconfirmed against a live unit. Once the adapter is attached: fill in
  `devterm.runsettings`' blank `RealSerialZoomH4nPort`, then run `RealHardwareZoomH4nTests`
  (`TestCategory=Hardware`).
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

## Backlog / research

Not-yet-started work, prioritization notes, and early-stage research now live in
[`BACKLOG.md`](BACKLOG.md), including the design-level items from the Architect's 2026-09-23 notes.
