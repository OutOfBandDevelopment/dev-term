# 051: LF followed by CR counts as two lines

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
| **Confidence** | Confirmed (rare devices only) |
| **Area** | DevTerm.Core (LineReplyPresenter) |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Core/Presenters/LineReplyPresenter.cs`

## What happens
A device ending lines in LF CR produces a second, empty line, which consumes a pending query id (see
[006](006-reply-queue-desync.md)).

## Suggested fix
Treat LF CR as one terminator, or skip empty lines while an id is pending.
