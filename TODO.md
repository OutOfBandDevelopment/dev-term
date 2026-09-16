# TODO

Active / in-progress work for dev-term. Not-yet-started backlog and research work moved out to
[`BACKLOG.md`](BACKLOG.md) (2026-09-16) to keep this file to what's actually being worked on.
Completed work is logged by date under `docs/changes/`.

## In progress

- **UI Definitions model** (`DevTerm.UiDefinitions`), landed 2026-09-15 — a framework-agnostic,
  JSON/XML-serializable model for declaring a device control panel once (`UiDefinition` →
  `UiSection`s → seven `UiControl` kinds: button/toggle/slider/numeric/choice/textField/indicator),
  so every front end can render it generically instead of hand-coding UI per device per front end.
  Built from real device mockups already written (Kuando Busylight, Velleman K8055, EByte, Zoom
  H4n), not designed in the abstract — see docs/design/ui-definitions.md. Polymorphic serialization
  uses the framework's own support (`System.Text.Json`'s `[JsonDerivedType]`, `XmlSerializer`'s
  `[XmlElement]` per derived type on the collection) rather than hand-rolled discriminator parsing.
  Round-trip tested against a full real panel (the Busylight mockup, reproduced as data). **This is
  step one only**: nothing yet reads this model to produce real Terminal.Gui or WPF controls, and
  it isn't wired to `IControlSurface` (still design-only) or any live device. Next real targets to
  build the actual TUI/WPF renderers against, per explicit plan: the K8055 (plugged in) and the
  Busylight (already verified both directions) — both already have `@startsalt` mockups this model
  needs to be able to reproduce as real, working controls.

- **Device Manifests** (`DevTerm.DeviceManifests`), landed 2026-09-15 — a no-code `DeviceManifest`
  (identity, a transport hint, the declarative command/response schema already sketched in
  device-control-modules.md, and a `UiDefinition`) plus a `DeviceManifestLoader` handling all three
  shapes from docs/design/device-manifests.md: a single JSON file, a folder (`device.json` at its
  root, referenced files resolved relative to it), or a `.zip` of one (extracted, then loaded
  exactly like a folder). Found and fixed a real bug immediately via testing: `XmlSerializer`
  can't serialize `Dictionary<string,string>` at all (throws at reflection time) — switched
  `TransportHint.Options` to a `List<TransportOption>` Key/Value pair list, which both JSON and XML
  handle natively; noted in `CLAUDE.md` as a constraint for any future XML-round-tripped type.
  7 tests (JSON/XML round-trip with an inline UI, folder-mode with an external UI file, zip-mode,
  missing-referenced-file failures) — 147 tests across the solution now. **Step one only, same as
  UI Definitions**: the manifest only *references* a Kaitai `.ksy` file by path, doesn't parse one;
  nothing turns a loaded manifest into a working `IControlSurface`/decoder pair or opens a
  connection from it.

- **Connection Editor, from the 2026-09-15 Architect Notes.** Landing incrementally since
  2026-09-15 — full detail on each increment is in `docs/changes/2026-09-15.md`/
  `docs/changes/2026-09-16.md`, not repeated here. Landed so far: profile save/load/delete/import/
  export (`ConnectionProfileStore`) with the TUI-as-default-mode flip; a real Configure screen in
  the TUI and a "File > Device Profiles..." menu in both front ends, sharing one
  `ConnectionEditorViewModel` (WPF binds to it directly via XAML, no code-behind business logic;
  the TUI copies values to/from it around each button press); a "File > Connect"/"Disconnect" menu
  item (surfaced and fixed a real `Session` read-loop bug along the way); live mid-session profile
  switching (no restart needed) plus a manifest-not-found warning; serial-field dropdowns,
  Delete/Refresh, an overwrite-confirmation prompt, and `docs/specs/` as a new precise-reference doc
  kind; double-click-to-load and a dirty-field discard confirmation; a real profiles-folder
  `FileSystemWatcher` for saved-list auto-refresh; a TUI file picker (Browse...) and a scrollable
  TUI form; "type it or pick from what's attached" pickers for the serial port and HID
  vendor/product ID; and, most recently, a decimal/hex display toggle for HID Vendor/Product ID
  (a separate `*Display` property per field, so the canonical value stays decimal regardless of
  what's currently shown). Test automation for CLI/TUI/WPF (including Terminal.Gui's own headless
  testing API) and a `[TestCategory]` coding standard, both prerequisites for landing the above with
  confidence, are also done — see `docs/design/testing.md`/`docs/coding-standards.md`.

  Still open (see [`BACKLOG.md`](BACKLOG.md) for detail): a long/short name for a detected serial
  port, export-selected/export-all as a zip with per-name import conflict resolution, multi-select
  Presenter, per-input-line parser selection, and a save-style picker for a not-yet-existing export
  filename.

## Backlog / research

Not-yet-started work, prioritization notes, and early-stage research now live in
[`BACKLOG.md`](BACKLOG.md) — including the still-open Connection Editor items (decimal/hex toggle,
export-as-zip, multi-select Presenter, per-input-line parser selection, save-style export picker)
and a new "Window title, from the Architect" item.
