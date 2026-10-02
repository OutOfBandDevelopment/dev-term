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
`--streamconvertmode externaltool`, and appear under **Stream Monitor** in the TUI's Configure screen. The WPF
profile editor has no Stream Monitor section yet.

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

## One tool at a time, for now

dev-term runs the single tool configured above for every capture. To convert PostScript and PCL both, change
the path and arguments between conversions. Registering several tools, each for the formats it handles, is
planned (see `BACKLOG.md`).

## Not verified

These steps are written from Ghostscript's documented command line and dev-term's converter settings. They
haven't been run against a real PostScript capture from a device yet.
