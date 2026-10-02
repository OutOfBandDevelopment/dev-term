# Converter tools editor

The dialog that manages the Stream Monitor's registered converter tools (`CliOptions.StreamConvertTools`). It is
opened from the **Stream Monitor** section of the connection editor (File > Device Profiles... > Edit in WPF, the
Configure screen in the TUI) by **Edit tools...**. Design: [stream-converter-tools](../design/proposals/stream-converter-tools.md).

Logic is shared: `DevTerm.Configuration.ConverterToolsEditor` (a working copy of the list). WPF renders it as
`ConverterToolsWindow`, the TUI as `ConverterToolsDialog`.

## Fields (per selected tool)

| Field | Meaning |
|---|---|
| Name | Unique (case-insensitive); what `tool:<Name>` and the Convert... list show. |
| Path | The executable. WPF has a Browse... button. |
| Arguments | Template with `{input}`, `{output}`, `{dpi}`. New tools start as `{input} {output}`. |
| Formats | Comma list of capture formats the tool handles (`ps`, `pcl`, `hpgl`, `image`, `bmp`...); empty means any. |
| Output extension | Extension of the converted file; a leading `.` is stripped. Default `png`. |
| DPI | Positive integer substituted for `{dpi}`. Default 150. |

## Actions

- **Add** appends a tool named `toolN` and focuses Name. **Remove** deletes the selected tool and selects its neighbour.
- **Up / Down** reorder. Order matters: Auto mode picks the first tool, in list order, whose Formats match.
- **OK** validates and, if clean, returns the edited list; otherwise the first problem is shown in the dialog and it stays
  open. Checks: blank name, duplicate name, blank path, DPI not above zero, blank output extension.
- **Cancel** discards every change (the dialog edits copies).

The connection editor shows a read-only **Converter tools** summary ("2 converter tools: gs, gpcl"). Accepting the dialog
replaces the list and marks the profile dirty; saving writes `StreamConvertTools` (and the other `StreamConvert*`
settings, which are now carried through the editor rather than dropped) to the profile JSON.
