# 042: A hinted #0 indefinite-length block keeps its header bytes in the capture

| | |
|---|---|
| **Severity** | Low |
| **Status** | Fixed |
| **Confidence** | Confirmed |
| **Area** | DevTerm.Core (StreamContentSniffer, StreamContentWatcher) |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Logging/StreamContentWatcher.cs:28-35` (`ScanForStart`) at the commit this was found at —
the code has since moved to `src/DevTerm.Core/StreamContent/StreamContentWatcher.cs` and
`StreamContentSniffer.cs` in the same directory.

## What happens
A `#0` indefinite-length IEEE 488.2 block is treated as NotABlock, so the `#0` header bytes end up in the captured data.

## Suggested fix
Recognize `#0` and strip its header, ending the block at the terminator or idle timeout.

## Resolution
Fixed on 2026-09-26 on `dev/fix-bugs`: `StreamContentSniffer.ParseBlockHeader`
(`src/DevTerm.Core/StreamContent/StreamContentSniffer.cs`) previously let a `digitCount` of 0 (the
literal two bytes `"#0"`) fall through to the `NotABlock` check; it now recognizes it as a new
`BlockHeaderStatus.Indefinite` shape, header length 2, no declared payload length. `Find` reports an
`Indefinite` match with `PayloadLength = null`, same as any other undeclared-length content.
`StreamContentWatcher.ScanForStart` (`src/DevTerm.Core/StreamContent/StreamContentWatcher.cs`) gained
a matching `case BlockHeaderStatus.Indefinite:` that strips the two header bytes and begins a capture
with no fixed length. Because the wrapped content's own signature (PNG, BMP, ...) may not have fully
arrived yet right after the header, identification is deferred to `AppendToCapture`, which now retries
`StreamContentSniffer.Identify` against the accumulated buffer on every chunk while the capture still
has no known kind/end-finder - once recognized, its structural end-finder takes over instead of
falling back to the idle timeout, the same way a recognized signature would if it had arrived whole
up front. An unrecognizable indefinite-length payload still falls back to the idle timeout, same as
today's undeclared/unrecognized-bytes case. Regression tests:
`StreamContentSnifferTests.Find_IndefiniteLengthBlockWrappingAnImage_ReportsTheHeaderWithNoPayloadLength`,
`StreamContentWatcherTests.DeclaredImage_InAnIndefiniteLengthBlock_IsCapturedByItsOwnStructuralEndWithNoHeaderBytes`.

Resolution recorded in commit `babdf36` (backfilled 2026-10-09 from git history).
