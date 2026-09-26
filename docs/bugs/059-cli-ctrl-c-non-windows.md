# 059: CLI Ctrl+C may not interrupt a pending read on Linux/macOS

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
| **Confidence** | Plausible |
| **Area** | CLI (CliMode) |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Console/CliMode.cs:75-78`

## What happens
`Console.In` is a `SyncTextReader`, whose `ReadLineAsync(ct)` checks the token once and then reads synchronously, so
it doesn't interrupt a pending read. On Windows `ReadLine` returns null after Ctrl+C anyway. On Linux/macOS, Ctrl+C
with `e.Cancel = true` probably leaves the loop blocked until Enter. The code comment says otherwise.

## Suggested fix
Verify on Linux; if confirmed, don't cancel the default Ctrl+C handling, or read stdin on a dedicated thread.
