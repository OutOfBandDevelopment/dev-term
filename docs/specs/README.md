# UI Specs

One spec per user-facing screen/flow, kept current as that screen changes — unlike
[`docs/design/`](../design/README.md) (why a feature exists, the shared architecture behind it) or
[`docs/user-guide/`](../user-guide/README.md) (how to use it, with real screenshots), a spec here is
the precise reference for *what a screen does*: every field, every action, every validation rule,
every state — the thing to check against when changing behavior or verifying an implementation
matches intent. When a screen's shared logic lives in one place and multiple front ends render it
differently (the connection editor is the first example), one spec covers the shared behavior once
and calls out only the real per-front-end differences, rather than duplicating the whole spec per
front end.

## Format

Each spec follows the same shape:

- **Purpose** — one paragraph, what this screen is for and when it appears.
- **Fields** — a table: name, type, default, validation, notes. Every field a user can see or edit.
- **Actions** — a table: name (button/menu item/keyboard shortcut), what it does, preconditions,
  what happens on success/failure.
- **States** — what's visible/enabled/disabled and when (e.g. "the Serial field group is visible
  only when Transport is Serial").
- **Per-front-end notes** — real differences in how each front end renders or interacts with the
  same behavior (a different widget, a platform-specific dialog, an input method the other front
  end doesn't have). Not a restatement of the shared behavior above.
- **Open items** — known gaps or deferred behavior, so the spec doesn't silently imply something
  works that doesn't yet.

## Specs

- [Connection Editor](connection-editor.md) — the shared connection-editing screen
  (`DevTerm.Configuration.ConnectionEditorViewModel`), rendered as the TUI's `ConfigureMode` and
  WPF's `DeviceProfilesWindow`. Reachable at startup (invalid configuration) and from each front
  end's "File > Device Profiles..." menu item.
- [TUI Main Screen](tui-main-screen.md) — `TuiMode`, the console app's default full-screen mode.
- [WPF Main Window](wpf-main-window.md) — `MainWindow`, the GUI front end's only window today.

**Keep these current as the screens they describe change** — a spec that's drifted from the code is
worse than no spec, since it looks authoritative while being wrong. Update the relevant spec in the
same change that changes the behavior it describes, not as a separate follow-up.
