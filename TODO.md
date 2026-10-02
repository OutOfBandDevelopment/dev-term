# TODO

Active / in-progress work for dev-term. Not-yet-started backlog and research work moved out to
[`BACKLOG.md`](BACKLOG.md) (2026-09-16) to keep this file to what's actually being worked on.
Completed work is logged by date under `docs/changes/`.

## In progress

- **Manifest expression builder: decoder properties and regex matching, `.ksy` build-out, and a
  `.ksy`-to-profile tool.** Made a priority 2026-10-02. The expression language itself is already built
  (`DevTerm.UiDefinitions/Expression.cs`; see `docs/design/proposals/manifest-editor-expression-builder.md`),
  but it only reads flat `{id}` values from a decoder's `ValuesChanged` dictionary. Asked for: expressions
  that reference properties of a device decoder (a structured/Kaitai-decoded record, not just flat ids) or
  match with a regex against raw reply text; more `.ksy` definitions; and a tool that turns a `.ksy` file
  into a device profile (manifest). Starting point found in research: no `.ksy` files exist in the repo,
  no Kaitai package is referenced, and `DeviceManifest`'s `.ksy` path is "referenced only, not parsed"
  (`DeviceManifest.cs:64`), so decoding a `.ksy` is the biggest missing piece (own small interpreter vs.
  the Kaitai compiler was the first design decision). **Decided 2026-10-02: our own `.ksy` interpreter, framed as a
  transformation** of a `.ksy` into dev-term's existing manifest formats (a binary-frame counterpart to
  `InboundProtocol.Patterns`' regex-to-named-values path) rather than a separate decoding stack. Nothing built yet.
  **Scoped 2026-10-02** ([proposal](docs/design/proposals/expression-picker-paths-and-cel.md)): build order is a
  value-path catalog (also used by the validator), a sample-data generator for realistic previews, a picker control in
  both front ends, then a CEL-style language (spike a .NET CEL library first). **Step 1 (value-path catalog for
  manifests) landed 2026-10-02**; SCPI-profile input landed the same day;
  `SampleDataGenerator` landed too;
  next: wire it into the manifest editor preview, then the picker view-model.
- **Triage the ~22 regenerated `docs/user-guide/images/*` screenshots left uncommitted**, using the
  improved `scripts/image-diff/image_diff.py` (mask real content changes, restore pure noise).

## Backlog / research

Not-yet-started work, prioritization notes, and early-stage research now live in
[`BACKLOG.md`](BACKLOG.md), including the design-level items from the Architect's 2026-09-23 notes.
