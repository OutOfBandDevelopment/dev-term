# Theme builder

Sourced from `BACKLOG.md`'s "Proposed Ideas" section (added 2026-09-30): "Have a theme builder —
include color pickers, have ability to save/export/import, store/enumerate from the
`~/.dev-term/themes` folder."

## Problem

[Theming](../theming.md) (implemented 2026-09-25) already has everything a builder would produce or
consume — `ThemeFile` (`~/.dev-term/themes/*.json`), `ThemeCatalog` (enumerates built-ins plus every
valid user theme, reports rejected files/duplicates/contrast warnings), `DevTermTheme.ContrastWarnings()`
(WCAG checks) — but *creating* one today means hand-writing JSON: knowing every `ThemeRole` name,
picking a `#RRGGBB` value with no live preview, and re-running dev-term to see the result. There's no
in-app way to build or edit a theme.

## Design

A new screen (View > Theme > Build/Edit Theme..., alongside the existing theme picker) that edits a
`DevTermTheme` directly against `ThemeRole`'s existing role list, backed entirely by the existing
model — no new persistence format:

- **One row per `ThemeRole`**, grouped roughly the way `theming.md`'s role list already groups them
  (surface colors, control colors, status colors, chart colors) — reusing `UiDefinitions`' existing
  section/control vocabulary (a `UiSection` per group) rather than inventing new layout code, per
  [ui-definitions.md](../ui-definitions.md)'s convention of describing panels declaratively.
- **Live preview**: apply the in-progress theme to the builder window itself (or a small sample panel
  showing a focused field, a menu, a status line, a chart swatch) via the same `ActiveTheme`/
  `TuiTheme`/`WpfTheme` mechanism a real switch uses, not a separate rendering path — so what the
  builder shows is provably what the real theme will look like, not an approximation.
- **Live contrast feedback**: run `DevTermTheme.ContrastWarnings()` on every edit and surface any
  failing pair next to the row that causes it, instead of only discovering a bad contrast ratio after
  saving (today's `ThemeFile` load-time behavior: "for a user theme the failures are reported but not
  rejected").
- **Save/export/import**: save writes a `ThemeFile`-shaped JSON into `~/.dev-term/themes/`
  (`DevTermUserDataPaths.ThemesDirectory`) using `ThemeFile`'s existing optional-field shape
  (`name`, `basedOn`, `chartPalette`, `colors` — only roles that differ from `basedOn` need to be
  written). Export/import are then just a file copy in/out of that directory — no new format to
  design.
- **Enumerate existing themes**: `ThemeCatalog` already does this (built-ins plus every valid file
  under `ThemesDirectory`) — the builder's "open an existing theme to edit" list is that catalog,
  not a new listing mechanism.

## Front-end split

- **WPF** has a native color picker path (a standard `ColorDialog`/a small custom RGB/HSV picker) —
  the Busylight panel already ships a custom RGB/HSV color-picker modal
  ([Kuando Busylight protocol](../features/kuando-busylight-protocol.md)) that's a reasonable
  starting point/reusable component for per-role color entry here.
- **TUI** has no true color-picker widget (Terminal.Gui v2.5.0's built-in controls don't include
  one). Editing a role's color as a `#RRGGBB` text field (validated the same way `ThemeFile` parsing
  already validates a malformed color) is the realistic v1 — matching how the Connection Editor
  already falls back to a plain text field for anything without a richer native widget.

## Open questions

- Whether the builder edits a `DevTermTheme` in place (a new theme from scratch, or a copy of an
  existing built-in/user theme to start from) or only ever edits a fresh copy — needs a decision on
  whether editing a built-in theme's *file* is possible (it isn't one — `light`/`dark`/`system` are
  code, not files under `ThemesDirectory`) or only ever produces a new user theme file seeded from a
  built-in's colors.
- Whether the live-preview mechanism needs a scoped "preview theme" concept in `ActiveTheme` (apply
  without persisting, revert on cancel) rather than reusing `Select`, which persists — `Select`
  today is "the only place a theme gets selected" and always writes the preference file.

## Status

**Not started — design only.** No code exists yet; this proposal is scoped against the theming
model as implemented 2026-09-25/2026-09-30 (see `docs/changes/2026-09-30.md`'s Dark-theme contrast
fix for the kind of contrast issue the live-feedback design above is meant to catch earlier).
