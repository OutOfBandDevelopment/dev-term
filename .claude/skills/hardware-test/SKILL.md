---
name: hardware-test
description: Use when asked to run a real-hardware bench test pass against dev-term's device profiles/transports ("test the bench", "verify against real hardware", "run a hardware test pass", or similar) — gathers the current bench setup, runs commands against each device via dev-term's console CLI, and writes the report to docs/test/{yyyy-MM-dd-HH-mm-ss}.md.
---

# Running a real-hardware bench test pass

Produces one file: `docs/test/{yyyy-MM-dd-HH-mm-ss}.md` — the report from one real-hardware bench
test session. This is the same kind of report `docs/changes/2026-09-24.md`'s "Real-hardware test
pass against the current bench setup" section was, before that content moved to its own dedicated
location; use that entry as a structural model for the report this skill produces, but write the new
report to `docs/test/`, not `docs/changes/`.

## Step 1 — Ask for the bench setup before running anything

Do not guess or reuse a stale topology from memory/docs. Ask the user (unless they already gave all
of this in the request that invoked the skill):

- **Which instruments are on the bench right now**, with make/model for each.
- **How each one is reached**: transport (serial/TCP/USBTMC/HID), and the specific address (COM
  port, IP:port, VID:PID) for each.
- **Bench topology** — what's wired to what (which supply feeds which DMM, which scope channel reads
  which signal, etc.) — optional, but include it as a PlantUML `@startuml`/`component` diagram in the
  report if the user describes one, same as `docs/changes/2026-09-24.md`'s diagram.
- **Scope of the pass**: every reachable device, or a specific subset?
- **Anything already known to be in a bad state** (a device left mid-test from a prior session, a
  connection that needs multiple identify attempts before it responds correctly, etc.) — ask, since
  this changes how much retry logic to expect needing during the run, and it belongs in the report's
  Findings either way.

## Step 2 — Match each device to a profile

For each instrument named in Step 1, look for a matching profile under
`src/DevTerm.Devices.Scpi/Profiles/*.json` (match on vendor/model). If more than one profile could
plausibly apply (e.g. a similar-but-not-identical model), say so explicitly in the report rather than
silently picking one — this has bitten a prior pass (see the DS1102E/DS1105E note in
`docs/changes/2026-09-24.md`). If no profile exists at all, note that too; you can still test the
transport/connection itself without one.

## Step 3 — Run the commands

Use dev-term's console CLI in scripted mode (`--cli true`, not the TUI):

```bash
dotnet run --project src/DevTerm.Console -- --transport <serial|tcp|usbtmc|hid> <connection args> --presenter ascii --lineending <LineEnding> --cli true
```

For each device:

1. Connect, then send its identify command (`*IDN?`, `ID?`, or whatever the profile/manual calls
   for) first. **Retry up to 3 times with a short pause (~1-2s) between attempts if you get no
   reply or a garbled one before concluding the device is actually unreachable** — bench connections
   left over from a prior session have needed this before (see `docs/changes/2026-09-24.md`'s TDS2024
   "TCP bridge drops a command sent immediately after connect" finding, and any bad-state warning
   from Step 1). Note in the report whether a retry was needed.
2. Then run whatever commands are in scope: if the user named specific commands/behavior to check,
   run those; otherwise default to a handful of safe, read-only status/identify queries representative
   of the device's profile (don't run anything destructive — a factory reset, a calibration routine,
   a save/overwrite — without the user explicitly asking for it).
3. Record the **exact** command sent and the **exact raw** reply (not a paraphrase) for every
   exchange — the report's value is in being a faithful transcript, not a summary.
4. Note anything unexpected as you go (an unsolicited banner line, a delay needed before the first
   command works, a stale error-queue entry, a presenter needing a flag change to see anything) —
   these become the report's Findings section.

## Step 4 — Write the report

Path: `docs/test/{yyyy-MM-dd-HH-mm-ss}.md`, using the actual local date/time the session ran (e.g.
`2026-09-24-14-32-05.md`), not the date alone — more than one session can happen in a day. Structure,
matching `docs/changes/2026-09-24.md`'s bench-test entry:

1. **Title + one-line summary** of what this pass covered and why (a routine confirm, a regression
   check after a specific code change, chasing a specific bug).
2. **Bench topology** — the PlantUML diagram from Step 1, if one was given.
3. **Device configuration matrix** — table of device / role / profile used / transport+address /
   framing / result (confirmed working, confirmed working with caveats, failed, not attempted).
4. **Commands tested and responses** — one subsection per device, table of sent/received/notes.
5. **Findings** — anything genuinely surprising or worth remembering for next time (a device-specific
   quirk, a transport-timing issue, a stale-state gotcha). Skip this section if there's honestly
   nothing to report — don't pad it.
6. **Summary line**: what changed in dev-term itself as a result of this pass, if anything (usually
   "none — every profile tested round-tripped as already shipped").

## Step 5 — Follow-up

This step alone does not update `TODO.md`, `BACKLOG.md`, or `docs/changes/`. If the session
surfaced something that needs a real code fix, a profile correction, or a new backlog item, say so
and handle it as separate follow-up work (use the `docs-sync` skill's guidance for where that lands) —
don't fold it silently into the `docs/test/` report itself. If the pass is routine and uneventful,
a one-line pointer from today's `docs/changes/YYYY-MM-DD.md` to the new `docs/test/` file is enough;
never duplicate the full transcript into `docs/changes/`.
