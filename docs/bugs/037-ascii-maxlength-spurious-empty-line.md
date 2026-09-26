# 037: A line exactly at the max length is followed by a spurious empty line

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
| **Confidence** | Confirmed |
| **Area** | DevTerm.Presenters.Text (AsciiPresenter) |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Presenters.Text/AsciiPresenter.cs:69-77, 82-85`

## Failure scenario
With `MaxLineLength = 4`, `ABCD\r\n` renders as `["ABCD", ""]`. At the default 4096, the same happens for an exactly
4096-byte line.

## Suggested fix
After a length flush, set a flag that swallows one immediately following CR, LF or CRLF.
