# Capturing screen dumps, plots and print jobs (Stream Monitor)

Some devices answer with something other than a line of text: an oscilloscope or function
generator's screen capture, a plotter's HP-GL, a hard copy in PostScript or PCL. In the main window
those just look like garbage. The **Stream Monitor** watches the connection for them and saves each
one as a file, automatically, the moment it has arrived.

The exact behavior (what's detected, how a capture ends, file naming) is in
[`docs/specs/stream-monitor.md`](../specs/stream-monitor.md).

## Without the window: list or copy captures from a script

`dev-term --listcaptures 5` prints the five newest saved captures; `dev-term --exportcaptures 5 --exportto ./out` copies them into `./out` (an existing file there is never overwritten; a numeric suffix is added). Neither connects to a device.

## Turning it on

Connect as usual, then pick **Device > Stream Monitor...**. Opening it starts monitoring the current
connection. It keeps running after you close its window, so you can close it and go back to the main
window to send the command that produces the image. Each capture then adds a status line to the
main window's output. In the TUI it looks like this (WPF shows the same text as a dimmed status
line):

```text
[dev-term] Captured 10,257 bytes of PNG image to C:\Users\you\.dev-term\exports\Rigol_DG1062Z_20260925-143727.png.
```

Use **Stop Monitoring** in the window to turn it off. Switching to another connection with **File >
Device Profiles...** moves the monitor to the new connection.

## What gets captured

- **Images**: PNG, JPEG, GIF, BMP and TIFF, recognized from their own header bytes.
- **HP-GL** plots, at the start of a reply (for example `IN;SP1;PU0,0;PD…`).
- **PostScript** (`%!PS…`) and **PCL** print jobs.
- Any of those wrapped the way SCPI instruments return binary data (`#9000230456<data>`). The
  `#…` header is stripped, so the saved file is the image itself.

A SCPI command can also say what its reply is. The bundled **Rigol DG1062Z** profile's **Screen
Capture (Bitmap)?** button, under **Device > SCPI Instrument...**, does this. Its reply is captured
even if its bytes aren't recognized. With the Stream Monitor running, press that button and the
screen dump lands in the export folder.

Files are saved as `{device}_{date}-{time}.{ext}` in the connection's export folder
(`~/.dev-term/exports` unless the profile sets another). For example:
`Rigol_DG1062Z_20260925-143708.bmp`. The device name is the saved profile's name, or the connection
(for example `tcp_192.168.0.5_5025`).

Anything the monitor doesn't recognize is not captured. It still shows in the main window's text/hex
output as before. The monitor never changes that output.

## TUI

The terminal can't show images, so the TUI window lists what was captured and where it went. Open
the saved file in any viewer.

![TUI Stream Monitor with three captures](images/tui-stream-monitor.png)

Each row shows the time, type, size, how the capture ended, and the file name:

- **complete**: the data said it was finished.
- **went quiet**: the device stopped sending for 2 seconds. This is normal for HP-GL and TIFF,
  which have no end marker.
- **stopped**: monitoring was stopped part-way through.

The two lines under the list describe the selected capture. **Close** returns to the main window
and leaves monitoring running.

## WPF

WPF shows the same list, plus a live preview of the selected capture for the image formats Windows
decodes itself (PNG, JPEG, GIF, BMP, TIFF):

![WPF Stream Monitor previewing a captured PNG screen dump](images/wpf-stream-monitor.png)

HP-GL, PostScript and PCL are captured and saved, but can't be previewed directly:

![WPF Stream Monitor with an HP-GL capture selected](images/wpf-stream-monitor-hpgl.png)

To see an HP-GL plot, pick **HP-GL to SVG** in the conversion drop-down above the preview and press
**Convert...**. The SVG is added to the list as a new entry (selected, "converted from HP-GL plot") and
drawn in the preview:

![WPF Stream Monitor drawing an HP-GL plot converted to SVG](images/wpf-stream-monitor-svg.png)

The drawing is dev-term's own and covers the simple shapes plots use (paths, lines, polygons, rectangles,
circles), with lines kept thick enough to see when a plot is in plotter units. The `.svg` file itself is
saved next to the capture, so any SVG viewer opens it too. The TUI lists the converted file but doesn't
draw it.

The WPF window also has:

- **Open Folder**: opens the export folder in Explorer.
- **Export As...**: saves a copy of the selected capture somewhere else. The automatic file stays
  where it is.

## Converting a capture

Both windows have a **Convert...** button next to Start/Stop Monitoring, enabled once you've
selected a capture. It runs whichever conversion mechanism the connection is configured for and
writes the result next to the capture's saved file (same folder and name, a new extension).

Choose the mechanism right in the window, next to Convert...: a **conversion** drop-down in WPF
(None, HP-GL to SVG, Auto, each registered tool, External tool) and a **Convert as:** button in the TUI that opens the
same list. The choice applies to that window; the profile's saved mode is just where it starts. For
HP-GL captures, **HP-GL to SVG** needs nothing else. External tool also needs its
path, which comes from the profile.

To save the choice, set the `StreamConvert*` settings in the profile JSON or `appsettings.Local.json` (the profile
editors have no fields for them). The registered tools below are not per profile: they are shared by every device, and are edited from **Device > Converter
Tools...**.

- **Internal HP-GL to SVG** (`internalhpgltosvg`) — no setup beyond picking this mode. Works only on
  HP-GL captures; converts a plotter stream to an SVG file dev-term draws itself, no external tool
  needed.
- **External tool** (`externaltool`) — point at a converter you already have installed (for example
  Ghostscript's `gswin64c.exe`) and give it an argument template with `{input}`, `{output}` and
  `{dpi}` placeholders, e.g. `-sDEVICE=png16m -r{dpi} -o{output} {input}`. dev-term doesn't bundle
  any converter — this just runs the one you point it at. [Installing and configuring Ghostscript](ghostscript-conversion.md) walks through it.
- **Registered tools** (`auto` or `tool:<name>`) — several converters, each declaring
  the capture formats it handles (Ghostscript for PostScript, GhostPCL for PCL). **Auto** picks by the capture's
  format. Manage the list with **Device > Converter Tools...**: add, edit, remove and reorder tools (order
  decides which one Auto tries first); see [the editor's spec](../specs/converter-tools-editor.md). See [the Ghostscript guide](ghostscript-conversion.md#several-tools-at-once).

![Converter tools dialog, TUI](images/tui-converter-tools.png)

![Converter tools dialog, WPF](images/wpf-converter-tools.png)

When a conversion succeeds, the converted file joins the capture list as a new entry, selected, marked
`converted from HP-GL plot` in its detail. In WPF an SVG is drawn in the preview pane, so you can check
a plot without leaving dev-term (simple shapes only: paths, lines, polygons, rectangles, circles).

If nothing is configured, or the conversion fails, Convert... reports why: in the TUI, in the detail
text; in WPF, in the detail text (success) or a message box (failure).

## Not yet

- Drawing a converted file in the TUI (the converted file is listed there, but only WPF draws SVG).
- Stream Monitor in the plain CLI (`--cli true`) mode.
- Automatic cleanup of old files in the export folder.
