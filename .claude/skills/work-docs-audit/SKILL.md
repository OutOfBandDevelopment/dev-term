---
name: work-docs-audit
description: Use periodically, or after a burst of work, to audit that dev-term's work-tracking documents (TODO.md, BACKLOG.md, OPEN_QUESTIONS.md, docs/bugs, docs/design proposals, docs/changes, docs/devices, docs/test) are current and correct against the code and git history, fix what is stale, and commit the fixes. Not for syncing a single change (use docs-sync) or filing one bug (use bug-report).
---

# Auditing the work-tracking documents

`docs-sync` keeps docs aligned during one change; this skill is the periodic sweep that catches what
slipped. Every claim in a work document is checked against the code, `git log`, or a test file, never
against another doc's say-so. Fix what is wrong in place; report what needs a decision.

## Scope and what "correct" means

| Document | Check |
|---|---|
| `TODO.md` | Each item is still in progress. Anything finished (see `git log` and `docs/changes/`) is deleted outright once its detail exists under `docs/changes/`, never replaced by a pointer. The narrative's "landed / next" claims match the code. |
| `BACKLOG.md` | No item that has since been built (grep the code). No item that is really in progress. Referenced proposals and bugs exist. |
| `docs/bugs/` | Each `Open` / `In progress` report re-verified against the code at HEAD: still reproduces, already fixed, or obsolete. Closed reports have `## Resolution` and live in `docs/bugs/resolved/`; every row and inbound link in `docs/bugs/README.md`, code comments, specs and tests resolves; severity tables match each file's Status. A claim contradicted by newer evidence (bench reports, `docs/changes/`) gets a dated update note. |
| `docs/design/proposals/*` and `docs/design/README.md` | Each proposal's Status section and the README one-liner agree with each other and with what is built and verified. "Not started" must be true; "built" needs the code to exist; hardware claims need a `docs/test/` or `docs/changes/` source. |
| `BACKLOG.md` "Proposals and bugs tracker" | One row per proposal with unchecked `- [ ]` checklist items (`grep -c -- "- \[ \]" docs/design/proposals/*.md`) and every open bug is listed; checked-off items and closed bugs are removed; "Last swept" date updated. |
| `OPEN_QUESTIONS.md` | Re-sweep every `## Open questions` / `## Open items` section (`grep -rn "^## Open" docs`) plus TODO "Manual review", BACKLOG and open bugs. Every unanswered, still-true question has a line here; every line points at a question that is still open (strike-through or "resolved" in the source means delete the line). Questions answered by code or `docs/changes/` since the last sweep get struck in their source doc first. Update the "Last swept" date. |
| `docs/changes/YYYY-MM-DD.md` | Every day with commits has an entry; entries match what the commits did. Never rewrite earlier days' substance; append a correction instead. |
| `docs/devices/*`, `docs/test/*` | Current-state sections are filled in (no "to fill in"), match the latest bench report, include the literal commands, and no credentials. Profile `Notes` that the latest evidence contradicts are flagged. |

## Procedure

1. **Establish the window.** `git log --since=<last audit or 2 weeks> --format='%h %ad %s' --date=short`, and
   `git status`. Note the branch and whether anything is unpushed (report, never push unasked).
2. **Read the documents** in the scope table. For bugs, list statuses first
   (`grep -h 'Status\*\*' docs/bugs/*.md docs/bugs/resolved/*.md`).
3. **Verify, don't trust.** For each open bug, read the cited code at HEAD. For each BACKLOG/TODO claim, grep for
   the symbol or look for the commit. A proposal marked built needs its code and tests found.
4. **Check links mechanically.** Every `docs/bugs/NNN-*.md` reference under `docs`, `src`, `tests` and `*.md`
   (except `docs/changes/`, which is historical) must exist; every README table link must exist; every file
   under `docs/bugs/resolved/` has a closed Status (`Fixed`, `Won't fix` or duplicate) and a `## Resolution`; no closed report is left in `docs/bugs/`.
5. **Fix in place** with small, specific edits: status lines, stale paths, deleted finished TODO items (only with
   their detail already in `docs/changes/`), README rows, dated update notes. Preserve existing style.
6. **Do not** change shipped data (profile JSON, code) just to match a doc; if the doc is right and the code is
   stale, report it. Do not close a bug as fixed or won't-fix without evidence, and never close one on a guess.
7. **Build if code files changed** (comment-only path fixes still get a `dotnet build`).
8. **Commit** with specific paths, docs-only separate from code, no attribution lines, never amend or push.
9. **Log it**: add a short entry to today's `docs/changes/YYYY-MM-DD.md` (counts and what was corrected, not a
   repeat of the content).
10. **Report** per document: what was current, what was fixed, what needs a human decision (and why), and what is
    unpushed. Keep decisions as a short list; do not re-raise items the user has said they don't care about.
