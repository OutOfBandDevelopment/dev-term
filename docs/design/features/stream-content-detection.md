# Stream content detection & rendering window

Sourced from a live conversation while adding real-hardware notes to the SCPI module
(2026-09-23): "an optional window that can be used to watch the data stream for stuff like HPGL,
binary image data and so on and if it's detected it should be presented and able to be exported
and converted/rasterized... if you can't detect the data from the request, having something like
the SCPI command know the response type should allow for proper presentation. I understand if TUI
would only automatically save the files by something like `(device name)_(timestamp).(ext)`."

This is the concrete front-end feature that sits on top of two things already designed but not yet
built in [presenters.md](../presenters.md)'s "Rendering presenters" section (§3): an HPGL/
PostScript/PCL presenter that renders a command stream to a drawing and exports it as SVG/PNG/JPG,
and the general idea that a presenter "declares what representation(s) it produces" so a front end
can show it generically. **Phase 1 is built (2026-09-25)** — see Status below.

## Problem

Today, a device that answers a query with something other than a short text line (a plotter
dumping HPGL, an oscilloscope/DMM screen-dump command returning a bitmap, a printer-language
stream) just shows up as garbage in the ASCII presenter and unreadable bytes in hex — there's no
way to notice that "this looks like a picture" and do something useful with it, short of the user
manually piping the session's raw capture through an external tool by hand.

## Two ways to know what a reply is

Sniffing the bytes will *sometimes* work, but not always — a truly opaque binary reply gives no
signature to recognize, and even signature-recognizable formats cost a false-positive risk. So this
proposes two complementary detection paths, matching the user's own framing ("if you can't detect
it from the request..."):

1. **Declared hint, from the command that triggered the reply.** A device profile already knows
   what a command is supposed to return — the person authoring `hp-agilent-keysight-34401a.json`
   knows `MEAS:VOLT:DC?` returns a text number and a hypothetical `:HCOPY:DATA?` on a scope with a
   screen-dump command returns a bitmap. `ScpiCommandDefinition` gains an optional
   `ExpectedResponseFormat` (an enum: `Text` (default), `Hpgl`, `PostScript`, `Pcl`, `Image`,
   `Binary`). When set, `ScpiControlSurface` passes it along with the existing `QuerySent(id)`
   correlation call (see `IScpiReplyTracker` in
   [scpi-instrument-control.md](scpi-instrument-control.md)) so the watcher below never has to
   guess for that command. This is the general shape [device-manifests.md](../device-manifests.md)
   will eventually want too, once a manifest's own command schema exists independently of SCPI.
2. **Sniffing**, for everything else (an unannotated command, a device with no profile at all, or
   an unsolicited/streamed reply that wasn't triggered by any tracked command). A small
   signature-matching pass over the buffered bytes at the head of a reply: HPGL's ASCII
   two-letter-mnemonic instructions (`IN;`, `SP1;`, `PU`/`PD` coordinate pairs), PostScript's
   `%!PS` header, PCL's `\x1bE`/`\x1b%` escape sequences, and common raster/image magic bytes (BMP
   `BM`, PNG's 8-byte signature, JPEG's `FFD8`, GIF's `GIF8`) are all cheap, well-known, and
   unambiguous enough for a first pass. No claim of perfect detection — an unrecognized binary blob
   still just falls back to hex, exactly as it does today.

## Architecture

A new, opt-in presenter/observer — not a decoder in the existing sense, since its job is
recognition and capture, not turning bytes into a text baseline (plain hex/ASCII already exists
for that, side by side, per presenters.md's "Composability" section):

- **`StreamContentWatcher`** (`DevTerm.Core.Presenters`, name not final): an `IPresenter` that
  buffers a reply's bytes, checks a pending format hint (if `IScpiReplyTracker`-style correlation
  supplied one for this reply) and otherwise runs the signature sniff above, and — once it decides
  a reply is "interesting" (not `Text`) — raises a `ContentDetected` event carrying the format, the
  raw bytes, and a suggested file extension (`.hpgl`, `.ps`, `.pcl`, or the sniffed image
  extension). Registered like any other optional presenter (`--presenter streamwatch`, an entry in
  `ConnectionEditorViewModel.PresenterOptions` — the same "forgot to add k8055/busylight" mistake
  already made twice this project is worth deliberately avoiding a third time here).
- **Front-end window** ("Stream Monitor..." menu item, `_Device`/`Device` menu, opened explicitly —
  it's optional, not automatic, per the user's own framing): subscribes to `ContentDetected`,
  keeps a running list of captures for the session, and offers Export.
- **Naming convention for auto-saved files**: `{deviceName}_{timestamp}.{ext}`, reusing the same
  "saved profile name, else a `tcp://…`/`serial://…`/`hid://…` connection string" resolution the
  Architect's live window title already uses (see `TODO.md`'s Connection Editor entry) for
  `deviceName`, and a sortable timestamp (`yyyyMMdd-HHmmss`) — e.g.
  `hp34401a_20260923-143512.bin`. Saved under a new `~/.dev-term/captures/` directory, mirroring
  the existing `DevTermUserDataPaths` per-user-storage convention.

## Front-end split (TUI vs. WPF) — by design, not a stopgap

Terminal.Gui cannot show real graphics inline (already a known constraint in this codebase — see
CLAUDE.md's headless-color-rendering note; there's no reason to expect raster/vector preview to
fare better than color did). So, as the user already anticipated and accepted:

- **TUI**: no live preview. On detection, auto-saves the raw captured bytes under the naming
  convention above and appends a status/log line ("Captured 4,213 bytes of image/bmp data to
  `~/.dev-term/captures/...`"). This alone is useful today — the file is fully exported the moment
  it's captured, nothing further to click.
- **WPF**: can do better for free where a format already has a native decoder. `System.Windows.Media.Imaging`
  decodes BMP/PNG/JPEG/GIF/TIFF out of the box, so the Stream Monitor window can show a live
  `Image` preview for those with zero new rendering code — only HPGL/PostScript/PCL need the
  not-yet-built rendering presenter from presenters.md §3 before WPF can preview *those* rather
  than just save them raw. WPF still auto-saves every capture the same way TUI does (consistent
  behavior, not a WPF-only convenience), and additionally offers a manual "Export As..." to pick a
  different path/name than the automatic one.

## Phasing

**Phase 1 (buildable now, no new rendering dependencies):**
- `ScpiCommandDefinition.ExpectedResponseFormat` (declared hint).
- Signature sniffer for HPGL/PostScript/PCL headers + common image magic bytes.
- `StreamContentWatcher` presenter + `ContentDetected` event.
- Both front ends: "Stream Monitor..." menu item, capture list, auto-save with the naming
  convention above.
- WPF: native-image-format live preview (BMP/PNG/JPEG/GIF/TIFF) using WPF's own decoders — no new
  parsing code.

**Phase 2 (conversion built; live rendering still ahead):** a manual "Convert" export action wrapping external tools or
the internal HP-GL-to-SVG converter, built 2026-10-01/02 (see the next section and Status). Live vector/raster preview of
PostScript/PCL in the window depends on the HPGL/PostScript/PCL rendering presenter (`presenters.md` section 3, in
`BACKLOG.md`), which is not built.

## Raster/convert tool integration (proposed 2026-09-30)

Sourced from `BACKLOG.md`'s "Proposed Ideas" section: "for the stream monitor, add the ability to
call [a] raster tool — something like ghostscript where path to the tool and arguments can be
mapped, or use a web service like Apache Tika by configuring a web request for conversion — also
support internal conversion tools like a simple HP/GL to SVG tool."

This is the concrete mechanism behind Phase 2's "Convert/Rasterize" export action. As built it does not use
`IExportable` (which was never needed): `StreamCaptureConverter` runs the chosen mechanism against the saved capture file.
Three mechanisms were proposed, offered as alternatives (a capture can be converted by whichever is configured/available, not all three at
once):

1. **External tool invocation** (Ghostscript-style). A new `ExternalConverterOptions` (a per-format
   or a single generic entry — path to the executable, an argument template with placeholders for
   input path/output path/DPI, e.g. `-sDEVICE=png16m -r{dpi} -o{output} {input}`) run as a child
   process (`System.Diagnostics.Process`) against the capture's already-saved file under
   `~/.dev-term/exports`. dev-term never bundles Ghostscript (or any converter) itself — the user
   points at their own install, the same way this project has consistently avoided bundling external
   binaries (USBTMC/HID both implement their protocols directly rather than depending on a vendor
   runtime, per CLAUDE.md's USBTMC note). This is a real command-execution surface — the argument
   template must be built from a fixed placeholder-substitution scheme, never raw user/device data
   concatenated into a shell string, to avoid command injection from a captured file name or a
   device-supplied value.
2. **Web-service conversion** (Apache-Tika-style). *Not built; removed 2026-10-02, a registered script or `curl` covers it.* A configured HTTP endpoint + method the capture's
   bytes are POSTed to, with the converted result read back from the response. Needs explicit
   per-profile opt-in (this sends a capture's raw bytes to an external, user-configured host — no
   default endpoint, ever), and reuses whatever HTTP client/timeout/retry conventions the rest of
   the configuration layer already follows (`Microsoft.Extensions.Http`-based, not a bespoke client).
3. **Internal HP/GL-to-SVG converter.** A small, dev-term-owned HP-GL instruction interpreter
   (`PU`/`PD`/`PA`/`PR`/`SP`/`IN` and a handful of the most common plotter commands) emitting SVG
   `<path>` elements directly — no external dependency, no network call, and (unlike the general
   HPGL/PostScript/PCL rendering presenter from presenters.md §3, which aims at a live on-screen
   preview) this only needs to produce a static SVG file for export. This is a real subset of the
   same parsing work the rendering presenter eventually needs, so it's worth deliberately building
   the HP-GL grammar as a shared, presenter-independent piece from the start rather than duplicating
   it later — the export-only converter and the live-preview presenter both consume the same parsed
   instruction list, just render it differently (one to a static SVG string, one to a canvas).
   PostScript/PCL have no equivalent internal-converter path proposed here (both are materially
   larger grammars); those stay dependent on the external-tool or web-service paths, or on the full
   rendering presenter once it exists.

Configuration lives alongside the existing Stream Monitor settings (per-profile override the same way
`CliOptions.EffectiveExportDirectory` is); the list of registered tools is app-wide, see
[stream-converter-tools.md](stream-converter-tools.md).

## Open questions

- Whether `ExpectedResponseFormat` belongs on `ScpiCommandDefinition` specifically (fastest to ship,
  matches where the idea came from) or on a more general, not-yet-built per-command schema shared
  with `DeviceManifest` (see device-control-modules.md's declarative-schema section) — starting on
  the SCPI type is the pragmatic choice; folding it into a general schema later is a rename/move,
  not a redesign.
- Whether a detected-but-unsolicited capture (no active query, e.g. a device that streams a bitmap
  unprompted) should still work — the sniffer path doesn't need a tracked query id to fire, only
  the declared-hint path does, so this should already fall out of the design as long as the watcher
  is wired into the pipeline unconditionally rather than only checking on `QuerySent`.
- Whether multiple simultaneous "interesting" captures (e.g. two profiles' worth of image-returning
  commands fired in quick succession) need their own correlation queue the way
  `ScpiReplyPresenter`'s FIFO already handles plain replies, or whether one-capture-at-a-time is
  good enough for how these commands are actually used in practice (a screen-dump command is
  usually a deliberate, single, waited-for action, unlike telemetry streaming).
- ~~Retention/cleanup policy for the exports folder~~ **Built 2026-10-03:** `ExportRetention` (`MaxAgeDays`, `MaxFiles`) in `preferences.json`, applied at startup; see [retention](../../user-guide/retention.md). Size-based pruning is not built.  **Owner input 2026-10-03:** also wants the option to enumerate files already on disk, so earlier exports are listed after a restart. **Built 2026-10-03:** `StreamMonitor.LoadFromDisk`.
- ~~Whether the external-tool and web-service converters are worth building~~ **Decided 2026-10-03:** dropped. A script can front them if one is ever needed, and no converter is required out of the box. The internal HP-GL to SVG converter is the one to build, optionally automatic by default. **Built 2026-10-03:** `StreamMonitor.AutoConvertHpgl` (profile `StreamAutoConvertHpgl`, default on).

## Completion checklist

What was needed to close this out. The unbuilt items moved to `BACKLOG.md`.

- [x] Phase 1: sniffer, end finder, `StreamContentWatcher`, declared hint, Stream Monitor in both front ends, WPF native-image preview
- [x] Phase 1 verified on real hardware: DG1062Z BMP and TDS2024 BMP (`docs/test/2026-10-02-18-20-00.md`)
- [x] Conversion: internal HP-GL to SVG, external tool, multiple named tools (`stream-converter-tools.md`)
- [x] WPF SVG preview, verified on a real Tektronix 2230 plot
- [x] Real Ghostscript (PostScript) and GhostPCL (PCL) conversion runs (`RealGhostscriptConversionTests`, 2026-10-02)
- [x] Rewrite the stale Phase 2 and Status text (web service removed; "Phase 2: not started" header was wrong)
- Not built, now in `BACKLOG.md`:
  - Direct in-window preview of PostScript and PCL (needs the rendering presenter, `presenters.md` section 3)
  - SVG drawing in the TUI (it only lists the converted file)
  - CLI-mode "export last N captures" (Stream Monitor has no CLI support)
- [x] Move the unbuilt items above into `BACKLOG.md`, then mark the proposal complete

## Status

**Implemented: Phase 1 on 2026-09-25, conversion (Phase 2) on 2026-10-01/02. Live PostScript/PCL rendering was not built (see `BACKLOG.md`).** Screen reference:
[docs/specs/stream-monitor.md](../../specs/stream-monitor.md); walkthrough:
[docs/user-guide/stream-monitor.md](../../user-guide/stream-monitor.md).

What was built, and where it differs from the text above:

- **Detection is a pure, front-end-independent component in `DevTerm.Core.StreamContent`.**
  `StreamContentSniffer` matches the signatures above (plus TIFF, binary EPS, the PJL universal exit
  language and the HP-GL/2-in-PCL mode switch, and IEEE 488.2 definite-length `#<n><len>` blocks
  wrapping any of them). `StreamContentEndFinder` finds each format's structural end (PNG `IEND`,
  JPEG EOI by walking segments, GIF trailer by walking blocks, PCX by decoding its run-length scanlines (plus the 768-byte palette), BMP/binary-EPS header sizes,
  PostScript `%%EOF`/Ctrl-D, a PJL job's closing UEL). HP-GL, TIFF and bare-reset PCL have no
  reliable in-band end and finish after a 2 s idle gap instead. `StreamContentWatcher` is the
  proposed `IPresenter`: it buffers, captures and raises `ContentDetected`, and never emits text.
  To keep ordinary uppercase text from looking like a plot, HP-GL is only recognized at a reply
  boundary (after a quiet gap or a CR/LF), and a lone `ESC E` (also VT100 NEL) doesn't count as PCL.
- **Declared hint: `ScpiCommandDefinition.ExpectedResponseFormat`**, as proposed (enum
  `StreamContentFormat`: `Text`/`Hpgl`/`PostScript`/`Pcl`/`Image`/`Binary`). It's delivered
  differently, though: not through `IScpiReplyTracker.QuerySent`. `ScpiControlSurface` calls
  `IStreamContentHintSink.ExpectResponse` on every sink in the session's live pipeline
  (`Session.Presenters`) just before sending. So nothing is wired between a control panel and the
  monitor, and a hinted command still just sends when no monitor is running. A declared reply is
  captured whatever its bytes are: by length when it's a definite-length block (header stripped),
  otherwise until idle. The bundled Rigol DG1062Z's `HCOPy:SDUMp:DATA?` declares `Image`. The
  TDS2024's `HARDCopy STARt` doesn't: its output format is itself set by `HARDCopy:FORMat`, so it's
  left to sniffing.
- **Not a selectable `--presenter streamwatch`, deliberately.** The watcher emits no text, so as a
  display presenter it would do nothing visible. Instead, `DevTerm.Configuration.StreamMonitor` binds
  a fresh watcher per session into the live pipeline with `Session.AddPresenter`, the mechanism the
  SCPI panel already uses; the new `Session.RemovePresenter`/`Pipeline.RemovePresenter` unbinds it.
  That lets monitoring be switched on and off mid-connection without reconnecting, keeps its state
  per session, and leaves every other presenter's output unchanged. It also means no
  `ConnectionEditorViewModel.PresenterOptions` entry is needed. The main window owns one
  `StreamMonitor` and calls `SetSession` on every profile switch, so monitoring follows the switch:
  a capture in progress on the old session is flushed and saved first.
- **Files go to the existing `DevTermUserDataPaths.ExportsDirectory` (`~/.dev-term/exports`)**, not a
  new `~/.dev-term/captures/`, via each profile's `CliOptions.EffectiveExportDirectory`. That
  directory and its override already existed for this purpose. Names are
  `{device}_{yyyyMMdd-HHmmss}.{ext}` as proposed, with `-2`, `-3`, … for same-second collisions and
  the device name made file-name-safe.
- **Front ends as proposed.** The TUI window (modal) shows state, folder, a capture list and
  Start/Stop. Because it's modal, monitoring keeps running after it closes, and each capture adds a
  status line to the main output; WPF behaves the same way for consistency. WPF adds a live
  `System.Windows.Media.Imaging` preview for BMP/PNG/JPEG/GIF/TIFF, Open Folder, and Export As....
  HP-GL/PostScript/PCL show "Preview not available yet".
- **Verified**: unit tests only, covering every signature/end finder byte-by-byte, the watcher on a
  fake clock, the monitor over a real `Session`, both windows, and the main windows' wiring including
  a live profile switch. **Verified against real hardware 2026-10-02**: the DG1062Z screen dump
  (`HCOPy:SDUMp:DATA?`, 230,456-byte BMP, USBTMC) and the TDS2024 hard copy (`HARDCopy START`, 77,878-byte BMP,
  19200 baud TCP bridge) each arrive as one capture, saved as `.bmp`; see `docs/test/2026-10-02-18-20-00.md`.
- **Open questions, as answered so far**: unsolicited captures work (sniffing needs no tracked
  query). One capture at a time: a second stream starting mid-capture is appended to the first.
  No retention/cleanup at the time (built 2026-10-03, see above).

**Phase 2, conversion (built 2026-10-01/02):** the external-tool and internal HP/GL-to-SVG mechanisms are
`DevTerm.Configuration.StreamCaptureConverter`, wired into both Stream Monitor windows as "Convert..." next to
Start/Stop Monitoring, with the mechanism chosen in the window. A converted file joins the capture list as its own entry
(`ConvertedFrom`, `StreamMonitor.AddConverted`), and WPF draws an SVG in the preview pane (`SvgPreview`: paths, lines,
polygons, rectangles, circles; no transforms, text or CSS). Several named tools: [stream-converter-tools.md](stream-converter-tools.md).
Verified against a real Tektronix 2230 HP-GL plot converted to SVG and drawn, and Ghostscript and GhostPCL runs
(`RealGhostscriptConversionTests`). The web-service mechanism was removed. Detail in `docs/changes/2026-10-02.md`.

**Not built** (tracked in `BACKLOG.md`): direct in-window preview of PostScript and PCL (needs the rendering presenter),
SVG drawing in the TUI, and a CLI "export last N captures".
