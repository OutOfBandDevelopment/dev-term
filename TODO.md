# TODO

Active / in-progress work for dev-term. Not-yet-started backlog and research work moved out to
[`BACKLOG.md`](BACKLOG.md) (2026-09-16) to keep this file to what's actually being worked on.
Completed work is logged by date under `docs/changes/`.

## In progress

- **Manifest expression builder: decoder properties and regex matching, `.ksy` build-out, and a
  `.ksy`-to-profile tool.** Made a priority 2026-10-02. Asked for: expressions that reference properties
  of a device decoder (a structured, Kaitai-decoded record, not just flat ids) or match with a regex
  against raw reply text; more `.ksy` definitions; and a tool that turns a `.ksy` file into a device
  profile (manifest). **Decided 2026-10-02:** our own `.ksy` interpreter, framed as a transformation of
  a `.ksy` into dev-term's existing manifest formats (a binary-frame counterpart to
  `InboundProtocol.Patterns`' regex-to-named-values path), and the CEL spike chose to extend our own
  `Expression` rather than take a library. Designs: [expression picker, value paths and
  CEL-style language](docs/design/proposals/expression-picker-paths-and-cel.md), [`.ksy` importer and
  binary frames](docs/design/proposals/ksy-importer.md), [expression
  builder](docs/design/proposals/manifest-editor-expression-builder.md).

  Built so far (detail in `docs/changes/2026-10-02.md`; unit-tested, plus one live run of a Radex One frame through the presenter): the
  value-path catalog, sample-data generator, the picker in both front ends, the language extension
  (strings, `matches()`, lists, indexing, dotted ids), binary frames and `KsyImporter`.

  **Remaining:**
  - From the proposals' own "not built" lists: the sample-data generator's playback-log and
    declared-example tiers, regex-building in the picker, `.ksy` bit fields / variable-length frames /
    checksums.
  - Small gaps found 2026-10-02:
    - Real-device runs of the Radex One's Write Settings and Reset Accumulated (Write Settings changes a device setting, so
      ask first).
    - Bench passes for the K8055, Busylight and Zoom H4n, whose `known-configuration.md` pages lean on earlier docs for
      what was hardware-tested; the DE-5000 has never been run against a real meter here.

## Backlog / research

Not-yet-started work, prioritization notes, and early-stage research now live in
[`BACKLOG.md`](BACKLOG.md), including the design-level items from the Architect's 2026-09-23 notes.
