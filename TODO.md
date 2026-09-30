# TODO

Active / in-progress work for dev-term. Not-yet-started backlog and research work moved out to
[`BACKLOG.md`](BACKLOG.md) (2026-09-16) to keep this file to what's actually being worked on.
Completed work is logged by date under `docs/changes/`.

## In progress

### UI batch (started 2026-09-25)

Built on `dev/error-handling-and-todo`, with parallel work in separate git worktrees that are merged and
verified one at a time. Each item is deleted from this file once its detail has landed in `docs/changes/`.

- **Multiple sessions per window** (queued last, since it restructures both main windows; from the TUI/WPF
  main-window specs' Open items). Steps 1-3 done 2026-09-30 (SessionTab extraction, WPF `TabControl`, TUI
  `Terminal.Gui.Views.Tabs`; see `docs/changes/2026-09-30.md` and `docs/design/multi-session-ui.md`'s
  Status section) — both front ends now have a working tab strip. Step 4 remains: resolve
  `docs/design/multi-session-ui.md`'s Open questions (SendHistory/logging/Stream Monitor scope, zero-tab
  behavior, keyboard shortcuts).

## Backlog / research

Not-yet-started work, prioritization notes, and early-stage research now live in
[`BACKLOG.md`](BACKLOG.md), including the design-level items from the Architect's 2026-09-23 notes.
