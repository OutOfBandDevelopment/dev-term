# 037: A line exactly at the max length is followed by a spurious empty line

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
| **Confidence** | Confirmed |
| **Area** | DevTerm.Presenters.Text (AsciiPresenter) |
| **Found** | 2026-09-26 code review of `main` @ `42758db` (branch `dev/review-code`); static review, not yet reproduced |

## Where
`src/DevTerm.Presenters.Text/AsciiPresenter.cs:69-77, 82-85`

## Failure scenario
With `MaxLineLength = 4`, `ABCD\r\n` renders as `["ABCD", ""]`. At the default 4096, the same happens for an exactly
4096-byte line.

## Suggested fix
After a length flush, set a flag that swallows one immediately following CR, LF or CRLF.
