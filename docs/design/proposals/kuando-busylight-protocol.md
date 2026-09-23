# Proposal: Kuando Busylight — USB HID Status Light Control + Test Device

## Source

- `C:\repo\oobdev\dotex\Incoming\BinaryDecoders\src\OoBDev.Kuando.Busylight\` — a working prior C#
  implementation (structs for the wire format, a real USB trace with request/response pairs), from
  the same local `dotex/Incoming/BinaryDecoders` project as the [EByte proposal](ebyte-e810-dtu-config-protocol.md).

## Device

[Kuando Busylight](https://www.plenom.com/) — a USB RGB status-light device (the kind used to show
Slack/Teams presence), USB HID, VID `0x04D8` PID `0xF848`.

## Why this is a good test device for the new HID transport specifically

This wasn't primarily proposed as a decoder to build — it started as a **real, low-risk device to
verify `DevTerm.Transports.Hid`'s read *and* write paths against**, which was, at the time of
writing, the one thing not yet verified against real hardware (open/write/close had been verified
against a different device; no real inbound HID report had been confirmed flowing through
`HidReadStream`'s background-thread/channel handoff yet, and no write had been confirmed to
actually change anything on a device). **Update, same day — verified live against the plugged-in
device**:

- **Read path**: the poll request below (pre-verified against the source trace's checksum) was
  sent via `dotnet run ... --transport hid --hidvendorid 1240 --hidproductid 63560 --presenter
  hex`, and the device's real ASCII identification reply came back through the full pipeline
  (`HidReadStream`'s background thread → `Channel<byte[]>` → `StreamToPipePump` → `Session` → the
  hex presenter → stdout), byte-for-byte matching the source trace's own reply:

  ```
  > 8f0000...00 060455ffffff03eb   (poll request, 64 bytes, zero-padded)
  < 0001504c454e4f4d3030303030313030303030303031444153414e30303032303131303831373030...
    (ASCII: "0001PLENOM0000010000000 1DASAN0002011081700...") - confirmed live, byte-for-byte
  ```

- **Write path**: the 64-byte batch-program report (also pre-verified against the source trace's
  checksum, "set green" per the source's own comment) sent without error but **visibly did nothing**
  — a real finding, not a transport bug: a report the transport accepts isn't the same as a report
  the device's firmware acts on, and this specific batch/program-mode shape is apparently not
  sufficient on its own (see open questions). The simpler **single-command 8-byte struct**, sent as
  a 9-byte write (report ID `0x00` + the 8 struct bytes, matching the source's own `Class1.cs`
  convention), *did* work — confirmed by directly watching the light change color three times in a
  row (green → blue → correctly back to green → red), with the R/G/B byte order exactly matching
  the documented struct (an 0x00 0x00 0xFF instead of 0x00 0xFF 0x00 genuinely came out blue, not
  green, confirming the byte order rather than a coincidence).

## Protocol summary

**Single-command mode** (8-byte struct, sent as a 9-byte write with a leading `0x00` report-ID
byte — **confirmed working live**, including the R/G/B byte order):

```
NextStep : 1 byte
Repeat   : 1 byte
Color    : 3 bytes (R, G, B)
On       : 1 byte  (on-time, unit unconfirmed)
Off      : 1 byte  (off-time, unit unconfirmed)
Audio    : 1 byte  (packed - see below)
```

**Audio byte packing** (matches the source's own `BusylightAudio` struct exactly):

```
Bit 7   : Play/Mute
Bits 6-3: Track select (0-15; devices vary in how many tracks they support)
Bits 2-0: Volume level (0-7)
```

**Batch/program mode** (64-byte reports, from the real trace): up to six 8-byte step records (each
shaped like the single-command struct above, minus the leading report-ID byte) packed back-to-back,
followed by a trailing 8-byte "commit" record (`06 04 55 ff ff ff <checksum, 2 bytes>`). **Verified**
(not just eyeballed) against four independent trace examples: the checksum is the plain additive
sum of all 62 preceding bytes, stored big-endian — e.g. one example sums to `0x04ED`, and its
trailing two bytes are literally `04 ed`; three other examples with different payloads each check
out the same way.

## Proposed shape

```plantuml
@startuml
skinparam componentStyle rectangle
skinparam backgroundColor #FEFEFE

actor "User" as user

package "Busylight Control Module (plugin)" {
  [Busylight Control Surface] <<IControlSurface>> as surface
  note bottom of surface : Set color/blink/sound;\nprogram multi-step sequences
  [Busylight Codec] <<internal>> as codec
  note bottom of codec : Single-command (8B) and\nbatch-program (~64B) report shapes
}

[Session / Transport] <<ITransport>> as transport
note right of transport : USB HID, VID 0x04D8 PID 0xF848\n(DevTerm.Transports.Hid - built)

user --> surface : Invokes command\n(e.g. Set Red, Program Sequence)
surface --> codec : Builds report
codec --> transport : HID output report
transport --> codec : HID input report (poll reply)
codec --> user : Human-readable text baseline
@enduml
```

- **No new transport work** — `DevTerm.Transports.Hid` already exists; this is purely a decoder +
  control-surface exercise, and specifically a real-hardware verification opportunity for the read
  path.
- **Two report shapes for one device** (single-command vs. batch-program) is a smaller-scale
  version of the same "one envelope, multiple message shapes" pattern already noted for
  [Radex One](radex-one-protocol.md) and the [EByte proposal](ebyte-e810-dtu-config-protocol.md).

A status light is one of the few devices in this batch where a real GUI control genuinely beats a
text baseline — picking a color by typing R/G/B bytes is exactly the kind of thing a color swatch
does better:

```plantuml
@startsalt
{
  {* File | Device | Help}
  {
    Color: | [ ] Red [ ] Green [ ] Blue [ ] Yellow [ ] Off | [Custom...]
  }
  {
    Blink: | ()Solid ()Slow ()Fast | On: "1000" ms | Off: "0" ms
  }
  {
    Sound: | [ ] Mute | Track: <Funky|Nordic|Quiet|Open Office|Kuando> | Volume: [-----|----]
  }
  {
    [Apply] | [Program Sequence...]
  }
  {
    VID 0x04D8 PID 0xF848 | Connected
  }
}
@endsalt
```

## Status

**Implemented 2026-09-22** as the second concrete device module built against the generic
`UiDefinition`/`IControlSurface` renderer (see [device-control-modules.md](../device-control-modules.md),
[ui-definitions.md](../ui-definitions.md)) — landed specifically to prove the renderer built for
[Velleman K8055](velleman-k8055-protocol.md) is genuinely generic, not K8055-specific: neither
`ControlPanelMode` (TUI) nor `ControlPanelWindow` (WPF) needed any change, only a new
`DevTerm.Devices.Busylight` module (`BusylightUiDefinition`, `BusylightControlSurface`,
`BusylightDecoder`) plus the same one-line `AddDevTermFrontEnd`/menu-item wiring K8055 used.
Selectable via `--presenter busylight`, reachable from both front ends' new "Busylight Control
Panel..." menu item. Every command except "apply" only mutates `BusylightControlSurface`'s internal
state (color preset, on/off bytes, mute, track, volume); "apply" sends the confirmed-working 9-byte
single-command frame (report ID `0x00` + the 8-byte struct above) built from that state, matching the
mockup's explicit `[Apply]` button rather than sending on every field change. `BusylightDecoder` has
no `IStructuredPresenter` companion — the panel has no `IndicatorControl`s — and just renders the
poll reply's ASCII bytes as text. Verified by 12 new unit tests (`DevTerm.Devices.Busylight.Tests`)
against the exact frame bytes for each command; **not yet verified against the real, physical
device** — this landed as a software-only pass, with real hardware review deferred to the user for a
later session, same as some of K8055's own remaining checklist items.

**Updated same day, after real-time user feedback**: "Custom..." was reported as doing nothing and
is now a real modal — `ButtonControl.ColorPickerTargetCommandId` (a new, generic, device-agnostic
field on the UI-definitions model, not Busylight-specific) opens an RGB *and* HSV color picker in
both front ends (WPF `ColorPickerWindow`, TUI `ControlPanelMode.PickColor`) and sends the result as
an `"r,g,b"` (0-255 each) string to the `color` command, which `BusylightControlSurface.SetColor` now
accepts alongside the five named presets. "Program Sequence..." was also reported as doing nothing —
since this was already a confirmed, real-hardware-tested no-op (see "Open questions" below, unchanged
by this update), the button was removed from `BusylightUiDefinition` rather than investigated further
(a button with no effect on real hardware is worse than no button); `programSequence` is still
accepted as a no-op command for backward compatibility, so an older saved manifest/script referencing
it doesn't throw.

## Open questions

- **Why the batch/program-mode write had no visible effect** despite matching the source trace's
  own checksummed example byte-for-byte and not erroring — plausible explanations: the batch
  format needs a separate "start sequence" trigger the trace didn't capture, the six-steps-plus-gap
  layout assumed here is wrong, or this device/firmware revision simply doesn't support that mode
  the way the source's trace (captured from a possibly different unit) suggests. Needs more
  real-device experimentation before trusting this mode for anything.
- On/Off time units aren't documented in the source — needs a real device to measure against
  (confirmed only that `On=0x01, Off=0x00` produces a solid, non-blinking color).
- Whether the ASCII poll reply's fields (what looks like two concatenated strings, "PLENOM..." and
  "DASAN...") are meaningful device/vendor identifiers worth surfacing, or just fixed firmware
  strings not worth decoding further.
