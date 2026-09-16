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
  review the fields, then Save if you want to keep it.

A missing or malformed file reports an error in the status line rather than throwing.

## What's not built yet

Two Architect Notes items are still open, worth knowing if you're looking for them:

- **Exporting several profiles at once** as a single zip, with per-name conflict handling on import
  (ignore/rename/replace/replace-all) — today it's strictly one profile per file.
- **Deleting** is per-profile via the Delete button shown above — there's no bulk delete.

See [`docs/specs/connection-editor.md`](../specs/connection-editor.md)'s Open items for the full
list, including a decimal/hex display toggle and multi-select presenters.
