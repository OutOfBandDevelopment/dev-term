# Converting PostScript and PCL captures with Ghostscript

The [Stream Monitor](stream-monitor.md) saves PostScript and PCL print jobs but can't draw them itself.
dev-term draws HP-GL (its own HP-GL to SVG converter) and the image formats Windows decodes, and hands
everything else to a converter you install. **Ghostscript** is the usual one: it turns PostScript into a PNG
that the WPF preview shows. dev-term bundles no converter; it runs the one you point it at.

## Install (Windows)

Ghostscript isn't in winget, so use the official installer:

1. Download the **Ghostscript AGPL Release**, Windows 64-bit (`gs<version>w64.exe`), from
   <https://ghostscript.com/releases/gsdnld.html>.
2. Run it with the defaults. It installs to `C:\Program Files\gs\gs<version>\`.
3. Check it, using the version folder you got:

   ```powershell
   & "C:\Program Files\gs\gs10.04.0\bin\gswin64c.exe" --version
   ```

   If you don't know the folder, `Get-ChildItem "C:\Program Files\gs" -Recurse -Filter gswin64c.exe` finds it.

Use `gswin64c.exe` (console), not `gswin64.exe`, which opens a window for each conversion.

## Configure

Set the mode to **External tool**, point at the executable and give it an argument template. These are the
`Stream Monitor` settings in a saved profile or `appsettings.Local.json`:

```json
"StreamConvertMode": "externaltool",
"StreamConvertExternalToolPath": "C:\Program Files\gs\gs10.04.0\bin\gswin64c.exe",
"StreamConvertExternalToolArguments": "-dBATCH -dNOPAUSE -dSAFER -sDEVICE=png16m -r{dpi} -sOutputFile={output} {input}",
"StreamConvertDpi": 150,
"StreamConvertOutputExtension": "png"
```

(JSON needs the backslashes doubled.) The same names work as command-line flags in lower case, for example
`--streamconvertmode externaltool`. Neither profile editor has fields for these settings; edit the profile JSON or `appsettings.Local.json`.

| Placeholder | Replaced with |
|---|---|
| `{input}` | the capture's saved file |
| `{output}` | the converted file, next to the capture, with `StreamConvertOutputExtension` |
| `{dpi}` | `StreamConvertDpi` |

dev-term splits the template on whitespace and substitutes each piece itself; it never builds a shell command,
so a path with spaces is safe and must **not** be quoted. Keep `-dSAFER`: the PostScript came from a device and
shouldn't be able to touch your files.

## Use it

1. **Device > Stream Monitor...**, then wait for a PostScript capture (or select one in the list).
2. The conversion drop-down (the **Convert as:** button in the TUI) should say **External tool**.
3. Press **Convert...**. The PNG is saved next to the capture and added to the list as "converted from
   PostScript document". WPF previews it; the TUI lists it.

A failed conversion says why in the detail line (a message box in WPF): the tool wasn't found, it exited
non-zero, or nothing was configured.

## PCL

Ghostscript's PCL interpreter is a separate program (**GhostPCL**, `gpcl6win64.exe`) and the standard installer
may not include it. Install a GhostPCL build from the same Artifex downloads page and use the same settings with
its path, for example `-dBATCH -dNOPAUSE -sDEVICE=png16m -r{dpi} -sOutputFile={output} {input}`.

## Several tools at once

Register each converter in the app-wide list instead of the single External tool settings: **Device > Converter
Tools...** (next to Stream Monitor, in both the TUI and WPF) edits it, and every profile and device shares it. Each
entry names the formats it handles, so Ghostscript takes PostScript and GhostPCL takes PCL. The list is stored in
`~/.dev-term/converter-tools.json`, so you can also edit the file directly:

```json
[
  {
    "Name": "gs",
    "Path": "C:\Program Files\gs\gs10.04.0\bin\gswin64c.exe",
    "Arguments": "-dBATCH -dNOPAUSE -dSAFER -sDEVICE=png16m -r{dpi} -sOutputFile={output} {input}",
    "Formats": "ps",
    "OutputExtension": "png",
    "Dpi": 150
  },
  {
    "Name": "gpcl",
    "Path": "C:\gpcl\gpcl6win64.exe",
    "Arguments": "-dBATCH -dNOPAUSE -sDEVICE=png16m -r{dpi} -sOutputFile={output} {input}",
    "Formats": "pcl"
  }
]
```

(A profile's own `StreamConvertTools` still works and is added to this list; an app-wide tool with the same name wins.)

`Formats` is a comma-separated list of format names (`ps`, `pcl`, `hpgl`, `image`) or file extensions (`bmp`); empty
means any. With **Auto (by format)** selected, Convert... runs the first registered tool whose formats include the
selected capture's. Picking a tool by its name in the conversion list (a profile's `"StreamConvertMode": "tool:gs"`) runs it
whatever the capture's format. The conversion list shows Auto and each tool by name once any are registered.
Design: [stream-converter-tools](../design/features/stream-converter-tools.md).

## Verified

Both converters were run for real by `RealGhostscriptConversionTests` (2026-10-02): a PostScript sample through
Ghostscript's `gswin64c.exe` and a PCL sample through GhostPCL's `gpcl6win64.exe`, each producing a PNG. The tests report
Inconclusive on a machine without the tool. A PostScript or PCL capture straight from a device has not been run through
them yet.
