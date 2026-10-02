# Expression Picker

## Purpose

Build a valid [expression](../design/manifest-editor-expression-builder.md) without remembering ids or syntax: pick the
values it may read, insert functions, and see at once whether it parses and what it evaluates to against sample data.
Opened from the **Pick...** button beside an indicator's **Expression** field, or a bar graph / strip chart's
**Channels** field, in the [Manifest Editor](manifest-editor.md) (other expression fields — a button's Parameter expressions, a chart channel's
expression — don't have it yet). Shared logic is `DevTerm.DeviceManifests.Editing.ExpressionPickerViewModel`, fed by
`ValuePathCatalog.Enumerate(manifest)` and `SampleDataGenerator`; rendered as `ExpressionPickerDialog` (TUI) and
`ExpressionPickerWindow` (WPF). Design: [proposal](../design/proposals/expression-picker-paths-and-cel.md).

## Fields

| Field | Type | Notes |
|---|---|---|
| Expression | text | The expression being built; edits re-parse immediately. Starts as the field's current text. |
| Diagnostics | read-only | `Empty`, `OK`, `OK, with warnings: ...` (an id nothing publishes, or a free-text path — warns, never blocks), or `Error: <parser message>`. Shown in the error color when invalid or warned. |
| Result with sample data | read-only | The expression evaluated over generated values, or `-` when empty/invalid. |
| Find a value | text | Filters the path list, case-insensitively, by id, origin or unit. |
| Value list | list | Each row `id  (type, unit, min to max / choices)`. |

## Actions

| Action | Effect |
|---|---|
| Choose a path (Enter / double-click) | Inserts `{id}` at the caret, replacing any selection. |
| Function buttons (`round`, `min`, `max`, `abs`, `if`) | Insert `name()` with the caret between the parentheses. |
| Next sample | Advances the sample data; the result changes. |
| OK | Returns the expression to the form field. |
| Cancel | Leaves the field unchanged. |

## Channels mode

From **Channels** the same dialog edits the chart's `id[:label[:#RRGGBB[:expression]]]; ...` list: choosing a value appends its
bare id as a new channel (with `; ` between), there are no function buttons, sample result or Next sample, and diagnostics
check each channel's id (and any channel expression) against the manifest, warning on unknown ids and erroring on a
channel expression that doesn't parse.

## States

The sample result is deterministic for a given manifest (seeded), so the same expression shows the same number each time
the picker opens until **Next sample** is pressed.

## Per-front-end notes

- TUI: a modal `Dialog` sized to the screen (up to 78x24); **Pick...** is a button after the field.
- WPF: a modal `ExpressionPickerWindow` owned by the editor; **Pick...** is docked right of the field.

## Open items

- Pick on a button's Parameter expressions, and on a single channel's expression segment.
- Operators/regex helpers and the CEL-style language extension (proposal's later steps).
