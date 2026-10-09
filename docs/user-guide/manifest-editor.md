# Editing a device manifest

A [device manifest](../design/device-manifests.md) describes a device with no code: its commands,
how to read its replies, and the control panel **Device > Device Manifest...** opens for it. The
manifest editor lets you build one, or change an existing one, without hand-editing JSON — and shows
the resulting panel as you go. Full field/action reference:
[`docs/specs/manifest-editor.md`](../specs/manifest-editor.md).

Open it from **Device > Edit Device Manifest...** in the TUI or WPF. It doesn't need a connection.

## The editor

The **outline** on the left lists every part of the manifest: its identity, each command (and each
command's parameters), each response pattern, and the panel's sections and controls. Selecting one
shows its form. WPF, with the Loopback Sensor Demo open and its **Stream Samples** command selected:

![WPF manifest editor: outline, a command's form, and the live panel preview](images/wpf-manifest-editor.png)

The same in the TUI, which has room for the outline and one pane:

![TUI manifest editor: outline and a command's form](images/tui-manifest-editor.png)

A command's form shows what it will send (**Sends**, with each parameter's default value and the
manifest's terminator filled in). Templates and the terminator are typed with escapes — `\n`, `\r`,
`\t`, `\x03`, `\\` — so a line ending is visible and editable in a one-line field.

A panel control's form only shows the fields that control's **Kind** has — a bar graph's channels
and range, a slider's step, a vector display's coordinate ids. Changing the Kind turns the control
into the new kind, keeping its id, label and help text:

An indicator's **Expression** field, a chart's **Channels** (`id:label:color:expression`), and a
button's **Parameter expressions** let a field derive its value instead of showing a raw decoder
value verbatim — `{raw_mv} / 1000` to show millivolts as volts, `round({voltage} * {current}, 2)` to
compute and send a derived value. See [`docs/specs/manifest-editor.md`](../specs/manifest-editor.md)'s
Fields table for the exact grammar and syntax.

An indicator's Expression field has a **Pick...** button that opens the expression picker: choose from the
manifest's own values (with type, unit and range), insert `round`/`min`/`max`/`abs`/`if`/`matches`, and watch the
diagnostics and the result against sample data update as you type. **OK** puts the expression back in the field.
Every field that names a value has a **Pick...** button too: a bar graph or strip chart's **Channels** (choosing a value
adds a channel), a button's **Parameter expressions**, a vector's coordinate ids, a color control's hue/saturation/brightness ids
and **Visible when**.
The full manual, with the whole expression language and every picker mode, is [Expressions and the expression picker](expression-builder.md). Reference: [`docs/specs/expression-picker.md`](../specs/expression-picker.md).

![WPF expression picker](images/wpf-expression-picker.png)

![TUI expression picker](images/tui-expression-picker.png)

![WPF manifest editor: a bar graph control's form](images/wpf-manifest-editor-control.png)

![TUI manifest editor: a bar graph control's form](images/tui-manifest-editor-control.png)

A response pattern's form has a **Sample line** box: paste a reply line there and **Publishes** shows
the values the pattern's regex would publish from it (or `no match`, or the regex's error). The
sample isn't saved.

**Add** (its label follows the selection: *Add command*, *Add parameter*, *Add pattern*, *Add
section*, *Add control*), **Remove**, **Up** and **Down** sit under the outline. A manifest with no
panel of its own uses one generated from its commands (a button per command, a field per parameter,
a reply line per query); select **Panel (generated)** and press **Create panel from commands** to
start from that and design your own.

## Describing a binary reply

A device that answers in binary rather than text has its reply layout under **Binary frame**: a
sync prefix and a list of fields (number, text, raw bytes, or skipped), each publishing a value the
panel can show like any pattern's. Press **Import .ksy...** (WPF) or **Ksy** (TUI, the button at
the right of the bottom row) to fill it from a Kaitai Struct file instead of typing each field, or
**Add field** to build it by hand. The status line reports what was imported and what the importer
couldn't express.

![WPF manifest editor: an imported binary frame and one of its fields](images/wpf-manifest-editor-frame.png)

![TUI manifest editor: an imported binary frame and one of its fields](images/tui-manifest-editor-frame.png)

Imported nested types show up as dotted names (`header.length`) and fixed-count repeats as indexed
ones (`samples[0]`); use them in an expression as `{header.length}` and `{samples[0]}`.

## Previewing the panel

WPF shows the panel on the right the whole time, rebuilt after every edit. In the TUI, press
**Preview** to swap the form for the panel (and **Edit** to swap back):

![TUI manifest editor: the live panel preview](images/tui-manifest-editor-preview.png)

It's the real panel, drawn the way **Device > Device Manifest...** draws it, but it sends nothing:
pressing a button or committing a field shows *what it would send* (the TUI's status line, under the
WPF preview), and the ⓘ/(i) previews work as they do on a live panel.

## Saving

**Save** checks the manifest first, with the same checks dev-term runs whenever it loads one, and
only writes it if they pass — so what you save always loads. Problems that would stop it loading
(a command with no template, two commands with the same id, a regex that doesn't compile, ...) are
listed in the status line and nothing is written; warnings (a button whose command doesn't exist, a
template placeholder no parameter fills) are listed but don't stop the save. **Check** runs the same
checks without saving.

Where it saves:

- A **new** manifest, or one you opened from the manifests that come with dev-term (or from a
  `.zip`), is saved as your own copy in `~/.dev-term/manifests/<folder>/device.json`. The folder is
  the one the manifest came from (so your copy of the bundled `loopback-sensor-demo` replaces it by
  name for your profiles), or else the manifest's name in lower case with dashes. You're asked before
  it overwrites a manifest already there.
- One you opened from anywhere else is saved back where it came from.
- **Save As...** saves to a `.json` file (or a folder, as its `device.json`) of your choice, and keeps
  saving there.

**Open...** takes a manifest from the same list **Device > Device Manifest...** offers, or any path to
a manifest file, folder, or `.zip`. It opens a manifest even if it has errors, so you can fix it.
**New**, **Open...** and **Close** ask before throwing away unsaved changes; the title bar ends in
` *` while there are some.

## Previewing with a recorded session

Press **Use recording...** (WPF) or **Log** (TUI, next to **Ksy**) and pick a session log you captured earlier. The preview panel and the expression pickers then show the values the device really sent, in order, instead of generated ones; **Next sample** steps through them. Press the button again to go back to generated data.

## On the web

Open `/manifest` (the Manifest link) for the same editor in a browser: choose a manifest under Open or press New, pick a part in the Outline, edit its form, then Check and Save. See the [spec](../specs/manifest-editor.md#web) for what differs from the desktop apps.

![Manifest editor on the web](images/web-blazor-manifest.png)
