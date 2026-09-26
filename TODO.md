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
- **Radex One (`DevTerm.Devices.RadexOne`) needs real-hardware verification.** Built and unit-tested
  2026-09-25 (see `docs/changes/2026-09-25.md`), but a live HID enumeration pass that same session
  found no Radex One device attached (only an unrelated MSI "MYSTIC LIGHT" RGB controller) — the
  HID report-framing assumptions in `RadexOneHidFraming`/`RadexOneFramer` are unconfirmed. Once the
  device is attached: run `--listhiddevices true` to get its real VendorId/ProductId, fill those into
  `devterm.runsettings`' blank `RealHidRadexOne*` parameters, then run
  `RealHardwareRadexOneTests` (`TestCategory=Hardware`). Tighten `DevicePanels.IsAvailable`'s
  `DevicePanel.RadexOne` gate (currently "any HID connection") to the confirmed id pair once known.

## Backlog / research

Not-yet-started work, prioritization notes, and early-stage research now live in
[`BACKLOG.md`](BACKLOG.md), including the design-level items from the Architect's 2026-09-23 notes.
