# Controlling a device from a panel, not just typed commands

Some devices get a real control panel instead of typing raw bytes — buttons, sliders, toggles, and
fields generated from that device's own declared layout. Once connected (see
[Connecting to a device](connecting.md)), open **Device** in the menu bar (TUI and WPF both) to see
what's available. Full field-by-field/action-by-action reference:
[`docs/specs/device-control-panel.md`](../specs/device-control-panel.md).

## K8055 and Busylight: open and go

**Device > K8055 Control Panel...** and **Device > Busylight Control Panel...** each open
immediately — no setup, no picker. These two devices have one fixed, built-in layout apiece:

- **K8055** (a generic USB HID digital/analog I/O board): digital output toggles, analog output
  sliders, and digital-input/analog-input indicators that update live as the device reports them.

![TUI K8055 control panel, with a live decoded input report showing Analog In/Digital In values](images/tui-control-panel-k8055.png)

![WPF K8055 control panel, with a live decoded input report showing Analog In/Digital In values](images/wpf-control-panel-k8055.png)

- **Busylight** (a USB HID RGB status light): color buttons (including a custom RGB/HSV color
  picker) and an on/off toggle.

![TUI Busylight control panel](images/tui-control-panel-busylight.png)

![WPF Busylight control panel](images/wpf-control-panel-busylight.png)

**Custom...** opens the RGB/HSV picker. It opens on the last color you picked, even if you've closed
and reopened the panel since; before any pick it starts on white. Once a custom color has been set,
a swatch next to the button shows its hex value on a background of that color. Here the color is
`#FF6600`:

![TUI Busylight control panel with a custom color set](images/tui-control-panel-busylight-custom-color.png)

![WPF Busylight control panel with a custom color set](images/wpf-control-panel-busylight-custom-color.png)

The WPF picker opens sized to fit its controls and can be resized. If you make it shorter than its
controls they scroll, and OK/Cancel stay pinned at the bottom (they used to be cut off, with no way
to resize the window to reach them):

![WPF custom color picker](images/wpf-color-picker.png)

The chosen color is remembered only while the app is running, not across restarts.

Both only make sense connected to that actual device over HID — opening either panel against an
unrelated connection just won't do anything useful.

## SCPI instruments: pick a profile first

**Device > SCPI Instrument...** is for SCPI-compatible bench equipment (multimeters, power supplies,
scopes, function generators). Unlike the two panels above, one menu item covers many different
instruments, so it needs to know which one you're talking to before it can build a panel:

- If the connection you're using already has a saved instrument choice (see
  [Managing connection profiles](managing-profiles.md) — it's part of a saved profile, same as the
  transport settings), the panel opens straight away with no extra step.
- Otherwise, a small picker appears listing every built-in instrument profile plus two extra
  choices:
  - **Auto-detect (*IDN?)** — sends the standard SCPI `*IDN?` identification query and matches the
    reply against each profile automatically. This is a real round-trip to the device (up to a
    few seconds); if nothing matches (or nothing answers in time), you get the Generic panel
    instead.
  - **Generic (manual)** — a minimal panel (`*IDN?`, `*RST`, `*CLS`, `*OPC?`) that works against any
    SCPI device, curated profile or not.

Whichever way you get there, every SCPI panel also has a **Custom Command** section at the bottom: a
text field and a Send button that sends exactly what you type, verbatim, with the reply shown right
there — so you're never limited to what's in the curated command list.

![TUI SCPI control panel (Korad KA6003P profile), after a Query Set Voltage round-trip](images/tui-control-panel-scpi.png)

![WPF SCPI control panel (Korad KA6003P profile), after a Query Set Voltage round-trip](images/wpf-control-panel-scpi.png)

### Sending a command with parameters

A curated command that needs a value (say, a frequency) shows as one or more fields next to a single
button. Fill in the field(s), then press the button — it reads whatever's currently in those fields
and sends the fully-formed command in one action. Query-type commands (anything ending in `?`) show
their reply in a read-only field right next to the button, as soon as it arrives — but only if the
`scpi` presenter is selected for the connection (see
[Connecting to a device](connecting.md)'s Presenters field); without it, commands still send fine,
you just won't see replies show up in the panel itself (they still appear in the main output as
plain text).

## What's not built yet

- No custom, per-manifest control panel yet for a device described only by a
  [device manifest](../design/device-manifests.md) — today's three panels are each built into
  dev-term directly.
