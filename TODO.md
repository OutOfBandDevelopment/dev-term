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
    Keysight 34401A, and now the Rigol DS1102E are confirmed (see the SCPI module entry above); the
    Rigol DM3058E/DG1022 profiles are still unconfirmed. The newly-available Rigol bench units
    (DG1000Z, DG3000, DM3000, DS1000) are different specific models than these bundled profiles, so
    verifying likely means adding sibling profiles rather than confirming the existing ones
    unmodified — and, for any of them reachable only over USB rather than RS-232/LAN, is blocked on
    the USBTMC transport's own parked bulk-IN stall issue (see `BACKLOG.md`).

## Backlog / research

Not-yet-started work, prioritization notes, and early-stage research now live in
[`BACKLOG.md`](BACKLOG.md) — including the remaining Connection Editor remnants (serial-port
descriptions on Linux/macOS; a WPF "not found" hint to match the TUI's; USBTMC's missing
`DevicePath`-equivalent) and the design-level items from the Architect's 2026-09-23 notes (see
above).
