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

**Phase 2 (depends on the HPGL/PostScript/PCL rendering presenter — genuinely new parsing/rendering
work, already backlogged in presenters.md §3 and `BACKLOG.md`, not rushed here):**
- Live vector/raster preview of HPGL/PostScript/PCL captures in the Stream Monitor window, reusing
  that presenter's canvas.
- A manual "Convert/Rasterize to PNG at DPI N" export action wrapping that presenter's own
  `IExportable` implementation, once it exists — for TUI too, as a non-graphical "export the last
  N captures to PNG" command even though it can't preview them.

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
- Retention/cleanup policy for `~/.dev-term/captures/` — nothing today prunes old files there
  automatically; low priority until real usage shows it matters.

## Status

**Phase 1: implemented 2026-09-25. Phase 2: not started.** Screen reference:
[docs/specs/stream-monitor.md](../../specs/stream-monitor.md); walkthrough:
[docs/user-guide/stream-monitor.md](../../user-guide/stream-monitor.md).

What was built, and where it differs from the text above:

- **Detection is a pure, front-end-independent component in `DevTerm.Core.StreamContent`.**
  `StreamContentSniffer` matches the signatures above (plus TIFF, binary EPS, the PJL universal exit
  language and the HP-GL/2-in-PCL mode switch, and IEEE 488.2 definite-length `#<n><len>` blocks
  wrapping any of them). `StreamContentEndFinder` finds each format's structural end (PNG `IEND`,
  JPEG EOI by walking segments, GIF trailer by walking blocks, BMP/binary-EPS header sizes,
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
  a live profile switch. **Not verified against real hardware yet**: the DG1062Z screen capture and
  the TDS2024 hard copy are the obvious first checks.
- **Open questions, as answered so far**: unsolicited captures work (sniffing needs no tracked
  query). One capture at a time: a second stream starting mid-capture is appended to the first.
  No retention/cleanup.

**Phase 2 (still ahead):** HP-GL/PostScript/PCL preview and a rasterize/convert export, gated on the
rendering presenter from [presenters.md](../presenters.md) §3.
