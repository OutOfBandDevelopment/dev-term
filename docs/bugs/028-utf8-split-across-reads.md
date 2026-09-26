# 028: UTF-8 characters split across two reads come out garbled

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Open |
| **Confidence** | Confirmed |
| **Area** | DevTerm.Presenters.Text (Utf8Presenter) |
| **Found** | 2026-09-26 code review of `main` @ `42758db` (branch `dev/review-code`); static review, not yet reproduced |

## Where
`src/DevTerm.Presenters.Text/Utf8Presenter.cs:9`: `[Encoding.UTF8.GetString(data.ToContiguousSpan())]`

## What happens
Decoding is stateless per chunk.

## Failure scenario
"é" (`C3 A9`) arriving over serial as two 1-byte reads renders as two replacement characters. Serial routinely
delivers 1-byte reads, and TCP can split anywhere.

## Suggested fix
Keep a per-instance `Decoder` (`Encoding.UTF8.GetDecoder()`) and decode with `flush: false` (valid now that presenters
are per-session).

## Tests to add
A multi-byte sequence split across two `Render` calls decodes correctly. The only test today is a single-chunk round trip.
