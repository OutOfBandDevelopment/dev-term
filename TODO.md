# TODO

Active / in-progress work for dev-term. Not-yet-started backlog and research work moved out to
[`BACKLOG.md`](BACKLOG.md) (2026-09-16) to keep this file to what's actually being worked on.
Completed work is logged by date under `docs/changes/`.

## In progress

### Forms engine and manifest editor (follow-ups from 2026-09-25)

- **Control panels ignore `VisibleWhen`** and show a `ChoiceStyle.CheckList` as a single choice (a
  dropdown); only the form renderers handle both.
- **The manifest editor can't edit a control's own `VisibleWhen`, and has no undo.**

### Binary layout formats

- `.ksy` reference for binary layouts via [Kaitai Struct](https://kaitai.io/) — see the new section
  in `docs/design/device-control-modules.md`. Not started.

### Tektronix TDS2024

- **Every `TRIGger:...?` query hangs (never replies) against this specific real TDS2024 unit** —
  real-hardware confirmed 2026-09-25 (`docs/test/2026-09-25-18-57-22.md`): `TRIGger:MAIn:FREQuency?`
  and `TRIGger:STATE?` (a much cheaper status query, ruling out "expensive measurement" as the
  cause) both hung the full step timeout, while every non-`TRIGger` query tried (`*IDN?`, `CH1?`,
  `CH2?`) answered normally, including as the 3rd command in a sequence (ruling out a simple
  "3rd command" positional issue). `tektronix-tds2024.json`'s own `Name` field notes this unit is
  specifically "NOT the TDS2024B" — unconfirmed hypothesis that the `TRIGger` query family needs
  that variant's firmware. `RealHardwareTcpTests`'s TDS2024 test avoids the whole `TRIGger` family
  for now (uses `CH1?`/`CH2?` instead). Not investigated further — needs a packet capture of a
  known-working `TRIGger` query (e.g. from a Tek-provided tool) against this exact unit to compare
  framing, similar to the USBTMC framing bugs below.

### WPF layout review follow-ups (from 2026-09-25)

- **Light theme Accent/Warning are below 4.5:1 as text colors** (4.1:1 and 3.3:1; Playback's `[tx]` and
  `[note]` lines). Needs a palette decision covering both front ends and `docs/design/theming.md`; allow-listed
  in `UiLayoutReviewTests` until then.
- **The Manifest Editor preview's fixed-size charts need a sideways scroll at the default 1180px.** Letting
  charts shrink to the column would fix it.
- **Cap field widths on wide windows.** At 1600px, text boxes and combos in Device Profiles and the
  manifest editor stretch across the whole window.
- **The Manifest picker's empty error area leaves ~24px of blank space** above the buttons.
- **Busylight's unlabeled Apply row isn't aligned** with the section label columns above it.
- **The Manifest Editor's pane title repeats its first section header** ("Identity" / "Identity").
- **No review at 125/150% DPI,** and no keyboard-focus-visual review; the layout review runs at 96 DPI only.
- **Not every review PNG was opened by eye:** most large-size captures, SCPI panels other than DS1102E and
  Generic, most manifest-editor node kinds, and the menus in the second theme.

### TUI layout review follow-ups (from 2026-09-25)

- **Control-panel button rows repeat their label** ("Apply: [Apply]", "Custom...: [Custom...]"). It's how
  the label column lines up; a design call.
- **Ctrl+Q in a nested TUI panel or dialog closes that window** rather than quitting the app. Decide which
  it should be.
- **The layout matrix made `DevTerm.Console.Tests` ~2 min** (was ~16 s). Reuse one app per class, or trim
  the matrix to 80x25 plus 200x60.
- **A scrolled form can show a lone button-shadow row** at the viewport's top edge (correct, odd look).
- **The startup editor looks unthemed in legacy conhost** (16-color downgrade of the truecolor theme:
  invisible field backgrounds, faint focus).
- **Busylight's panel says "Not decoding — connect with the matching --presenter"** when opened without a
  structured source; check whether that message suits an output-only device.
- **Not reviewed yet:** the Terminal.Gui file dialogs (Browse, Save As); the manifest editor's New/empty
  state and its "Create panel from commands" hint; Playback, the Stream Monitor and the SCPI panels in Dark;
  the K8055 with live data; the main window's menus while disconnected.

### UI batch (started 2026-09-25)

Built on `dev/error-handling-and-todo`, with parallel work in separate git worktrees that are merged and
verified one at a time. Each item is deleted from this file once its detail has landed in `docs/changes/`.

- **Multiple sessions per window** (queued last, since it restructures both main windows; from the TUI/WPF
  main-window specs' Open items). Presenters stopped being the blocker on 2026-09-25, since they're
  per-session now. Nothing builds the UI for it yet.

## Backlog / research

Not-yet-started work, prioritization notes, and early-stage research now live in
[`BACKLOG.md`](BACKLOG.md), including the design-level items from the Architect's 2026-09-23 notes.
