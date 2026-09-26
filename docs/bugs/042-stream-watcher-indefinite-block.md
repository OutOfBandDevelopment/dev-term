# 042: A hinted #0 indefinite-length block keeps its header bytes in the capture

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
| **Confidence** | Confirmed |
| **Area** | DevTerm.Logging (StreamContentWatcher) |
| **Found** | 2026-09-26 code review of `main` @ `42758db` (branch `dev/review-code`); static review, not yet reproduced |

## Where
`src/DevTerm.Logging/StreamContentWatcher.cs:28-35` (`ScanForStart`)

## What happens
A `#0` indefinite-length IEEE 488.2 block is treated as NotABlock, so the `#0` header bytes end up in the captured data.

## Suggested fix
Recognize `#0` and strip its header, ending the block at the terminator or idle timeout.
