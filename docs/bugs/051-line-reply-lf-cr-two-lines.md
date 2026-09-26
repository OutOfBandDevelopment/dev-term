# 051: LF followed by CR counts as two lines

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
| **Confidence** | Confirmed (rare devices only) |
| **Area** | DevTerm.Core (LineReplyPresenter) |
| **Found** | 2026-09-26 code review of `main` @ `42758db` (branch `dev/review-code`); static review, not yet reproduced |

## Where
`src/DevTerm.Core/Presenters/LineReplyPresenter.cs`

## What happens
A device ending lines in LF CR produces a second, empty line, which consumes a pending query id (see
[006](006-reply-queue-desync.md)).

## Suggested fix
Treat LF CR as one terminator, or skip empty lines while an id is pending.
