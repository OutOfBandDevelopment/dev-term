# Stream Monitor

## Purpose

An optional window (**Device > Stream Monitor...** in both front ends) that watches the current
connection's incoming bytes for renderable/binary content — raster images, HP-GL plots, PostScript
and PCL print jobs — captures each one whole, and auto-saves it as a file. It's for devices that
answer (or spontaneously send) something other than a short text line: an oscilloscope/generator
screen dump, a plotter's HP-GL stream, a hard copy in a printer language. Without it, those bytes
only show up as garbage in the text presenters. The TUI shows a capture list only (Terminal.Gui can't
draw images); WPF adds a live preview for the image formats it decodes natively. Design intent:
[`docs/design/features/stream-content-detection.md`](../design/features/stream-content-detection.md).
How to use it: [`docs/user-guide/stream-monitor.md`](../user-guide/stream-monitor.md).

The shared behavior lives outside both front ends: detection in `DevTerm.Core.StreamContent`
(`StreamContentSniffer`, `StreamContentEndFinder`, `StreamContentWatcher`), capture/auto-save in
`DevTerm.Configuration.StreamMonitor`. The windows are `DevTerm.Console.StreamMonitorMode` (TUI) and
`DevTerm.Wpf.StreamMonitorWindow` (WPF).

## Fields

| Field | TUI | WPF | Notes |
|---|---|---|---|
| State line | `● Monitoring {device}` (the theme's `statusConnected`) / `○ Stopped — {device}` (its menu colors) | Colored dot + `Monitoring {device}` / `Stopped — {device}` | `{device}` is the saved profile's name when the connection is exactly one, otherwise its `tcp://…`/`serial://…`/`hid://…` definition (`StreamMonitor.DeviceNameFor`, the same subject the main window's title shows) |
| Export folder | `Saving to: {folder}` | `Saving to {folder}` (full path in the tooltip) | The connection's `ExportDirectory` (`CliOptions.EffectiveExportDirectory`), default `~/.dev-term/exports`. The user's home folder is shown as `~` (`StreamMonitor.DisplayPath`) |
| Explanation | Two fixed lines | One wrapped line | What's detected, and (TUI) that there's no preview |
| Capture list | `ListView`, one row per capture: `HH:mm:ss  TYPE  size  end  file` | `ListBox`, two lines per capture: `HH:mm:ss — {kind}` / `{size} bytes · {end} · {file}` | Oldest first; the newest is selected whenever one arrives. Keeps the last 100 (`StreamMonitor.MaxRetainedCaptures`) — saved files are never deleted |
| Detail | Two lines under the list: `{kind}, {size} bytes, {end}[ (declared by the command)].` / `Saved as {file} in the folder above.` or `Not saved: {reason}` | Same first line; second line `Saved to {path}` or `Not saved: {reason}` | For the selected capture |
| List criteria | A `Search:` field (same matching as WPF) and a `Sort: {order}` button that cycles Oldest, Newest, Largest, Kind, Device; no separate type/device filters (search matches them) | A search box (device, content type, file name or time, case-insensitive), a content-type filter, a device filter and a sort (Oldest, Newest, Largest, Kind, Device) above the list | One list, not parallel captures. `StreamCaptureView.Apply` (shared, in `DevTerm.Configuration`) does the work. The newest capture by time is selected after each change; nothing matching shows the empty message |
| Preview | — | `Image` for BMP/PNG/JPEG/GIF/TIFF (WPF's built-in decoders, scaled down to fit, never up) or a converted SVG (`SvgPreview`); otherwise a message | See States |

"End" is how the capture finished (`StreamMonitorCapture.EndLabel`):

| Label | Meaning |
|---|---|
| `complete` | The content's own structure (or a SCPI block's declared length) said it was finished |
| `went quiet` | No more bytes for the idle timeout (`StreamIdleTimeoutMs`, default 2 s; HP-GL waits at least 10 s) — the normal end for TIFF and undeclared-length data |
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
| **CLI** | `--listcaptures <n>` prints the newest n saved captures (oldest first); `--exportcaptures <n> --exportto <folder>` copies them out without overwriting (`CaptureExport`). Both read the same folders as the window and exit without connecting |
| **Earlier exports** | When the window opens, files already in the export folders (the profile's folder and `~/.dev-term/exports`) that match `{device}_{yyyyMMdd-HHmmss}[-n].{ext}` are listed too, oldest first, so earlier captures survive a restart (`StreamMonitor.LoadFromDisk`) |
| **Automatic HP-GL to SVG** | An HP-GL capture is also written as an `.svg` beside it and listed as a second capture, with dev-term's own converter. On by default; profile setting `StreamAutoConvertHpgl` (`false` turns it off) |
| **Conversion** drop-down (WPF) / **Convert as:** button (TUI) | Picks the mechanism Convert... uses: None, HP-GL to SVG, Auto, each registered tool (`StreamConvertTools`), or External tool (`StreamConversionChoice.For`). Starts on the profile's `Stream Convert Mode`; changing it affects only this window and isn't saved. The TUI button opens a pick-one list. External tool still reads its path from the profile |
| **Convert...** (TUI + WPF) | Runs the configured conversion mechanism (below) against the selected capture, writing the result next to its saved file (same folder and name, a new extension). Enabled when a capture is selected. TUI reports the outcome in the detail label; WPF reports success in the detail text and a failure via a message box |
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
- **WPF preview**: shows the decoded image for BMP/PNG/JPEG/GIF/TIFF, and draws a converted SVG; `Preview not available yet for
  {kind} — the captured bytes were saved as-is.` for HP-GL/PostScript/PCL/unrecognized data;
  `Could not preview this {kind}: {decoder message}` when WPF can't decode it; `Nothing captured
  yet. …` when the list is empty. A truncated (`stopped`/`went quiet`) PNG may still decode and show
  partially — WPF's PNG decoder was found to accept a truncated file without error.

## Per-front-end notes

- **TUI**: no preview by design (Terminal.Gui can't draw images); open the saved file. The window is
  modal like every other TUI screen, which is why closing it doesn't stop monitoring. Device names
  and file names are shown verbatim (`_` is not treated as a hotkey marker).
- **Web** (`/monitor`, `WebStreamMonitor`): one monitor on the host's shared session, started when a read-write viewer opens the page (a read-only viewer sees it but cannot start/stop) and following a profile switch. Shows the state line, `Saving to` folder, Start/Stop, search, Type and Device filters and Sort (`StreamCaptureView.Apply`), the capture list (newest selected as each arrives, live), the detail lines, a Download link (`GET /api/monitor/captures/{n}`) and an `<img>` preview for BMP/PNG/JPEG/GIF/SVG (a message otherwise). Not on the web yet: Convert, Open Folder, Export As (Download replaces it), extra connections opened from a project.
- **WPF**: the capture list and preview split the width 2:3 (the list at least 220px, at most 420px; it was a fixed 320px), with a 640x380 minimum window size. A capture's second line is the item's own text color at 85% opacity rather than the muted color, so it stays readable on a selected row in a dark theme. Export As... stays button-sized at the top of the details area however many lines the details wrap to. Live preview + Open Folder + Export As..., per the proposal's "WPF can do better for free Under the preview, the detail text spans the full width and the conversion drop-down, Convert... and Export As... sit on their own right-aligned row (they used to share the text's row, which squeezed it to a sliver at the minimum window size).
  where a format already has a native decoder".

## Converting a capture

"Convert..." wraps three mechanisms, configured per connection profile (the `StreamConvert*` settings; the app-wide
converter tools list is edited from **Device > Converter Tools...**, see [converter-tools-editor](converter-tools-editor.md))
and offered as alternatives — one mode is active at a time, never all
three. `DevTerm.Configuration.StreamCaptureConverter` picks the mode and never throws: every failure
(nothing configured, the selected capture was never saved, a process error, a failed HTTP request)
reports an explanatory message instead.

| `Stream Convert Mode` | What it does | Other fields it uses |
|---|---|---|
| (blank/unrecognized) | Convert... always fails with "no conversion mechanism is selected" | — |
| `internalhpgltosvg` | dev-term's own HP-GL-to-SVG converter (`HpglToSvgConverter`) — HP-GL captures only, fails for any other kind | `Stream Convert Output Extension` (default `svg`) |
| `externaltool` | Runs a configured executable against the capture's saved file as a child process | `Stream Convert External Tool Path`, `Stream Convert External Tool Arguments` (a template with `{input}`/`{output}`/`{dpi}` placeholders, e.g. `-sDEVICE=png16m -r{dpi} -o{output} {input}`), `Stream Convert Dpi` (default 150), `Stream Convert Output Extension` (default `png`) |
| `auto` | Runs the first registered tool (app-wide tools first, then the profile's own) whose `Formats` include the capture's (empty `Formats` accepts anything); fails naming the format if none does | `Stream Convert Tools` (a list: `Name`, `Path`, `Arguments`, `Formats`, `OutputExtension` default `png`, `Dpi` default 150), `Stream Convert Output Extension` if set overrides the tool's |
| `tool:<name>` | Runs the registered tool of that name regardless of format; an unknown name is reported when Convert... runs (the validator cannot see app-wide tools) | as `auto` |

The argument template is split on whitespace before `{input}`/`{output}`/`{dpi}` substitution, and
each resulting token becomes its own process argument (`ProcessStartInfo.ArgumentList`, no shell
parsing) — a captured file path containing spaces still arrives as one argument, with no
command-injection risk from a captured file name or a device-supplied value. dev-term bundles no
external converter itself; the external-tool mode points at whatever the user already has installed
(Ghostscript, for example). The built-in web-service mode was removed 2026-10-02: a script or `curl` registered as
a converter tool covers it, and a profile still saying `webservice` now fails validation as an unknown mode.

## Open items

- **Converted files are listed, and WPF draws an SVG** — a successful Convert... adds the output file as a new,
  selected list entry (`converted from HP-GL plot` in its detail; `StreamMonitor.AddConverted`). WPF draws
  `.svg` itself (`SvgPreview`: path, line, polyline, polygon, rect, circle, ellipse with stroke/fill/viewBox;
  no transforms, gradients, text or CSS) and shows `Could not draw this SVG: …` otherwise; the TUI lists it
  but still can't draw (an accepted limit, 2026-10-03: the TUI will not render graphics). The list entry is for this session only (it isn't re-found after a restart). Live
  HP-GL/PostScript/PCL preview is still gated on the rendering presenter from
  [presenters.md](../design/presenters.md) §3.
- **No CLI mode** support, and it isn't selectable as a `--presenter` (it emits no text; see the
  proposal's Status for why it's bound in place instead).
- **Only the SCPI command schema can declare a format** today; a device manifest's own command schema
  can't yet.
- **One capture at a time**: a second declared/sniffed stream starting while one is still in progress
  is appended to the first, not captured separately (the proposal's open question on correlation).
  **Decided 2026-10-03:** keep one list view rather than parallel captures, with filter, search and sort. Built in both
  front ends 2026-10-03 (see List criteria); the correlation question itself stays open.
- **No retention/cleanup** of the export folder.
- **Not verified against real hardware** yet — the DG1062Z screen capture (a real BMP in a
  definite-length block) and the TDS2024's `HARDCopy STARt` output (BMP/TIFF/EPS/PCL depending on
  `HARDCopy:FORMat`) are the obvious first checks.
