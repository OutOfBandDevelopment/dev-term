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

**Phase 1 — in progress** (the Stream Monitor branch landed 2026-09-25 and is documented in `docs/changes/2026-09-25.md`; the panels and logger/playback branches are still running):

- **Control-panel leftovers** (from `docs/specs/device-control-panel.md` Open items):
  - TUI Notes re-wrap on terminal resize.
  - Long TUI rows no longer run off the right edge.
  - Section expand/collapse state is remembered across panel openings.
- **New `UiDefinitions` display controls:** bar graph (one bar per channel), strip/roll chart recorder
  (1+ channels), and the vector/coordinate families (x/y, x/y/z, r/theta, optionally with an h/s/v
  color channel). They're rendered in both front ends.
- **Live panels from device manifests:** a declarative control surface that runs a loaded
  `DeviceManifest`'s command templates over the live session, and a "Device > Device Manifest..."
  menu item. A loaded manifest's `UiDefinition` was model-and-loader only until now.
- **Logger mode:** capture every sent/received message with direction, a sequence number and a
  timestamp, plus connect/disconnect events, to a documented, lossless log format. It's enabled from
  the CLI (`--log`), the TUI and WPF.
- **Playback mode** (was a `BACKLOG.md` research item):
  - Replays a log through the presenters: play/pause, realtime/fast/slow, step, rewind, fast-forward
    and a position indicator.
  - Trim (save a range as a new log) and markup (notes at a position).
  - A playback window in the TUI and WPF, and `--playback` in the CLI.
  - Never touches a real transport.

**Phase 2 — queued (after Phase 1 merges):**

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
- **Theming** (was a low-priority `BACKLOG.md` item): light/dark mode plus custom, user-defined theme
  profiles for both front ends. Investigate first:
  - WPF: .NET's Fluent `ThemeMode` on `net10.0-windows`.
  - TUI: Terminal.Gui v2.5.0's own `Scheme`s.

  The colors hard-coded today (status bar, output styling, swatches) must move onto the theme.

**Phase 3 — queued (last; it restructures both main windows):**

- **Multiple sessions per window** (from the TUI/WPF main-window specs' Open items). Presenters stopped
  being the blocker on 2026-09-25, since they're per-session now; nothing builds the UI for it yet.

## Backlog / research

Not-yet-started work, prioritization notes, and early-stage research now live in
[`BACKLOG.md`](BACKLOG.md), including the design-level items from the Architect's 2026-09-23 notes.
