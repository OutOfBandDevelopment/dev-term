# 059: CLI Ctrl+C may not interrupt a pending read on Linux/macOS

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
| **Confidence** | Plausible |
| **Area** | CLI (CliMode) |
| **Found** | 2026-09-26 code review of `main` @ `42758db` (branch `dev/review-code`); static review, not yet reproduced |

## Where
`src/DevTerm.Console/CliMode.cs:75-78`

## What happens
`Console.In` is a `SyncTextReader`, whose `ReadLineAsync(ct)` checks the token once and then reads synchronously, so
it doesn't interrupt a pending read. On Windows `ReadLine` returns null after Ctrl+C anyway. On Linux/macOS, Ctrl+C
with `e.Cancel = true` probably leaves the loop blocked until Enter. The code comment says otherwise.

## Suggested fix
Verify on Linux; if confirmed, don't cancel the default Ctrl+C handling, or read stdin on a dedicated thread.
