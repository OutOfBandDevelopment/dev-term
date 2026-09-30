# Loopback sample rate control

Sourced from `BACKLOG.md`'s "Proposed Ideas" section (added 2026-09-30): "for the loopback device,
add a parameter for setting the sample rate to control how fast stream samples are generated."

## Problem

[The Loopback transport](../transports.md#loopback) pushes its `MEAS?`/`Samples: N` responses (and
every other scripted response) back as separate writes with no artificial delay between them — useful
for exercising the UI end-to-end with no hardware, but it means anything that wants to visually verify
*live-arriving* behavior (a `StripChartControl` scrolling in as samples arrive, a Stream Monitor
capture appearing incrementally, a screen recording for the user guide showing a device "streaming")
gets every sample at once instead of at a believable device cadence.

## Design

`LoopbackTransportOptions` is already documented as "currently empty... purely as the natural
extension point for that later" (`transports.md`'s Loopback section) — this proposal is exactly that
"later": add a `SampleIntervalMs` (default `0`, preserving today's instant-delivery behavior so no
existing test or documented behavior changes unless a profile opts in) that the transport's `Samples:
N`/`MEAS?` handling awaits between each pushed line.

- **Surfaced in both front ends' connection editors** as a numeric field, visible only when the
  selected transport is `loopback` — the same transport-conditional `VisibleWhen` section pattern the
  Connection Editor already uses for every other transport's fields (see
  [ui-definitions.md](../ui-definitions.md)'s "Forms from one definition" section, settled answer #1:
  "each transport's fields are one section with a visibility condition").
- **CLI**: a `--loopbacksampleintervalms <N>` flag (or the equivalent `CliOptions` property name),
  following the existing "property names double as CLI flag names" convention
  (`CLAUDE.md`'s Configuration section).
- No change to the transport's command surface (`hello`, `Send Stream:`, `Send Events:`, `MEAS?`,
  `Samples:`, `help`) — this only affects the pacing of already-scripted output, not what's produced.

## Open questions

- Whether the interval applies uniformly to every scripted response, or only to the ones that are
  naturally multi-line/streaming (`Samples: N`, `Send Events: N`) — a single-line reply like `hello`
  arguably shouldn't gain an artificial delay just because a profile set an interval for streaming
  demos.
- Whether jitter (a small random variance around the configured interval, to look less mechanically
  even) is worth adding — the transport is documented elsewhere as deterministic on purpose (no
  unseeded `Random` anywhere in the demo data generators, per the screenshot-determinism guarantee
  used by `docs/design/testing.md`), so any jitter would need to come from a seeded, injectable clock
  the same way `session-logging.md`'s playback engine already does, not `System.Random` directly.

## Status

**Not started — design only.** No code exists yet.
