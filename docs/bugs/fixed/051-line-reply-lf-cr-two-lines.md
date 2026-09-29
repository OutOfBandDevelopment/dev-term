# 051: LF followed by CR counts as two lines

| | |
|---|---|
| **Severity** | Low |
| **Status** | Fixed |
| **Confidence** | Confirmed (rare devices only) |
| **Area** | DevTerm.Core (LineReplyPresenter) |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Core/Presenters/LineReplyPresenter.cs`

## What happens
A device ending lines in LF CR produces a second, empty line, which consumes a pending query id (see
[006](fixed/006-reply-queue-desync.md)).

## Suggested fix
Treat LF CR as one terminator, or skip empty lines while an id is pending.

## Resolution
Fixed on 2026-09-29 on `dev/fix-bugs`: `LineReplyPresenter.Render`
(`src/DevTerm.Core/Presenters/LineReplyPresenter.cs`) already treated CR LF as one terminator (a
`_pendingCr` flag set on CR swallows an immediately-following LF), but had no symmetric handling for
LF CR — an LF completed the line, then the CR that followed completed a second, empty line,
consuming the next pending query's reply id exactly as [006](fixed/006-reply-queue-desync.md)
describes. Skipping empty lines while an id is pending (the report's other suggested option) was
already rejected in bug 006's own resolution ("some devices legitimately reply with an empty line"),
so this reuses the CRLF approach instead: a new `_pendingLf` flag, set when an LF completes a line,
swallows an immediately-following CR the same way `_pendingCr` already swallows an immediately-following
LF. Both flags reset on any other byte and in `Reset()`.

Regression test: `ScpiReplyPresenterTests.Render_LfThenCr_CountsAsOneLine_NotAnExtraEmptyOne` (fails
against the pre-fix code — the second query's reply id was consumed by the spurious empty line,
shifting "TWO" onto a nonexistent third query). Full `TestCategory=Unit` run green across the whole
solution (no regressions).
