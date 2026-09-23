---
name: device-manual
description: Use when asked to research a device's SCPI/remote-command interface and write a programming manual for it — e.g. "write a programming manual for the <device>", "research the <device>'s command set", or before starting a new DevTerm.Devices.Scpi profile for a device that doesn't have a docs/devices/ manual yet. Produces docs/devices/<vendor-model>/programming-manual.md by actually researching the vendor's current documentation, not by assuming a similar model's command set.
---

# Building a device programming manual from vendor documentation

Produces one file: `docs/devices/<vendor-model-slug>/programming-manual.md` — a complete,
self-contained remote-command reference for one instrument, written from real vendor documentation
(never from assumptions about a similar model in the same product line, since command sets vary even
within a series). This is the same kind of source document `hp-34401a/programming-manual.md`,
`tektronix-2230/programming-manual.md`, `tektronix-tds2024/programming-manual.md`, and
`korad-ka3005p-ka6003p/programming-manual.md` already are — it's what a later session reads to build
or correct a `src/DevTerm.Devices.Scpi/Profiles/*.json` profile against.

## Step 1 — Get the device and interface

If the invocation already names a specific make/model and interface, skip straight to Step 2.
Otherwise, ask (don't guess):

- **Device**: exact make and model. Push for the sub-variant or firmware revision too if the product
  line has ones that change the command set (e.g. "TDS2024" vs "TDS2024B", a specific firmware rev
  that added/removed commands).
- **Interface(s)** it'll actually be controlled over: GPIB, RS-232, USB-TMC/VISA, LAN/VXI-11, or more
  than one. This matters — it determines which framing/syntax details are even relevant (see Step 3's
  scope rules).
- **Any add-on** already known about (an installed module, add-on card, or license key) that changes
  what command interface is reachable at all.

## Step 2 — Research before writing anything

Do not draft from memory or from a similar model's known command set. For this specific
make/model/firmware:

1. Find the vendor's own programmer/remote-control manual (search for `"<model>" programming manual
   OR programmer's guide OR remote control manual pdf`). Fetch and read it, not just a search
   snippet.
2. Confirm the exact document identity — title, part/document number, revision/date — and note it;
   it belongs in the output's header/citation, not just in your own working notes.
3. Confirm which module/license/add-on (if any) is required to reach the command interface at all,
   and what it does and doesn't add to the command set.
4. If you can't find current, authoritative vendor documentation for this exact model, say so
   explicitly rather than substituting a similar model's manual silently — ask the user whether to
   proceed with a named substitute (clearly flagged as such throughout the output) or stop.

## Step 3 — Scope rules

- Include **only** commands the specified model + module/license combination actually supports. A
  command needing a different model tier, a different add-on, or a paid feature key is **excluded**,
  not just labeled — unless it's directly relevant to a capability the user asked about, in which case
  flag it clearly as unavailable in this configuration rather than omitting it silently.
- Strip out (or clearly separate into their own subsection) syntax details specific to an interface
  the user isn't using (e.g. GPIB-only EOI/block-transfer framing is noise in an RS-232-only manual).
- Group commands by functional category, matching the vendor manual's own grouping where possible
  (acquisition, trigger, vertical/horizontal, waveform transfer, status/error, etc. for a scope;
  source/measure/system for a meter or supply — whatever the vendor's own chapter structure is).

## Step 4 — Per-command content

For every command include:

- Full syntax with argument placeholders and valid value sets.
- A plain-language description: what it does, when you'd reach for it.
- Set vs. query form, and exactly what the query returns (format, units, example reply).
- A short usage example with realistic values.
- Gotchas: units, value ranges, firmware-version differences, side effects on other settings.

## Step 5 — Required sections beyond the command reference

- **Before you start**: physical connection requirements, any module/interface compatibility table.
- **Command syntax conventions**: abbreviation rules, command concatenation, numeric argument types,
  string/block argument formats.
- **A worked end-to-end example script** for the device's most common task (capturing and
  transferring a waveform for a scope; the equivalent core workflow for a meter/supply/other
  instrument).
- **Common gotchas** — a dedicated section, distinct from the per-command gotchas above.
- **Closing "what's excluded and why"** section: what was left out per Step 3's scope rules, and what
  would change about the manual if the module/interface configuration were different.

## Step 6 — Write the file

- Path: `docs/devices/<vendor-model-slug>/programming-manual.md` (lowercase, hyphenated — match the
  existing sibling directories' naming, e.g. `hp-34401a`, `tektronix-tds2024`,
  `korad-ka3005p-ka6003p`). Create the directory if it doesn't exist.
- Single self-contained Markdown file — this is the source document a later change reads to build or
  correct a `DevTerm.Devices.Scpi` profile against (see `docs/design/proposals/scpi-instrument-control.md`),
  so it needs to stand on its own without the chat context that produced it.
- This step alone does not update `TODO.md`, `docs/changes/`, or any `DevTerm.Devices.Scpi` profile —
  that's separate follow-up work once someone actually builds the profile from this manual. Say so
  when you finish, and mention the manual is now ready for that follow-up if the user wants it next.
