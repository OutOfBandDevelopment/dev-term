# 071: A text presenter prints the tail of a captured image after the capture ends

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Fixed |
| **Confidence** | Reproduced (real TDS2024 BMP hard copy; session log matched against the BMP header) |
| **Area** | DevTerm.Core (Pipeline, StreamContentWatcher), text presenters (ascii/scpi) |
| **Created** | 2026-10-03 |
| **Found at commit** | `afaf02e06b030e1d1843baf8599ac2773fe0e865` (`dev/hardware-review`) |
| **Found by** | User report (`.scratchpad/tds2024_mw_bmptest.png`) |

## Where
- `Pipeline.Render` fanned every chunk out to every presenter, including line-buffering text presenters, while `StreamContentWatcher` was capturing.

## What happens
`HARDCopy START` on the TDS2024 returns a BMP. The Stream Monitor captured it correctly (the log shows exactly the 77,878 bytes the header declares: 320x240, 8bpp, 54 + 1,024 palette + 76,800), but the ASCII/SCPI presenter had also been fed the image as text and kept an unterminated tail in its line buffer. Two stray lines of image bytes (`SSSS...`, `QQQ...`) then appeared after the "Captured" line, the second glued onto the next `*IDN?` reply.

## Failure scenario
Any binary hard copy on a connection that also shows a text presenter: leftover image bytes in the output, and the next reply corrupted by them. For a SCPI presenter this can also misalign pending reply ids.

## Suggested fix
Let the capturing presenter claim a chunk: run `IContentCapturePresenter`s first and withhold the chunk from every other presenter while a capture is in progress or ends in it.

## Tests to add
`PipelineTests.Render_WithholdsCapturedContentFromTextPresenters_SoNothingIsLeftInTheirBuffers`; the live-session watcher test expecting other presenters to see text before the image but not the image.

## Related
[069](069-tds2024-laserjet-pcx-hardcopy-no-capture.md). Known trade-off: text arriving in the very read that finishes a capture is withheld too.

## Resolution
Fixed in `ba191e9b08055422f96f46d3bd993bb724577c25` on 2026-10-03: `Pipeline.Render` consults `IContentCapturePresenter` first (`IsCapturing`, `CompletedCount`) and withholds the chunk from the other presenters. Regression test: `PipelineTests.Render_WithholdsCapturedContentFromTextPresenters_SoNothingIsLeftInTheirBuffers`. On the real scope 2026-10-03 the BMP, EPSIMAGE, TIFF, PCX and LASERJET captures all complete; the text view itself was not inspected. Details: `docs/changes/2026-10-03.md`.
