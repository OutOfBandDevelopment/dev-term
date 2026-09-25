# TODO

Active / in-progress work for dev-term. Not-yet-started backlog and research work moved out to
[`BACKLOG.md`](BACKLOG.md) (2026-09-16) to keep this file to what's actually being worked on.
Completed work is logged by date under `docs/changes/`.

## In progress

- **Real-hardware/real-usage bugs reported by the Architect (2026-09-23), not yet fixed.** Raw notes
  triaged into this file and `BACKLOG.md` the same day — design-level items (collapsible groups,
  per-field data-type/control-type metadata, an info icon showing the underlying command, Busylight
  custom-color UX, the Tektronix 2230 direction) moved to `BACKLOG.md`; these are concrete bugs
  against already-shipped screens:
  - **HP 34401A SCPI panel** (now on COM5): changing "Range" under "Configure" throwing an exception
    was fixed by the multi-parameter-field commit earlier today; "Configure DC Voltage Range doesn't
    appear to do anything" has a plausible fix (the profile's Range choice offered the illegal keyword
    "AUTO" instead of "DEF" — see `docs/changes/2026-09-23.md`) but is not yet re-verified against the
    real instrument.
  - **Device Profiles / Connection Editor**: selecting "BBL Lamp" under HID "detected devices" throws
    an out-of-index exception; a blank/`0`/unparsable HID Vendor or Product ID value isn't validated;
    switching profiles should disconnect the existing connection and only reconnect when "Connect" is
    pressed (not automatically); loading a saved profile should populate "Save as profile named" with
    that profile's name; "Save as profile named" is incorrectly cleared after "Save profile"; the
    detected-devices/detected-ports lists need a Refresh button.
  - **Busylight panel**: the custom-color dialog's values don't persist between openings (see also the
    related UX item moved to `BACKLOG.md`).
  - **Terminal screen (both front ends)**: the window title reportedly doesn't include the loaded
    profile name even though this was believed already landed (2026-09-18's "Architect's live window
    title" — needs re-checking, may be a regression); the send-line history (2026-09-23's Up/Down
    recall) adds a duplicate entry when the same line is sent twice in a row.
  - **Device presenter "Custom Command" section** (SCPI and any device profile using the always-present
    custom-command escape hatch): pressing Enter in the "Command" field throws an exception; clicking
    "Send" with a value typed in "Command" also throws. **Likely fixed by the same-day
    `ScpiControlSurface.InvokeAsync` change** (recognizes the custom-command field's own id, same as a
    multi-parameter command's own field, and no-ops instead of throwing "Unknown SCPI command" — see
    `docs/changes/2026-09-23.md`), but this specific repro hasn't been re-run to confirm.
  - **Remaining real-hardware verification opportunity**: Korad KA3005P/KA6003P, the HP/Agilent/
    Keysight 34401A, the Rigol DS1102E, and now the Rigol DM3058E are all confirmed via the automated
    `RealHardware*Tests` suite (`docs/test/2026-09-25-15-02-44.md`) — the DM3058E's previously-parked
    USBTMC bulk-IN stall did not reproduce. The Rigol DG1062Z has a test now too, but it surfaced a
    new, real USBTMC framing bug (2-byte bulk-IN reply, 12 bytes required) rather than confirming the
    device — see `BACKLOG.md`'s USBTMC entry. The DG1022 now has a test too (with its real, confirmed
    serial number set in `devterm.runsettings` to disambiguate it from the DS1102E); running it found
    its previously-documented "stuck at the device/USB level" behavior is intermittent, not
    permanent (`*IDN?` succeeded twice in a row, then the next query hit the empty-reply stall) — see
    `docs/test/2026-09-25-15-02-44.md`'s follow-up section and `BACKLOG.md`'s USBTMC entry.

## Backlog / research

Not-yet-started work, prioritization notes, and early-stage research now live in
[`BACKLOG.md`](BACKLOG.md) — including the remaining Connection Editor remnants (serial-port
descriptions on Linux/macOS; a WPF "not found" hint to match the TUI's; USBTMC's missing
`DevicePath`-equivalent) and the design-level items from the Architect's 2026-09-23 notes (see
above).
