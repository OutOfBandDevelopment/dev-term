# 037: A line exactly at the max length is followed by a spurious empty line

| | |
|---|---|
| **Severity** | Low |
| **Status** | Fixed |
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

## Tests to add
A test that renders a line whose length is exactly `MaxLineLength` followed by CRLF (and separately CR-only,
LF-only) and asserts the result is only the one flushed line, plus a test that a genuinely empty line right after
a length-triggered flush (`ABCD\r\n\r\n` at `MaxLineLength = 4`) still comes through as its own empty line.

## Resolution
Fixed on 2026-09-26 on `dev/fix-bugs`: `AsciiPresenter.Render` (`src/DevTerm.Presenters.Text/AsciiPresenter.cs`) now
sets a `_pendingLengthFlushTerminator` flag right after a length-triggered flush, and the CR/LF handling swallows
one CR, LF, or CRLF pair while it's set (clearing it as soon as a real data byte arrives, so it only ever
suppresses the one terminator immediately following the flush). Regression tests:
`AsciiPresenterTests.Render_LineExactlyAtMaxLengthWithCrLf_IsNotFollowedByASpuriousEmptyLine`,
`..._WithLf_...`, `..._WithCr_...`, and
`Render_LengthTriggeredFlushFollowedByAnIntentionalBlankLine_StillReturnsThatBlankLine`.
