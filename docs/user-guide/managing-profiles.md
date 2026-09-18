# Managing connection profiles

Saving a working connection so you don't retype it, loading one back, deleting old ones, or moving
one between machines as a file — all from the same Connection Editor screen described in
[Connecting to a device](connecting.md). Reachable at startup (no valid connection configured) or
any time from **File > Device Profiles...**. Full field/action reference:
[`docs/specs/connection-editor.md`](../specs/connection-editor.md).

## Save, Load, Delete, Refresh

Fill in the fields (or load an existing profile to start from), type a name into **Save as profile
named**, and press **Save Profile**. If that name already exists, both front ends ask for
confirmation before overwriting. Loading a saved profile also fills that name back into the save
field, so re-saving after a tweak is just Save again.

TUI: the Load/Delete/Refresh buttons sit above the saved-profiles list —

![TUI connection editor showing Load/Delete/Refresh](images/tui-configure-serial.png)

WPF: the same three actions, plus the list grows and shrinks with the window instead of staying a
fixed height —

![WPF connection editor showing Load/Delete/Refresh](images/wpf-device-profiles-serial.png)

**Refresh** re-reads the profiles folder from disk manually — you shouldn't usually need it: both
front ends also watch `~/.dev-term/profiles` in real time and pick up a profile added, removed, or
renamed by the other front end (or by hand) automatically, refreshing the list on their own.

## Import / Export

Type, or pick a path with one of two buttons (both front ends have both): **Browse...** for an
existing file (the TUI's opens a real Terminal.Gui `OpenDialog`, WPF's a native `OpenFileDialog`),
or **Save As...** for a brand-new filename that doesn't exist yet (`SaveDialog`/`SaveFileDialog`) —
handy for Export specifically, since Browse alone would otherwise mean hand-typing a new name. Then:

- **Export** writes the *current fields* (validated first) to that path as JSON — the same shape a
  saved profile uses, just as a standalone file instead of going into `~/.dev-term/profiles`. Handy
  for handing a working connection to someone else, or backing one up outside the profiles folder.
- **Import** reads that JSON back into the fields — it does **not** save it as a profile by itself;
  review the fields, then Save if you want to keep it. Pointing it at a `.zip` file instead (see
  below) imports straight into the profiles folder rather than into the fields.

A missing or malformed file reports an error in the status line rather than throwing.

## Working with several profiles at once (export as zip, bulk delete)

The saved-profiles list supports selecting more than one row: click-drag or ctrl/shift-click in
WPF, or press SPACE on a row in the TUI (a checkmark shows which rows are marked). With one or more
selected:

- **Export Selected** writes just those profiles to the Import/export path as a single `.zip` (one
  `{name}.json` per profile).
- **Export All** does the same for every saved profile, regardless of what's selected.
- **Delete Selected** removes just those profiles, after a confirmation that lists their names —
  unlike the plain **Delete** button (one profile, no prompt), this can't be undone and touches more
  than the row you're looking at, so it asks first. In the TUI it's on the same row as Export
  Selected/Export All; in WPF it's in the button column under Delete.

Importing a `.zip` (via Import, same button as a single-file JSON import — it's detected by the
`.zip` extension) writes every profile it contains straight into `~/.dev-term/profiles`, refreshing
the list. If an incoming name already matches a saved profile, both front ends ask what to do —
**Replace** the existing one, **Rename** the incoming one (e.g. `name (2)`), or **Skip** it — once
per conflicting name.

## What's not built yet

A few Architect Notes items are still open, worth knowing if you're looking for them:

- **A wholesale "delete all existing profiles, then import everything"** shortcut for a zip import
  with several conflicts — today only the per-name Replace/Rename/Skip choice exists.

See [`docs/specs/connection-editor.md`](../specs/connection-editor.md)'s Open items for the full,
prioritized list.
