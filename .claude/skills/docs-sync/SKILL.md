---
name: docs-sync
description: Use after any change to a dev-term UI screen, command/flag, or shared behavior (ConnectionEditorViewModel, TuiMode, MainWindow, CliOptions, transports/presenters) — keeps docs/specs, docs/user-guide (including real screenshots), CLAUDE.md, docs/design, TODO.md, and the daily changelog in sync with the code in the same change, instead of as a follow-up that never happens.
---

# Keeping dev-term's docs aligned with its code

dev-term has five kinds of documentation, each with a different job. A behavior change is not done
until every doc whose job covers that behavior has been updated **in the same change**, not as a
separate follow-up:

| Doc | Job | Update it when |
|---|---|---|
| `docs/specs/*.md` | Precise reference for what a screen does — every field, action, state, validation rule | Any field, action, validation rule, or state changes on a screen that has a spec |
| `docs/user-guide/*.md` | Task-oriented walkthrough with real captured screenshots/transcripts | The visible layout, flow, or wording of a screen changes |
| `CLAUDE.md` | Non-obvious constraints that will bite the next change if forgotten | You hit a real, non-googleable gotcha (an API restriction, a threading/lifecycle hazard, a serialization limitation) — verified empirically, not guessed |
| `docs/design/*.md` | Why a feature exists, the shared architecture behind it | The architecture or rationale changes (new shared component, new cross-cutting concern) |
| `docs/changes/YYYY-MM-DD.md` | Daily log of what was verified and how | Any non-trivial change, especially anything verified against real hardware |
| `TODO.md` | Current backlog/in-progress state | A backlog item is finished, or a new one is identified |

## Screenshots are tests, not manual chores

Never hand-write a screenshot description or paste an old capture. Every screenshot/buffer-dump
example in `docs/user-guide/` comes from a real, automated test:

- **TUI**: `DevTerm.Console.Tests.ScreenshotTests` (and `TuiTestRunner.DumpBuffer()` for anything
  not yet covered there) renders a real Terminal.Gui window headlessly and dumps the actual screen
  buffer as text.
- **WPF**: `DevTerm.Wpf.Tests.ScreenshotTests` (via `WpfScreenshot`) renders a real, off-screen-but-
  actually-shown `Window` to a PNG via `RenderTargetBitmap`. It must be shown (even off-screen) —
  `Measure`/`Arrange` alone renders a blank image, confirmed empirically. Never call `Close()` on a
  `MainWindow` from a test — its async `OnClosing` cleanup has a real reentrancy hazard against a
  single manually-pumped `DispatcherFrame` (see `WpfScreenshot.ShowOffScreen`'s doc comment); just
  leave it open, the test process exits shortly after.
- Both `ScreenshotTests` classes find the repo root by walking up from `AppContext.BaseDirectory`
  looking for `DevTerm.slnx`, then write into `docs/user-guide/images/`.

When a screen's layout changes:

1. Update the production code.
2. Run the relevant `ScreenshotTests` class (`dotnet test --filter ClassName~ScreenshotTests`) —
   this regenerates the PNGs/`.txt` captures under `docs/user-guide/images/` in place.
3. If a new state/variant needs documenting, add a new `[TestMethod]` there rather than a one-off
   throwaway capture — it stays as a regression check, not just a one-time doc generation step.
4. Re-read the regenerated images/dumps before describing them in prose — don't assume the old
   description still matches.

## Order of operations for a UI/behavior change

1. Make the code change.
2. Update/add tests for the behavior itself (not just screenshots).
3. If the change touches a screen with a spec (`docs/specs/`), update that spec's Fields/Actions/
   States/Per-front-end notes/Open items — whichever section actually changed.
4. If the change is visible, re-run the relevant `ScreenshotTests` class and update the
   `docs/user-guide/` page that embeds those images.
5. If you hit a real gotcha while doing 1-2 (not a hypothetical one), add it to `CLAUDE.md`'s
   constraints list with what was verified and how.
6. If the change is architectural (not just a screen tweak), update the relevant `docs/design/*.md`.
7. Update `TODO.md`: move the finished item, add any new backlog items you deliberately deferred.
8. Add an entry to today's `docs/changes/YYYY-MM-DD.md` (create it if it doesn't exist).
9. Run the full test suite (`dotnet test`) before committing.

Skipping straight to step 9 after step 1 is the failure mode this skill exists to prevent — a spec
or user-guide page that's silently drifted from the code is worse than no doc at all, because it
still looks authoritative.
