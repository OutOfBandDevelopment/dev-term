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

### UI batch (started 2026-09-25)

Being built in three phases on `dev/error-handling-and-todo`. Parallel work happens in separate git
worktrees, merged and verified before the next phase starts. Items moved here from `BACKLOG.md`
(and from the specs' Open items) when work started. Each is deleted from this file once its detail
has landed in `docs/changes/`.

**Phase 1 — done** (2026-09-25): Stream Monitor, panels (leftovers, chart controls, live manifest panels) and logger/playback all landed; see `docs/changes/2026-09-25.md`.

**Phase 2 — in progress** (theming landed 2026-09-25, see `docs/changes/2026-09-25.md`; the forms engine + manifest editor branch is still running):

- **Forms from one definition** (was a low-priority `BACKLOG.md` item):
  - Attributes on a model's properties plus a reflection-based generator that turns an annotated
    model (starting with `CliOptions`) into the same `UiDefinition` a device manifest produces.
  - The Connection Editor then renders through the one engine per front end, instead of being
    hand-built twice (`ConfigureMode`, `DeviceProfilesWindow`).
  - Open scope questions from the backlog note: whether `UiDefinition`'s one level of grouping covers
    the editor's transport-conditional field groups, and whether `ConnectionEditorViewModel` sits
    under the render engine or is subsumed by it.
- **Device manifest editor:** at least a default render for request/response messages, ideally a
  presentation editor. It builds on the forms engine above.
**Phase 3 — queued (last; it restructures both main windows):**

- **Multiple sessions per window** (from the TUI/WPF main-window specs' Open items). Presenters stopped
  being the blocker on 2026-09-25, since they're per-session now; nothing builds the UI for it yet.

## Backlog / research

Not-yet-started work, prioritization notes, and early-stage research now live in
[`BACKLOG.md`](BACKLOG.md), including the design-level items from the Architect's 2026-09-23 notes.
