---
name: bug-report
description: Use when filing, updating or closing a dev-term bug report under docs/bugs/ — whether the bug came from a code review, a failing test, a hardware bench session, or a user report — and when fixing a bug that already has a report. Covers numbering, the report template, recording the git commit the bug was found at, confidence levels, the docs/bugs/README.md index, and the Fixed/Resolution lifecycle.
---

# Filing and maintaining dev-term bug reports

Known bugs live in `docs/bugs/`, one Markdown file per bug, indexed by `docs/bugs/README.md`. A report is the
durable record of *what is wrong and how we know*; the fix's own detail still goes in `docs/changes/YYYY-MM-DD.md`
(see CLAUDE.md's Documentation section and the `docs-sync` skill).

## Before filing

1. **Check it isn't already filed.** Search `docs/bugs/` for the symbol, file and symptom
   (`grep -ril "<symbol>" docs/bugs`). If an open report covers it, add to that report instead (a new scenario,
   another location) rather than filing a duplicate. If two findings share a root cause, file one report and list
   both scenarios.
2. **Verify it against the code.** Read the actual code path, callers included. Quote the lines that show the
   defect. Don't file "might" findings with no concrete failure scenario.
3. **Record the code version.** Run `git rev-parse HEAD` (and note the branch) at the moment you confirm the bug.
   The line numbers in the report refer to that commit, so they stay checkable after the code moves:
   `git show <hash>:<path>`. If you found it on uncommitted work, say so and give the base commit.

## Numbering and file name

- The next number is one more than the highest existing `NNN` in `docs/bugs/` (three digits, zero-padded). Numbers
  are never reused, even for `Won't fix` or duplicates.
- File name: `NNN-short-kebab-slug.md`, the slug naming the defect (`001-session-double-open.md`), not the fix.

## Template

Copy this exactly; keep the header table's rows in this order.

```markdown
# NNN: <one-line statement of the defect, as the user or caller would see it>

| | |
|---|---|
| **Severity** | High / Medium / Low |
| **Status** | Open |
| **Confidence** | Confirmed / Reproduced / Plausible (and how: "checked against …", "found by two reviewers") |
| **Area** | <project(s) / component(s)> |
| **Created** | YYYY-MM-DD |
| **Found at commit** | `<full 40-char hash from git rev-parse HEAD>` (`<branch>`) |
| **Found by** | Code review / failing test `<name>` / bench session `docs/test/<file>.md` / user report |

## Where
- `path/to/File.cs:line-line` (`Member`), plus callers that matter.

## What happens
The mechanism: what the code does, quoted where it helps.

## Failure scenario
Concrete inputs/state -> the wrong result, hang or crash the user would see.

## Suggested fix
The smallest fix that removes the root cause, and alternatives if the choice is a design call.

## Tests to add
The regression test(s) that should fail before the fix and pass after.
```

Optional sections, when they apply: `## Evidence` (captured output, a trace), `## Related` (links to other reports as
`[NNN](NNN-slug.md)`), `## Workaround`.

### Severity

- **High**: data loss or corruption, a crash or hang in normal use, a wrong measurement shown as valid, or a
  security issue reachable from normal use.
- **Medium**: a real failure with a narrower trigger, a resource leak that grows with use, a security issue that
  needs untrusted input, or wrong behaviour with an easy workaround.
- **Low**: edge cases, cosmetic or wording defects, performance on unusual paths, hardening.

### Confidence

- **Reproduced**: seen happen (a failing test, a real run, a bench session). Say where.
- **Confirmed**: the code path was read end to end and the failure follows from it; not yet run.
- **Plausible**: the code path is real but the trigger depends on timing, hardware or platform behaviour that
  wasn't verified. Reproduce it (ideally as a failing test) before fixing.

## After filing

1. Add its row to the right severity table in `docs/bugs/README.md`:
   `| NNN | [title](NNN-slug.md) | area | Open |`, in number order.
2. For a batch (a review), add a short `docs/changes/YYYY-MM-DD.md` entry naming the source, the commit reviewed,
   and the count by severity; don't repeat the reports' content there.
3. Commit the reports on their own (not mixed with a fix), so `git log --diff-filter=A -- docs/bugs/NNN-*.md`
   finds the filing commit.

## Fixing a reported bug

1. If Confidence is `Plausible`, reproduce first; if it doesn't reproduce, say so in the report and set the
   status to `Won't fix` (with why) or keep it open with the new evidence.
2. Write the regression test from **Tests to add**, see it fail, then fix.
3. In the same change as the fix:
   - Set **Status** to `Fixed`, and add at the end:
     ```markdown
     ## Resolution
     Fixed in `<commit or PR>` on YYYY-MM-DD: <one line on the fix>. Regression test: `<TestClass.TestMethod>`.
     ```
   - Move the file into `docs/bugs/fixed/` (`git mv docs/bugs/NNN-slug.md docs/bugs/fixed/NNN-slug.md`) — same
     file name, just relocated, so `docs/bugs/` holds only what's still open. Update every link to it
     accordingly: its row in `docs/bugs/README.md` (`fixed/NNN-slug.md`), the `docs/changes/` entry
     (`../bugs/fixed/NNN-slug.md`), and any plain-text `docs/bugs/NNN-slug.md` mentions left in code
     comments or other reports' `## Related` sections.
   - Update the report's row in `docs/bugs/README.md` (Status column and link path).
   - Log the fix's detail in today's `docs/changes/` file, citing the report number.
   - If the bug revealed a non-obvious constraint, add it to CLAUDE.md's constraints list (see `docs-sync`).
4. Keep the file (under `docs/bugs/fixed/` now). Don't delete fixed reports.

Other statuses: `In progress` (someone is on it; say who or which branch), `Won't fix` (add a `## Resolution`
saying why), `Duplicate of [NNN](NNN-slug.md)`.
