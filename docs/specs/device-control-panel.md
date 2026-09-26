# Device Control Panel

## Purpose

A generic renderer (`DevTerm.Console.ControlPanelMode` for the TUI, `DevTerm.Wpf.ControlPanelWindow`
for WPF) that turns any `DevTerm.UiDefinitions.UiDefinition` into real, wired controls against any
`DevTerm.Core.Control.IControlSurface` — one implementation shared by every device control module,
not one screen per device. Opened from each front end's **Device** menu, next to **File**, once a
session is connected. Three menu items use it today:

- **K8055 Control Panel...** — `DevTerm.Devices.K8055`'s fixed `UiDefinition`/`K8055ControlSurface`,
  opens immediately (no picker).
- **Busylight Control Panel...** — `DevTerm.Devices.Busylight`'s fixed `UiDefinition`/
  `BusylightControlSurface`, opens immediately (no picker).
- **SCPI Instrument...** — opens an instrument picker first (see below), then builds the panel from
  whichever `DevTerm.Devices.Scpi.ScpiInstrumentProfile` was chosen
  (`ScpiUiDefinitionBuilder.Build`/`ScpiControlSurface`).

Each menu item is **enabled only when the current connection could be that device**
(`DevTerm.Configuration.DevicePanels.IsAvailable`), and all three are disabled while disconnected:

| Item | Enabled when connected over |
|---|---|
| K8055 Control Panel... | HID, vendor `0x10CF`, product `0x5500`–`0x5503` (the four board-address jumper settings) |
| Busylight Control Panel... | HID, `0x04D8:0xF848` (the bench unit, real-hardware confirmed) or any Plenom `0x27BB` device |
| SCPI Instrument... | any transport except HID (SCPI is text over a byte stream; HID is fixed-size binary reports) |

The items follow the connection live: they're recomputed on every connect, disconnect, self-disconnect
and profile switch, the same pass that refreshes the main window's title and status bar. See [`docs/design/ui-definitions.md`](../design/ui-definitions.md) and
[`docs/design/device-control-modules.md`](../design/device-control-modules.md) for the design intent
behind this being generic, and
[`docs/design/features/scpi-instrument-control.md`](../design/features/scpi-instrument-control.md)
for the SCPI module specifically.

## Fields

The control set is data-driven (one row per `UiControl` in the definition), not a fixed list — see
`DevTerm.UiDefinitions.UiControl`'s subtypes for the full set. Per subtype:

| Control kind | TUI widget | WPF widget | Notes |
|---|---|---|---|
| `ButtonControl` | `Button` | `Button` | Sends `CommandId ?? Id` with a `null` value on click, unless one of the two variants below applies |
| `ButtonControl` with `ColorPickerTargetCommandId` | `Button` that opens a nested RGB/HSV color-picker `Dialog` | `Button` that opens `ColorPickerWindow` | Sends the picked color as `"{r},{g},{b}"` to the *target* command id, not the button's own id. The picker opens on the color last picked for that button (`LastPickedColors`, keyed by the button's control id), including after closing and reopening the panel. It starts on white until a color has been picked, and it's kept only for the life of the process. Next to the button, a swatch shows that color's `#RRGGBB` hex value, with the color as its background and black or white text by luminance. It's hidden until a color has been set; WPF uses a `Border` (`ColorSwatches`), the TUI a `Label` registered as `"{id}.swatch"` in `ControlViews`. The WPF picker opens sized to its content, is resizable, and scrolls its controls, with OK/Cancel always visible |
| `ButtonControl` with `ParameterFieldIds` | `Button` | `Button` | On click, reads each named sibling control's *current* value (see below), joins with `,`, sends that as one value to `CommandId ?? Id` — this is how a command with parameters (e.g. a SCPI command taking a frequency) gets a "fill in fields, press one button" flow without a bespoke form per command |
| `ToggleControl` | `CheckBox` | `CheckBox` | Sends `"1"`/`"0"` on every change |
| `SliderControl` | Bounded `TextField` (no drag widget in the installed Terminal.Gui) + a `[min-max]unit` hint label | Real `Slider` + a live value label | Commits on Enter (TUI) / on every drag (WPF). TUI: validated on commit (see Validation) — a number is clamped to `[Minimum, Maximum]`, anything else is rejected and not sent |
| `NumericControl` | Bounded `TextField` + a `[min-max]unit` hint label | `TextBox` | Commits on Enter (TUI) / on Enter or losing focus (WPF). Validated on commit in both: a number is clamped to `[Minimum, Maximum]` and written back normalized; unparsable input is rejected and not sent (it used to silently send `DefaultValue` instead) |
| `ChoiceControl` | `OptionSelector` (horizontal) regardless of `ChoiceStyle` — no radio-group/combo-box widget in the installed Terminal.Gui | `RadioButton` group when `ChoiceStyle.RadioGroup`, otherwise `ComboBox` | Sends the selected option string on change |
| `TextFieldControl` | `TextField` | `TextBox` (`MaxLength` set directly when `TextFieldControl.MaxLength` is set) | Commits on Enter (TUI) / on Enter or losing focus (WPF); TUI truncates to `MaxLength` on commit. With a `Constraint` (`ValueConstraint`: `Text`/`Integer`/`Number`, optional `Minimum`/`Maximum`), the typed value is validated on commit — rejected and not sent when invalid or (by default) out of range, normalized when valid (`" 3.0 "` → `"3"`) |
| `IndicatorControl` | Read-only `Label` | Read-only, bold `TextBlock` | Never sends anything; updated only by a live `IStructuredPresenter.ValuesChanged` event keyed by the control's id — see States |

"Current value" for `ParameterFieldIds` (both front ends): a text field/box's text, a choice
control's selected option string, a toggle's `"1"`/`"0"`, a WPF slider's position, or an
indicator's displayed text — read at click time, not cached, and validated first (see Validation):
if any field is invalid, nothing is sent.

### Layout

- **Sections are collapsible, expanded by default.** WPF: one `Expander` per labeled section
  (`SectionExpanders`). TUI (no expander widget): a focusable header button reading `[-] Name`
  (expanded) or `[+] Name` (collapsed) — Enter/Space or a click toggles it (`SectionHeaders`,
  `SectionBodies` in `ControlPanelWindowParts`); every section below moves up or down to match, so a
  collapsed section leaves just its header line, no gap. An **unlabeled** section (Busylight's lone
  Apply button) has nothing to name a header with, so it has no header and is always shown.
- **Labels are aligned per section and never wrap.** WPF: a two-column `Grid` per section — an
  auto-sized, `NoWrap` label column, then the controls. TUI: every control in a section starts at the
  section's longest `Label:` plus one space.
- **Notes come last.** A definition-level `Description` (if set — e.g. a SCPI profile's `Notes`)
  renders as a collapsible **Notes** section after all the others, text wrapped (WPF: a wrapping
  `TextBlock`, the scroll area has no horizontal scrolling so it wraps to the window; TUI: word-wrapped
  once, at build time, to the screen width). It used to be one line above the sections (cut off in
  the TUI).
- A status line at the bottom reads "Not decoding — connect with the matching `--presenter` to see
  live values." whenever the presenter passed in isn't an `IStructuredPresenter` (K8055/Busylight
  always are; SCPI needs the `scpi` presenter selected on the connection — see Open items). TUI: at
  the end of the scrolling form. WPF: the red `StatusText` line, below the scroll area.

### Command preview

A control that sends a command shows exactly what it would send, when the panel's
`IControlSurface` also implements the optional `DevTerm.Core.Control.ICommandPreview`
(`PreviewCommand(commandId, value)`, which never sends or changes state). The preview uses the
control's **current** value: the slider position, the typed text (validated first — an invalid value
shows `Won't send: <reason>`), the selected choice, a parameter button's joined, validated
parameter fields — and for a toggle, the state toggling it would send. A control gets a preview only
when the surface returns one for it; a parameter field that only feeds a button (a SCPI command's
parameter, the Custom Command text field) shows none itself — its button does.

| Surface | Preview |
|---|---|
| `ScpiControlSurface` | The command text with the profile's terminator appended and control characters escaped (`MEAS:VOLT:DC? DEF\n`, `VSET1:05.00` for a terminator-less Korad) — built by the same code path `InvokeAsync` sends through |
| `K8055ControlSurface` | The 9-byte HID report as hex (`00 05 01 00 00 00 00 00 00`), including the output state the change would produce, without committing it |
| `BusylightControlSurface` | Only **Apply** sends anything, so only it has a preview: the frame as hex. Color/blink/sound controls only change state, so they have none |

- **WPF**: an "ⓘ" icon after the control (`InfoIcons`); its tooltip is recomputed each time it opens.
- **TUI** (no hover): a `(i)` marker after the control (`InfoMarkers`), and a footer line pinned
  below the scrolling form (`PreviewLabel`) reading `Sends: MEAS:VOLT:DC? DEF\n` while that control —
  or a parameter field feeding it — has focus. It updates as the value changes and clears when focus
  moves to a control that sends nothing.

### Validation

Every typed value is checked by the one shared `DevTerm.UiDefinitions.ValueValidator` before it's
sent — on a field's own commit and when a parameter button reads it. The constraint comes from
`ValueValidator.ConstraintFor(control)`: a `TextFieldControl`'s declared `Constraint`, or the
clamped `[Minimum, Maximum]` number range a `SliderControl`/`NumericControl` implies. An invalid value
is **not sent**, and the reason is shown as `<Label>: <reason> Not sent.` (e.g.
`Channel: 9 is out of range (1 to 4). Not sent.`) — TUI: the footer's second line (`MessageLabel`);
WPF: the red `StatusText` line (never a modal). The next valid commit clears it. A valid number is
normalized (invariant culture, `1e3` → `1000`) and, for a field's own commit, written back into it.

Rough layout (a SCPI panel, since it exercises the most control kinds — numeric field with a
parameter button, a query button with an indicator, and the always-present Custom Command section):

```plantuml
@startsalt
{
  {* File | Device | Help}
  {
    [-] Source
    Frequency (Hz): | "1000.0" [1-30000000]Hz
    Set Frequency:  | [Set Frequency] (i)
  }
  {
    [-] Measure
    Measure Voltage:       | [Measure Voltage] (i)
    Measure Voltage Reply: | "DC VOLT +1.23450000E+00"
  }
  {
    [+] Configure
  }
  {
    [-] Custom Command
    Command: | "*IDN?"
    Send:    | [Send] (i)
    Reply:   | ""
  }
  {
    [-] Notes
    Confirmed against real hardware over RS-232: 9600 baud,
    8 data bits, 2 stop bits ...
  }
  {
    Sends: *IDN?\n
  }
}
@endsalt
```

## Actions

| Action | Behavior | Preconditions | On failure |
|---|---|---|---|
| **Device > K8055 Control Panel...** / **Busylight Control Panel...** | Opens the panel immediately, built from that device's fixed `UiDefinition` and a fresh `IControlSurface` over the current session | None checked | n/a |
| **Device > SCPI Instrument...** | Binds the `scpi` presenter into the session's live pipeline if it wasn't already part of it (`Session.AddPresenter` — see below), resolves a profile choice, then opens the panel built from it (see "Picking a SCPI profile" below) | None checked | n/a |
| **Interacting with a control** (button click, toggle, commit a field, move a slider, pick a choice) | Calls `IControlSurface.InvokeAsync(id, value)`, which — for the SCPI surface — formats the command's template, appends the profile's terminator, and sends it over the live session; for K8055/Busylight, drives the device directly over HID reports | Session must actually be open for the send to succeed | A value the surface rejects, or a device-side failure, is shown in the panel and never escapes. The TUI shows an error dialog ("dev-term — command failed"), since the modal panel hides the main output pane. WPF shows `Command failed: …` in the panel's red status line; it uses no modal so the panel stays testable. A device-side failure has also disconnected the session (`Session.Disconnected`), and the main window reports that too |
| **Committing an invalid value** (a field's Enter/blur, or a parameter button) | Nothing is sent; the reason appears in the panel (see Validation) | n/a | n/a |
| **Expanding/collapsing a section** | WPF: click the `Expander` header. TUI: focus the `[-]`/`[+]` header and press Enter/Space, or click it; the sections below reflow | Section has a label | n/a |
| **Seeing what a control sends** | WPF: hover its "ⓘ" icon. TUI: move focus to it (or to a parameter field feeding it); the footer shows `Sends: …` | Surface implements `ICommandPreview` and sends something for that control | n/a |
| **Scrolling the form (TUI only)** | PageUp/PageDown (global, works regardless of focus) or mouse wheel scroll the form when it's taller than the window; moving focus to a control below the fold scrolls it into view automatically | Form taller than the visible window | n/a |
| **Closing the panel** | TUI: a nested `Application.Run` — closing the window ends that loop and returns to the parent screen. WPF: an ordinary (non-modal) `Window` — closing it just closes it | None | n/a |

### Picking a SCPI profile

Opening **SCPI Instrument...** first resolves a choice, in this order:

1. If the connected profile has a saved `CliOptions.ScpiProfile` that still resolves to a real choice
   (a loaded profile's name, `"Auto-detect (*IDN?)"`, or `"Generic (manual)"`), that's used directly —
   **no picker shown**.
2. Otherwise, a picker lists every `ScpiProfileCatalog.All` profile name plus the two synthetic
   choices, "Auto-detect (*IDN?)" and "Generic (manual)" (TUI: a `PickFromList` modal `Dialog`
   wrapping a `ListView`, mirroring `ConfigureMode`'s own list-picker pattern; WPF:
   `ScpiInstrumentPickerWindow`, a small `ListBox` + Select button, or double-click a row). Cancelling
   either does nothing — no panel opens.

Choosing a named profile opens the panel immediately. Choosing **Generic (manual)** opens the panel
built from `ScpiProfileCatalog.Generic` (`*IDN?`, `*RST`, `*CLS`, `*OPC?`, plus the always-present
Custom Command section — see below). Choosing **Auto-detect (*IDN?)** sends `*IDN?` over the live
session and regex-matches the reply against every loaded profile's `IdnPattern`
(`ScpiProfileCatalog.TryMatchByIdn`, via the shared `DevTerm.Devices.Scpi.ScpiAutoDetect`). This is a
real async round-trip, so it can't finish synchronously inside the menu click. The wait is
`CliOptions.ScpiAutoDetectTimeoutMs`: 3000 ms by default, 100–60000, `--scpiautodetecttimeoutms`, saved
in a profile only when changed. Progress is shown while it waits:
- The main window's output gets `Auto-detecting the SCPI instrument: sent *IDN?, waiting up to 3 s…`
  (WPF also shows a wait cursor).
- Afterward it names the outcome: `Detected {profile} (*IDN? replied "…")`,
  `No loaded SCPI profile recognizes *IDN? reply "…" — opening the Generic panel.`, or
  `No *IDN? reply within 3 s — opening the Generic panel.`

The panel opens once a reply arrives or the wait elapses, falling back to `Generic` when nothing matched. Correlating the reply
needs the `scpi` presenter active in the session's pipeline; opening **SCPI Instrument...** always
binds it in first (`Session.AddPresenter`, see Actions above) regardless of whether it was selected
when the connection was made, so auto-detect (and every command's reply afterward) works either way.

```plantuml
@startuml
autonumber
actor User
participant "Device menu\n(TuiMode / MainWindow)" as Menu
participant Session
participant "scpi presenter\n(ScpiReplyPresenter)" as Presenter
participant ScpiProfileCatalog as Catalog

User -> Menu: SCPI Instrument... -> Auto-detect (*IDN?)
Menu -> Presenter: QuerySent("scpiAutoDetect.reply")
Menu -> Session: SendAsync("*IDN?\n")
Session -> Presenter: incoming line
Presenter -> Menu: ValuesChanged({"scpiAutoDetect.reply": line})
alt matched within 3s
  Menu -> Catalog: TryMatchByIdn(line)
  Catalog --> Menu: matching profile
else no reply / no match in 3s
  Menu -> Catalog: Generic
end
Menu -> User: opens control panel for the resolved profile
@enduml
```

Every SCPI profile — named, generic, or auto-detected — also gets an always-present **Custom
Command** section: a text field, a Send button (`ParameterFieldIds`-driven, sends the typed text
verbatim with no template substitution), and an indicator showing the reply — the escape hatch for
any command not in the curated list.

## States

- **Live reply indicators**: when the `IPresenter` passed in also implements
  `DevTerm.Core.Presenters.IStructuredPresenter` (true for K8055/Busylight always; true for SCPI only
  when the `scpi` presenter is selected on the connection), the panel subscribes to its
  `ValuesChanged` event for its own lifetime and updates any `IndicatorControl` whose id is a key in
  the published dictionary. Unsubscribed on close/disposal in both front ends.
- **SCPI query correlation**: `ScpiControlSurface` calls `IScpiReplyTracker.QuerySent("{commandId}.reply")`
  before sending a query command; `ScpiReplyPresenter` (the `scpi` presenter) FIFO-matches the next
  complete line it receives to the oldest pending query id and publishes it as that indicator's value.
  An unsolicited line with nothing pending still renders as ordinary output text, just with no
  indicator update — this is a synchronous, one-command-at-a-time correlation, not built for
  concurrent overlapping queries (SCPI itself doesn't really support that either).

## Per-front-end notes

- **TUI has no native slider, combo box, or radio-group widget** (checked directly against the
  installed Terminal.Gui v2.5.0) — `SliderControl`/`NumericControl` both render as a bounded
  `TextField`, and every `ChoiceControl` (regardless of `ChoiceStyle`) renders as a horizontal
  `OptionSelector`, unlike WPF's real `Slider`/`RadioButton`/`ComboBox`. See
  `ControlPanelMode`'s own class remarks.
- **TUI's form scrolls the same way `ConfigureMode`'s does** — PageUp/PageDown wired via the global
  `Application.KeyDown` event (not a per-view handler), plus auto-scroll-into-view on focus change
  (per row, using the positions the last expand/collapse computed rather than `Frame`, which lags
  until the next layout pass). The two footer lines (preview, validation message) sit below the
  scrolling form, so they're always visible. WPF needs none of this; its `StackPanel`/`ScrollViewer`-
  based layout (via the containing `Window`) grows and lets the OS scroll naturally.
- **TUI has no expander, tooltip, or hover** — hence the `[-]`/`[+]` header buttons and the focus-driven
  `Sends:` footer instead of WPF's `Expander` and "ⓘ" tooltip.
- **WPF's control-panel windows are non-modal** (`ControlPanelWindow.Show()`, not `ShowDialog()`) —
  the main window stays usable while a control panel is open; the TUI's is a nested
  `Application.Run`, which blocks the parent screen (same trade-off `ConfigureMode` already makes for
  Device Profiles).
- **The SCPI picker is two separate small UIs** (`TuiMode.PickFromList`,
  `DevTerm.Wpf.ScpiInstrumentPickerWindow`) rather than one shared component, mirroring this
  codebase's general pattern of front-end-specific screens over the same shared
  catalog/profile/builder logic.

## Open items

- **The TUI's Notes wrap once, at the screen width the panel opened at** — resizing the terminal
  afterwards doesn't re-wrap them.
- **Long TUI rows can run past the right edge** (e.g. the 34401A's "Configure 4-Wire Resistance
  Range" button pushes its `(i)` marker off-screen) — the form scrolls vertically only.
- **Expand/collapse state isn't remembered** — every section opens expanded each time the panel opens.
- **`DeviceManifest`'s own `UiDefinition` isn't wired to this renderer yet** — a manifest can declare
  a `UiDefinition` today, but nothing yet resolves a loaded manifest into a live `IControlSurface`/
  panel the way K8055/Busylight/SCPI do (see
  [`docs/design/device-manifests.md`](../design/device-manifests.md)).
