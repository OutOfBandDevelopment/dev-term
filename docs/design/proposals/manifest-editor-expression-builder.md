# Manifest editor expression builder

Sourced from `BACKLOG.md`'s "Proposed Ideas" section (added 2026-09-30): "for the manifest editor
create an expression builder — expressions should be setable fields allows for data values to be
mapped to parameters for controls."

## Problem

[UI definitions](../ui-definitions.md)' live display controls (`IndicatorControl`,
`BarGraphControl`, `StripChartControl`, `VectorControl`) bind directly to a decoder's published
values by id — `IStructuredPresenter.ValuesChanged` publishes a dictionary keyed by `UiControl.Id`,
and a control just shows whatever value arrives under its id, verbatim. There's no way to *derive* a
displayed value from the raw one (unit conversion — raw ADC counts to volts, a scale/offset, combining
two published values into one) without writing that logic into the decoder itself in code. The same
gap exists on the outbound side: `ButtonControl.ParameterFieldIds` reads each named sibling control's
raw current value and comma-joins them into the command — there's no way to transform a value (format
a number, apply a scale) before it's sent.

This matters specifically for the [device manifest](../device-manifests.md) case (a no-code, JSON-only
device configuration) — a code-based device module (K8055, Busylight) can always just write the
transform in its decoder; a manifest has no code to write it in at all.

## Design

Add an optional `Expression` string to the places a value currently passes through unmodified:

- `IndicatorControl.Expression` / a chart's `ChartChannel.Expression` — evaluated against the live
  published-values dictionary before display, in place of a bare `Id` lookup when present.
- A parameter-mapping expression for `ButtonControl` — evaluated per field (or as one expression over
  all named fields) before joining into the command, in place of the current bare comma-join, when
  present.

**Expression language**: a small, dev-term-owned arithmetic expression grammar (numeric literals, the
published values as named variables, `+ - * /`, parentheses, and a handful of built-in functions —
`round`, `min`, `max`, maybe a basic `if`/ternary) — not a general-purpose external DSL. This matches
the precedent already set in [device-control-modules.md](../device-control-modules.md)'s own
"declarative command/response schema" section, which deliberately chose a lightweight dev-term-owned
schema over a heavyweight external dependency for the same reason (plain ASCII query/response gear
doesn't need Kaitai Struct's weight; this doesn't need a general expression-engine dependency either).
A hand-rolled recursive-descent parser over this small a grammar is a few hundred lines, fully unit
testable with no external dependency, and the resulting `.ksy`-style "own the schema" precedent is
already established for the manifest/UI-definition layer.

**Alternative considered**: embedding an existing .NET expression-evaluation library (e.g. NCalc).
Faster to start, but pulls in a runtime dependency for what's a genuinely small grammar, and (per the
Kaitai precedent above) this project has consistently preferred owning small schemas outright over
taking on an external dependency for them. Worth revisiting only if the hand-rolled grammar's scope
grows past simple arithmetic (e.g., string manipulation, lookups against an external map file — closer
to the "mappable presenters" idea in [presenters.md](../presenters.md)).

**Where it's authored**: the [manifest editor](../specs/manifest-editor.md) gains an "Expression"
field next to the controls above, alongside the existing per-control fields it already edits
(Description, Constraint). Validated live against the manifest's own sample/preview values (the editor
already has a live panel preview — see `ui-definitions.md`'s "Display controls, manifest panels, and
TUI fitting" milestone) so a syntax error or an unknown variable name is caught while editing, not at
first live use.

## Open questions

- Whether an expression can reference *other controls'* current values (for `IndicatorControl`, this
  is unusual — indicators are normally one decoder value each) or only the raw value(s) it's already
  bound to plus constants.
- Whether this generalizes to `TextFieldControl.Constraint`-typed fields at all, or stays scoped to
  read-only display controls and the `ButtonControl` parameter-join case described above.
- Whether a bad/unparsable expression should behave like a bad `ValueConstraint` today (rejected at
  load with every problem listed, never silently ignored) — almost certainly yes, matching
  `ThemeFile`'s and `ValueConstraint`'s existing "never throw, always report" convention.

## Status

**Not started — design only.** No code exists yet. Grounded in the existing `UiDefinitions`/
`IStructuredPresenter` model as implemented through 2026-09-25 (see [ui-definitions.md](../ui-definitions.md)).
