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
  - **Busylight panel**: the custom-color dialog's values don't persist between openings (see also the
    related UX item moved to `BACKLOG.md`).
  - **Terminal screen (both front ends)**: the window title reportedly doesn't include the loaded
    profile name even though this was believed already landed (2026-09-18's "Architect's live window
    title" — needs re-checking, may be a regression); the send-line history (2026-09-23's Up/Down
    recall) adds a duplicate entry when the same line is sent twice in a row.

## Backlog / research

Not-yet-started work, prioritization notes, and early-stage research now live in
[`BACKLOG.md`](BACKLOG.md) — including the remaining Connection Editor remnants (serial-port
descriptions on Linux/macOS; a WPF "not found" hint to match the TUI's; USBTMC's missing
`DevicePath`-equivalent) and the design-level items from the Architect's 2026-09-23 notes (see
above).
