# Expression Picker

## Purpose

Build a valid [expression](../design/manifest-editor-expression-builder.md) without remembering ids or syntax: pick the
values it may read, insert functions, and see at once whether it parses and what it evaluates to against sample data.
Opened from the **Pick...** button beside an indicator's **Expression** field, or a bar graph / strip chart's
**Channels** field, in the [Manifest Editor](manifest-editor.md), and (2026-10-02) beside every other field that names a value: a
button's Parameter expressions, vector/color value ids and visible-when. Shared logic is `DevTerm.DeviceManifests.Editing.ExpressionPickerViewModel`, fed by
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

## Text expressions

The picker has no buttons for the text functions, but typing them works and the diagnostics check them: `matches(text, regex)`
(a literal regex that doesn't compile is an error), `contains`, `startsWith`, `endsWith`, `size`, `number`, `string`,
`has({id})`, `!x` and `cond ? a : b`, with `'single'` or `"double"` quoted strings and `+` to join them. On a live indicator
or chart, `{id}` is the device's published text: arithmetic still reads the number out of `"12.5 V"`, while string functions see the
raw text. A text path gets sample text (`sample-xxxx-N`) in the preview, and a text result shows quoted. A button's parameter expressions see each sibling control's current text the same way.

Lists, indexing and bare names also parse: `[1, 2, 3]`, `x[i]` (an item of a list or a character of a string; out of range reads as 0),
`x in list` / `'ell' in 'hello'`, `size(list)`, `list + list`, `split(text, sep)` and `join(list, sep)`, so
`split({frame}, ',')[1]` reads the second comma-separated field. `volts` and the dotted `gps.sats` mean the same as `{volts}` and
`{gps.sats}` (the picker still inserts the braced form), and `true`/`false` are 1 and 0. A list result shows as `[1,2,3]` and is not a number.

## Channels mode

From **Channels** the same dialog edits the chart's `id[:label[:#RRGGBB[:expression]]]; ...` list: choosing a value appends its
bare id as a new channel (with `; ` between), there are no function buttons, sample result or Next sample, and diagnostics
check each channel's id (and any channel expression) against the manifest, warning on unknown ids and erroring on a
channel expression that doesn't parse.

## Other modes

The same dialog serves every field that names a value (`PickerMode`):

- **Parameter expressions** (a button): a `;`-separated list of expressions. Choosing a value inserts `{id}` at the caret, the
  function buttons are shown, blanks are allowed, and a bad item is reported as `Item N: ...`.
- **Value id** fields (vector X/Y/Z/Radius/Angle ids, color Hue/Saturation/Brightness ids, **Visible when**): choosing a value
  replaces the field with that id; an id nothing publishes warns.

## States

The sample result is deterministic for a given manifest (seeded), so the same expression shows the same number each time
the picker opens until **Next sample** is pressed.

## Per-front-end notes

- TUI: a modal `Dialog` sized to the screen (up to 78x24); **Pick...** is a button after the field.
- WPF: a modal `ExpressionPickerWindow` owned by the editor; **Pick...** is docked right of the field.

## Open items

- Pick on a single channel's expression segment (typed after the third `:`), and on a button's Parameter fields (command parameter ids, not value paths).
- Operators/regex helpers and the CEL-style language extension (proposal's later steps).
