# Stream Monitor

## Purpose

An optional window (**Device > Stream Monitor...** in both front ends) that watches the current
connection's incoming bytes for renderable/binary content — raster images, HP-GL plots, PostScript
and PCL print jobs — captures each one whole, and auto-saves it as a file. It's for devices that
answer (or spontaneously send) something other than a short text line: an oscilloscope/generator
screen dump, a plotter's HP-GL stream, a hard copy in a printer language. Without it, those bytes
only show up as garbage in the text presenters. The TUI shows a capture list only (Terminal.Gui can't
draw images); WPF adds a live preview for the image formats it decodes natively. Design intent:
[`docs/design/proposals/stream-content-detection.md`](../design/proposals/stream-content-detection.md).
How to use it: [`docs/user-guide/stream-monitor.md`](../user-guide/stream-monitor.md).

The shared behavior lives outside both front ends: detection in `DevTerm.Core.StreamContent`
(`StreamContentSniffer`, `StreamContentEndFinder`, `StreamContentWatcher`), capture/auto-save in
`DevTerm.Configuration.StreamMonitor`. The windows are `DevTerm.Console.StreamMonitorMode` (TUI) and
`DevTerm.Wpf.StreamMonitorWindow` (WPF).

## Fields

| Field | TUI | WPF | Notes |
|---|---|---|---|
| State line | `● Monitoring {device}` (green) / `○ Stopped — {device}` (grey) | Colored dot + `Monitoring {device}` / `Stopped — {device}` | `{device}` is the saved profile's name when the connection is exactly one, otherwise its `tcp://…`/`serial://…`/`hid://…` definition (`StreamMonitor.DeviceNameFor`, the same subject the main window's title shows) |
| Export folder | `Saving to: {folder}` | `Saving to {folder}` (full path in the tooltip) | The connection's `ExportDirectory` (`CliOptions.EffectiveExportDirectory`), default `~/.dev-term/exports`. The user's home folder is shown as `~` (`StreamMonitor.DisplayPath`) |
| Explanation | Two fixed lines | One wrapped line | What's detected, and (TUI) that there's no preview |
| Capture list | `ListView`, one row per capture: `HH:mm:ss  TYPE  size  end  file` | `ListBox`, two lines per capture: `HH:mm:ss — {kind}` / `{size} bytes · {end} · {file}` | Oldest first; the newest is selected whenever one arrives. Keeps the last 100 (`StreamMonitor.MaxRetainedCaptures`) — saved files are never deleted |
| Detail | Two lines under the list: `{kind}, {size} bytes, {end}[ (declared by the command)].` / `Saved as {file} in the folder above.` or `Not saved: {reason}` | Same first line; second line `Saved to {path}` or `Not saved: {reason}` | For the selected capture |
| Preview | — | `Image` for BMP/PNG/JPEG/GIF/TIFF (WPF's built-in decoders, scaled down to fit, never up); otherwise a message | See States |

"End" is how the capture finished (`StreamMonitorCapture.EndLabel`):

| Label | Meaning |
|---|---|
| `complete` | The content's own structure (or a SCPI block's declared length) said it was finished |
| `went quiet` | No more bytes for the idle timeout (2 s) — the normal end for HP-GL, TIFF and undeclared-length data |
| `size limit` | Cut off at 64 MiB — probably incomplete |
| `stopped` | Monitoring was stopped (or moved to another connection) mid-capture — probably incomplete |

## Detection

A capture starts when either:

1. **A declared hint** is pending: a SCPI command whose `ScpiCommandDefinition.ExpectedResponseFormat`
   is not `Text` was just sent from a SCPI Instrument panel (it calls `IStreamContentHintSink.ExpectResponse`
   on every sink in the session's pipeline — the running monitor's watcher, if any). The next reply is
   captured regardless of its bytes: if it starts with an IEEE 488.2 definite-length block header
   (`#<n><length>`), exactly `<length>` bytes after the header are captured (header stripped);
   otherwise everything until the idle timeout. The kind is identified from the captured bytes, falling
   back to the declared format (`Image` → "image data (unrecognized format)", `.bin`). Bundled today:
   the Rigol DG1062Z's `HCOPy:SDUMp:DATA?` ("Screen Capture (Bitmap)?") declares `Image`.
2. **A signature is sniffed** anywhere in the incoming bytes (no hint needed, so unsolicited data
   works too):

| Kind | Signature | Saved as | Ends |
|---|---|---|---|
| PNG image | `89 50 4E 47 0D 0A 1A 0A` | `.png` | After the `IEND` chunk (chunk lengths walked) |
| JPEG image | `FF D8 FF` | `.jpg` | At the final end-of-image marker (segments walked, so an embedded EXIF thumbnail's own marker doesn't end it) |
| GIF image | `GIF87a` / `GIF89a` | `.gif` | At the trailer byte (blocks walked) |
| BMP image | `BM` + a plausible 14-byte file header and DIB header size | `.bmp` | At the header's file size |
| TIFF image | `II*\0` / `MM\0*` | `.tif` | Idle timeout |
| PostScript | `%!PS`, or the binary EPS header `C5 D0 D3 C6` | `.ps` | After `%%EOF` (plus a trailing CR/LF already received) or Ctrl-D; binary EPS at its header's section sizes |
| PCL | PJL `ESC%-12345X`, `ESC E` directly followed by another PCL escape, or `ESC%0B`/`ESC%1B`/`ESC%-1B` | `.pcl` | At the closing `ESC%-12345X` for a PJL job; otherwise idle timeout |
| HP-GL | `IN;` or `DF;` first, or three consecutive recognized two-letter instructions each ending in `;` — **only at the start of a reply** (after a quiet gap, or right after CR/LF) | `.hpgl` | Idle timeout |
| any of the above in a SCPI block | `#<n><length>` immediately followed by one of the signatures above | as above | Exactly `<length>` payload bytes |

A signature split across two reads is still found (the unmatched tail of each read is rescanned with
the next). Plain text — including uppercase text like `OK;ERR;`, a lone VT100 `ESC E`, or `#` in
ordinary output — is not a match. Anything unrecognized is simply not captured and still shows in the
text/hex presenters exactly as before: **the monitor never changes what the main window's output
shows** (its watcher is a presenter that emits no text).

## Files

Every capture is written the moment it completes, to `{export folder}/{device}_{yyyyMMdd-HHmmss}.{ext}`
(`StreamMonitor.FileNameFor`), timestamped with the capture's local start time. `{device}` is the
state line's device name made file-name-safe: letters, digits, `-`, `_` and `.` are kept, any run of
anything else becomes one `_` (`tcp://192.168.0.5:5025` → `tcp_192.168.0.5_5025`, `Bench DMM` →
`Bench_DMM`). Two captures in the same second get `-2`, `-3`, … rather than overwriting. The folder is
created if needed. A save that fails (unwritable folder, a file where the folder should be) is recorded
on the capture (`Not saved: {reason}`) and reported; it never interrupts the connection.

## Actions

| Action | What it does |
|---|---|
| **Device > Stream Monitor...** | Creates the main window's monitor on first use, points it at the current connection, **starts monitoring**, and opens the window (TUI: modal; WPF: non-modal, or brings an already-open one forward). Always enabled, connected or not |
| **Stop Monitoring** / **Start Monitoring** | Toggles monitoring. Stopping unbinds the watcher from the session; a capture in progress is flushed and saved (`stopped`) |
| **Close** (TUI) / window close (WPF) | Closes the window only — **monitoring carries on** until stopped, so captures keep being saved while you're back in the main window sending commands |
| **Open Folder** (WPF) | Opens the export folder in Explorer (created first if missing) |
| **Export As...** (WPF) | Saves a copy of the selected capture's bytes wherever you choose; the automatic file is untouched. Enabled when a capture is selected |
| Selecting a capture | Shows its detail (and, in WPF, its preview) |

While monitoring, each capture also appends a status line to the main window's output:
`Captured 4,213 bytes of BMP image to C:\…\hp34401a_20260923-143512.bmp.` (with a note when it went
quiet, hit the size limit, or was stopped mid-capture), or `…, but could not save it: {reason}`.

## States

- **Running**: the watcher is bound into the current session's live presenter pipeline
  (`Session.AddPresenter`); **Stopped**: it isn't, and nothing is captured.
- **Profile switch** (File > Device Profiles...): a running monitor moves to the new session
  (`StreamMonitor.SetSession`) — a capture in progress on the old one is flushed and saved first — and
  picks up the new device name and export folder. A stopped monitor just remembers the new session.
- **Disconnected**: the monitor stays bound; nothing arrives, so nothing is captured. Reconnecting the
  same session carries on.
- **Main window closing**: the monitor is disposed (stopped).
- **WPF preview**: shows the decoded image for BMP/PNG/JPEG/GIF/TIFF; `Preview not available yet for
  {kind} — the captured bytes were saved as-is.` for HP-GL/PostScript/PCL/unrecognized data;
  `Could not preview this {kind}: {decoder message}` when WPF can't decode it; `Nothing captured
  yet. …` when the list is empty. A truncated (`stopped`/`went quiet`) PNG may still decode and show
  partially — WPF's PNG decoder was found to accept a truncated file without error.

## Per-front-end notes

- **TUI**: no preview by design (Terminal.Gui can't draw images); open the saved file. The window is
  modal like every other TUI screen, which is why closing it doesn't stop monitoring. Device names
  and file names are shown verbatim (`_` is not treated as a hotkey marker).
- **WPF**: live preview + Open Folder + Export As..., per the proposal's "WPF can do better for free
  where a format already has a native decoder".

## Open items

- **No HP-GL/PostScript/PCL preview or rasterize/convert action yet** — the proposal's phase 2, gated
  on the rendering presenter from [presenters.md](../design/presenters.md) §3.
- **No CLI mode** support, and it isn't selectable as a `--presenter` (it emits no text; see the
  proposal's Status for why it's bound in place instead).
- **Only the SCPI command schema can declare a format** today; a device manifest's own command schema
  can't yet.
- **One capture at a time**: a second declared/sniffed stream starting while one is still in progress
  is appended to the first, not captured separately (the proposal's open question on correlation).
- **No retention/cleanup** of the export folder.
- **Not verified against real hardware** yet — the DG1062Z screen capture (a real BMP in a
  definite-length block) and the TDS2024's `HARDCopy STARt` output (BMP/TIFF/EPS/PCL depending on
  `HARDCopy:FORMat`) are the obvious first checks.
