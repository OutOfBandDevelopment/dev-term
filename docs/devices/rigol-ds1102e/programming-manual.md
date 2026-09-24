# Rigol DS1102E — Programming Manual (USB / RS-232)

2-channel, analog-only digital storage oscilloscope. This manual covers the full native SCPI-style
command set reachable over the DS1102E's **USB** and **RS-232** interfaces — this product line has
no GPIB or LAN option at all, so there is nothing to exclude on that front (unlike some sibling
manuals in this repo where GPIB/LAN exist on a different model tier).

## Source document

**RIGOL Programming Guide — DS1000E, DS1000D Series Digital Oscilloscope**, Sept. 2010, RIGOL
Technologies, Inc. No separate publication number was printed in the extracted copyright block of
this edition. This single guide documents the DS1000E (analog-only, "E"-suffix: DS1052E, DS1102E,
DS1152E) and DS1000D (mixed-signal, "D"-suffix, adds a 16-channel logic analyzer) series together,
and is explicit where a command applies to only one tier. The `*IDN?` example in the guide itself
uses a DS1102E unit (`RIGOL TECHNOLOGIES,DS1102E,DS1EB104702974,00.02.01.01.00`), confirming this
guide is directly authoritative for this model.

**Model-tier scope note:** DS1102E is the "E"-suffix (analog-only) tier — it has no logic-analyzer
hardware and no digital-channel inputs. Wherever the guide documents a command or parameter that
requires digital channels, it is excluded from the per-command reference below and called out in
"What's excluded and why" at the end, the same way a hardware-absent interface is handled in this
repo's other manuals.

## Before you start

### Physical connection

- **USB:** connect the oscilloscope to the PC with a USB data cable. On first connection, Windows
  prompts to install a driver for "Rigol USB Test and Measurement Device" (RIGOL's own USB driver).
- **RS-232:** connect via a standard RS-232 serial cable between the oscilloscope's RS-232 port and
  the PC's serial port (or a USB-to-RS232 adapter).

The source guide includes a rear-panel connector diagram that did not extract cleanly from the
source PDF (a text-extraction gap, not an omission of content) — it confirms a USB port and an
RS-232 port both exist on the rear panel, but the exact physical layout/position isn't verified from
this text. Consult the DS1000E/D User's Guide or the physical unit if panel layout matters for your
purposes.

### Driver / software prerequisites

Two programming paths are documented, both usable over USB or RS-232 once the connection is up:

1. **RIGOL's own USB driver** ("Rigol USB Test and Measurement Device") — the guide's example code
   loads `RigolTMCUsb_UI.dll` and calls `GetTMCDeviceNum`, `WriteUSB`, `ReadUSB` directly.
2. **VISA** — install NI-VISA (or an equivalent VISA implementation); the device then enumerates as
   a "USB Test and Measurement Device" and is addressed through standard VISA calls. This path is
   the more portable one for typical PC automation (Python/PyVISA, LabVIEW, etc.).

Either path speaks the identical ASCII command set documented below — the driver/library choice
affects only how bytes get to/from the instrument, not command syntax.

## Command syntax conventions

- **Tree structure.** Commands form a hierarchical tree: a "root" keyword followed by one or more
  sub-keywords, joined by `:`. A command line starts with `:` (except IEEE 488.2 common commands,
  which start with `*` and never use `:`). `?` appended to a command line makes it a query. A space
  separates the command from its parameter(s).
  Example: `:TRIGger:EDGE:SLOPe {POSitive|NEGative}` / `:TRIGger:EDGE:SLOPe?`
- **Multiple parameters** in one command are comma-separated, e.g.
  `:TRIGger:DURation:PATTern <value>,<mask>`.
- **Braces `{ }`** enclose a required choice — pick exactly one of the `|`-separated options, e.g.
  `{ON|OFF}`.
- **Square brackets `[ ]`** enclose an optional keyword or parameter — it may be omitted, e.g.
  `:TIMebase[:DELayed]:OFFSet <offset>` (the `:DELayed` keyword is optional; omit it to address the
  MAIN timebase).
- **Angle brackets `< >`** mark a placeholder that must be replaced with an actual value, e.g.
  `:DISPlay:BRIGhtness <ncount>` where `<ncount>` is a number like `25`.
- **Case-insensitive, abbreviation by capitalization.** All commands are case-insensitive. Where the
  guide spells a keyword with some letters capitalized (e.g. `TRIGger`), those capitalized letters
  are the minimum valid abbreviation — you may send the abbreviation or the full word, but not
  something in between. `:TRIGger:EDGE:SLOPe` may be sent as `:TRIG:EDGE:SLOP`.
- **Five parameter types**, with different valid-value shapes:
  1. **Boolean** — `ON`/`OFF` (some commands also accept `1`/`0`; check the specific command).
  2. **Consecutive integer** — any integer in a stated range, e.g. `0`–`32`.
  3. **Consecutive real number** — any real value within a stated range/precision, e.g. `0.1`–`1`.
  4. **Discrete** — one of an enumerated set of specific values only, e.g. `2, 4, 8, 16, 32, 64, 128,
     256` for `:ACQuire:AVERages`.
  5. **ASCII character string** — a fixed set of keyword strings, e.g. `EDGE`, `PULSe`, `VIDEO`, ...
     for `:TRIGger:MODE`.

## Command reference

Commands are grouped by the vendor guide's own Chapter 2 structure, in the same order.

### General Commands (IEEE 488.2)

- **`*IDN?`** — Query instrument identity. Returns 4 comma-separated fields: manufacturer, model,
  serial number, firmware version. Example: `RIGOL TECHNOLOGIES,DS1102E,DS1EB104702974,00.02.01.01.00`.
  Always send this first to confirm remote comms are working.
- **`*RST`** — Reset system parameters to their default state. No query form.

### SYSTem Commands

- **`:RUN`** — Start continuous waveform acquisition (runs until a `:STOP`).
- **`:STOP`** — Stop acquisition. Restart with `:RUN`.
- **`:AUTO`** — Auto-set: evaluate all input signals and choose display-optimal vertical/horizontal/
  trigger settings automatically (equivalent to the front-panel "Auto" button).
- **`:HARDcopy`** — Save the current screen as a bitmap (`HardCopyxxx.bmp`) to USB flash storage.
  **Gotcha:** the source explicitly notes this command is unavailable when the (optional) file
  system isn't in use — i.e. it requires a USB flash drive plugged into the front-panel USB Host
  port to actually write to.

### ACQuire Commands

- **`:ACQuire:TYPE <type>` / `?`** — Set/query acquisition type: `NORMal`, `AVERage`, or
  `PEAKdetect`. Query returns `NORMAL`, `AVERAGE`, or `PEAKDETECT`.
- **`:ACQuire:MODE <mode>` / `?`** — Set/query sampling mode: `RTIMe` (real-time) or `ETIMe`
  (equivalent-time / repetitive sampling for signals faster than the real-time sample rate allows).
  Query returns `REAL_TIME` or `EQUAL_TIME`.
- **`:ACQuire:AVERages <count>` / `?`** — Set/query the averaging count used in `AVERage` acquire
  type. `<count>` is a power of 2 from 2 to 256 (discrete: 2, 4, 8, 16, 32, 64, 128, 256).
- **`:ACQuire:SAMPlingrate? {CHANnel<n>}`** — Query the current sample rate for analog channel `<n>`
  (`1` or `2`). Returns a decimal value in Hz, e.g. `100000000.000000` for 100 MHz.
  **Excluded parameter:** the source also documents a `DIGITAL` source option for this query
  ("only for DS1000D series") — not applicable on the DS1102E, which has no digital channels; use
  `CHANnel<n>` only.
- **`:ACQuire:MEMDepth <depth>` / `?`** — Set/query memory depth: `LONG` or `NORMal`. Query returns
  `LONG` or `NORMAL`.

### DISPlay Commands

- **`:DISPlay:TYPE <type>` / `?`** — Display mode between sample points: `VECTors` or `DOTS`. Query
  returns `VECTORS` or `DOTS`.
- **`:DISPlay:GRID <grid>` / `?`** — Screen grid: `FULL` (grid + coordinates), `HALF` (grid off,
  coordinates on — per the guide's own wording, though this is likely a documentation inversion
  worth verifying against real hardware), or `NONE` (grid + coordinates off). Query returns `FULL`,
  `HALF`, or `NONE`.
- **`:DISPlay:PERSist {ON|OFF}` / `?`** — Waveform persistence. `ON`: record points accumulate/hold
  until disabled; `OFF`: points refresh at full rate. Query returns `ON`/`OFF`.
- **`:DISPlay:MNUDisplay <time>` / `?`** — Menu auto-hide delay: `1s`, `2s`, `5s`, `10s`, `20s`, or
  `Infinite`. Query returns the same set of values.
- **`:DISPlay:MNUStatus {ON|OFF}` / `?`** — Show/hide the operation menu. Query returns `ON`/`OFF`.
- **`:DISPlay:CLEar`** — Clear stale/accumulated waveforms left on screen by persistence mode. No
  query form.
- **`:DISPlay:BRIGhtness <ncount>` / `?`** — Grid brightness, integer `0`–`32` (dark to bright).
- **`:DISPlay:INTensity <count>` / `?`** — Waveform trace brightness, integer `0`–`32`.

### TIMebase Commands

- **`:TIMebase:MODE <mode>` / `?`** — Horizontal scan mode: `MAIN` or `DELayed` (delayed/zoomed
  scan). Query returns `MAIN` or `DELAYED`.
- **`:TIMebase[:DELayed]:OFFSet <offset>` / `?`** — Set/query horizontal offset (waveform position
  relative to the trigger midpoint) for MAIN or delayed timebase; omit `:DELayed` for MAIN.
  **Range depends on run state:** in NORMAL (running) mode, `1s` to end-of-memory; in STOP mode,
  `-500s` to `+500s`; in SCAN (roll) mode, `±6×Scale` where Scale is the current horizontal
  s/div setting. Returned value is in seconds, e.g. `1.000e+00`.
- **`:TIMebase[:DELayed]:SCALe <scale_val>` / `?`** — Set/query horizontal scale (s/div) for MAIN or
  delayed timebase. In YT mode: `2ns`–`50s`. In ROLL mode: `500ms`–`50s`. Returned value in seconds,
  e.g. `2.000e+00`.
- **`:TIMebase:FORMat <value>` / `?`** — Horizontal display format: `XY`, `YT`, or `SCANning`. Query
  returns `X-Y`, `Y-T`, or `SCANNING`.

### TRIGger Commands

#### Trigger Control

- **`:TRIGger:MODE <mode>` / `?`** — Select trigger type: `EDGE`, `PULSe`, `VIDEO`, `SLOPe`,
  `PATTern`, `DURation`, or `ALTernation`. Query returns the same set, uppercased.
- **`:TRIGger<mode>:SOURce <src>` / `?`** — Set/query trigger source for the given `<mode>`
  (`:EDGE`, `:PULSe`, `:SLOPe`, or `:VIDEO`). Valid `<src>` depends on mode: EDGE accepts
  `CHANnel<n>`, `EXT`, `ACLine`; PULSe accepts `CHANnel<n>`, `EXT`; SLOPe and VIDEO accept
  `CHANnel<n>` or `EXT`. `<n>` is `1` or `2`. Query returns `CH1`, `CH2`, `EXT`, or `ACLINE`.
  **Excluded:** the source additionally lists a `DIGital<m>` source option for EDGE and PULSe modes
  (`<m>` = `0`–`15`, i.e. `D0`–`D15`) — not applicable on the DS1102E, which has no digital
  channels; this is the same hardware-absence reasoning as the `LA` subsystem exclusion below.
- **`:TRIGger<mode>:LEVel <level>` / `?`** — Set/query trigger level for `:EDGE`, `:PULSe`, or
  `:VIDEO` mode. Range `±6×Scale` (Scale = current vertical V/div). Returned in volts, e.g.
  `1.00e+00`.
- **`:TRIGger<mode>:SWEep {AUTO|NORMal|SINGle}` / `?`** — Set/query sweep/trigger-arming type for
  `:EDGE`, `:PULSe`, `:SLOPe`, `:PATTern`, or `:DURation` mode. Query returns `AUTO`, `NORMAL`, or
  `SINGLE`.
- **`:TRIGger<mode>:COUPling {DC|AC|HF|LF}` / `?`** — Set/query trigger coupling for `:EDGE`,
  `:PULSe`, or `:SLOPe` mode. `DC`: passes all signals. `AC`: blocks DC, attenuates below 10 Hz.
  `HF`: rejects signals above 150 kHz. `LF`: rejects DC, attenuates below 8 kHz.
- **`:TRIGger:HOLDoff <count>` / `?`** — Set/query trigger holdoff time, `500ns`–`1.5s`. Returned in
  seconds, e.g. `5.000e-04`.
- **`:TRIGger:STATus?`** — Query run status: `RUN`, `STOP`, `` T`D `` (triggered), `WAIT`, or
  `AUTO`. No set form.
- **`:Trig%50`** — Set the trigger level to the vertical midpoint of the signal's amplitude. No
  query form. **Gotcha:** note the unusual non-colon-tree, `%`-in-name syntax — this and
  `:FORCetrig` are the only two commands in this whole command set that break the normal `:`-rooted
  keyword pattern.
- **`:FORCetrig`** — Force an immediate trigger (typically used in `Normal`/`Single` sweep mode to
  force one acquisition without waiting for a real trigger condition). No query form.

#### EDGE Trigger

- **`:TRIGger:EDGE:SLOPe {POSitive|NEGative}` / `?`** — Rising or falling edge. Query returns
  `POSITIVE`/`NEGATIVE`.
- **`:TRIGger:EDGE:SENSitivity <count>` / `?`** — Trigger sensitivity, `0.1div`–`1div`. Returned in
  divisions, e.g. `5.00e-01`.

#### PULSe Trigger

- **`:TRIGger:PULSe:MODE <mode>` / `?`** — Pulse-width qualifier: `+GREaterthan`, `+LESSthan`,
  `+EQUal`, `-GREaterthan`, `-LESSthan`, `-EQUal` (sign = pulse polarity). Query returns the
  spelled-out form, e.g. `+GREATER THAN`.
- **`:TRIGger:PULSe:SENSitivity <count>` / `?`** — `0.1div`–`1div`.
- **`:TRIGger:PULSe:WIDTh <wid>` / `?`** — Pulse width, `20ns`–`10s`. Returned in seconds.

#### VIDEO Trigger

- **`:TRIGger:VIDEO:MODE <mode>` / `?`** — Video sync mode: `ODDfield`, `EVENfield`, `LINE`, or
  `ALLlines`. Query returns `ODD FIELD`, `EVEN FIELD`, `LINE`, or `ALL LINES`.
- **`:TRIGger:VIDEO:POLarity {POSitive|NEGative}` / `?`** — Video signal polarity.
- **`:TRIGger:VIDEO:STANdard {NTSC|PALSecam}` / `?`** — Query returns `NTSC` or `PAL/SECAM`.
- **`:TRIGger:VIDEO:LINE <value>` / `?`** — Sync line number. NTSC: `1`–`525`. PAL/SECAM: `1`–`625`.
- **`:TRIGger:VIDEO:SENSitivity <count>` / `?`** — `0.1div`–`1div`.

#### SLOPe Trigger

- **`:TRIGger:SLOPe:TIME <count>` / `?`** — Slope-trigger time setting, `20ns`–`10s`.
- **`:TRIGger:SLOPe:SENSitivity <count>` / `?`** — `0.1div`–`1div`.
- **`:TRIGger:SLOPe:MODE <mode>` / `?`** — Same six-value qualifier set as `PULSe:MODE`
  (`+GREaterthan` etc.).
- **`:TRIGger:SLOPe:WINDow <count>` / `?`** — Which level boundary is adjustable: for positive
  slope conditions, `PA`/`PB`/`PAB` (rising-edge Level A/B/AB); for negative, `NA`/`NB`/`NAB`.
  Query returns `P_WIN_A`, `P_WIN_B`, `P_WIN_AB`, `N_WIN_A`, `N_WIN_B`, or `N_WIN_AB`.
- **`:TRIGger:SLOPe:LEVelA <value>` / `?`** — Upper trigger-level boundary. Range: `LevelB` to
  `+6×Scale`.
- **`:TRIGger:SLOPe:LEVelB <value>` / `?`** — Lower trigger-level boundary. Range: `-6×Scale` to
  `LevelA`. **Gotcha (source's own note):** Level A must not be set below Level B's current value —
  set them in a consistent order to avoid a rejected/clamped value.

#### PATTern Trigger

- **`:TRIGger:PATTern:PATTern <value>,<mask>[,<edge source>,<edge>]` / `?`** — Set/query a
  channel-state pattern to trigger on. `<value>` and `<mask>` are unsigned 16-bit integers:
  in `<value>`, bit=1 means that channel must currently read high, bit=0 means low; in `<mask>`,
  bit=1 means that channel is included in the match ("enable"), bit=0 means "don't care" (`X`).
  Query returns `<value>`, `<mask>`, `<edge source>`, `<edge>` in turn, with `<value>`/`<mask>`
  returned as decimal numbers.
  **Model-tier caveat:** the optional trailing `[<edge source>,<edge>]` pair explicitly documents
  `<edge source>` as ranging `0`–`15` (i.e. `DIG0`–`DIG15`) with `<edge>` meaning rising(`1`)/
  falling(`0`) — this edge-qualification extension requires digital channels and is **not usable on
  the DS1102E** (no digital channels exist), the same hardware-absence reasoning as the `LA`
  subsystem below. The base `<value>,<mask>` pattern-match itself is not explicitly restricted to
  D-series in the source text, and on a 2-analog-channel instrument would presumably map bit 0 = CH1
  and bit 1 = CH2 (bits 2–15 meaningless/don't-care) — but this bit-to-channel mapping for the
  analog-only case is not spelled out anywhere in the source, so treat it as unverified and test
  against real hardware before relying on it.

#### DURation Trigger

- **`:TRIGger:DURation:PATTern <value>,<mask>` / `?`** — Same 16-bit value/mask pattern semantics as
  `PATTern:PATTern` above (bit=1 in `<value>` = channel must be high; bit=1 in `<mask>` = channel
  included in match), used as the qualifying pattern for the duration trigger. **Gotcha:** unlike
  `PATTern:PATTern`, the source gives no `<edge source>`/digital-channel reference here at all, so
  there's no explicit basis to exclude this on the DS1102E — but the bit-to-channel mapping for a
  2-analog-channel instrument still isn't spelled out; verify against hardware.
- **`:TRIGger:DURation:TIME <time>` / `?`** — Duration limit, `2ns`–`10s`.
- **`:TRIGger:DURation:QUALifier <qual>` / `?`** — Duration comparison: `GREaterthan`, `LESSthan`,
  or `EQUal`. Query returns the spelled-out form.

#### ALTernation Trigger

Lets CH1 and CH2 each trigger independently with their own trigger type, alternating between them.

- **`:TRIGger:ALTernation:SOURce <src>` / `?`** — Which channel this sub-configuration applies to:
  `CHANnel1` or `CHANnel2`. Query returns `CH1`/`CH2`.
- **`:TRIGger:ALTernation:TYPE <value>` / `?`** — Trigger type for the alternation source: `EDGE`,
  `PULSe`, `SLOPe`, or `VIDEO`.
- **`:TRIGger:ALTernation:TimeSCALe <value>` / `?`** — Timebase for the current alternation channel.
- **`:TRIGger:ALTernation:TimeOFFSet <value>` / `?`** — Horizontal offset for the current
  alternation channel; same NORMAL/STOP/ROLL range rules as `:TIMebase[:DELayed]:OFFSet`.
- **`:TRIGger:ALTernation<mode>:LEVel <value>` / `?`** — Trigger level (`:EDGE`, `:PULSe`, or
  `:VIDEO`), range `±6×Scale`.
- **`:TRIGger:ALTernation:EDGE:SLOPe <value>` / `?`** — `POSitive`/`NEGative`.
- **`:TRIGger:ALTernation<mode>:MODE <value>` / `?`** — For `:PULSe`/`:SLOPe`: the same six-value
  `+GREaterthan`... qualifier set; for `:VIDEO`: `ODDfield`/`EVENfield`/`LINE`/`ALLlines`.
- **`:TRIGger:ALTernation<mode>:TIME <value>` / `?`** — Pulse width or slope time (`:SLOPe` or
  `:PULSe`), `20ns`–`10s`.
- **`:TRIGger:ALTernation:VIDEO:POLarity {POSitive|NEGative}` / `?`**
- **`:TRIGger:ALTernation:VIDEO:STANdard {NTSC|PALSecam}` / `?`**
- **`:TRIGger:ALTernation:VIDEO:LINE <value>` / `?`** — Same NTSC/PAL range rules as the plain VIDEO
  trigger.
- **`:TRIGger:ALTernation:SLOPe:WINDow <count>` / `?`** — Same `PA`/`PB`/`PAB`/`NA`/`NB`/`NAB`
  semantics as plain SLOPe trigger.
- **`:TRIGger:ALTernation:SLOPe:LEVelA <value>` / `?`** / **`:LEVelB <value>` / `?`** — Same
  boundary semantics as plain SLOPe trigger.
- **`:TRIGger:ALTernation<mode>:COUPling {DC|AC|HF|LF}` / `?`** — `<mode>` = `:EDGE`, `:PULSe`, or
  `:SLOPe`.
- **`:TRIGger:ALTernation<mode>:HOLDoff <count>` / `?`** — `<mode>` = `:EDGE`, `:PULSe`, `:SLOPe`,
  or `:VIDEO`. `500ns`–`1.5s`.
- **`:TRIGger:ALTernation<mode>:SENSitivity <count>` / `?`** — `<mode>` = `:EDGE`, `:PULSe`,
  `:SLOPe`, or `:VIDEO`. `0.1div`–`1div`.

### STORage Command

- **`:STORage:FACTory:LOAD`** — Recall factory default settings. No query form.

### MATH Commands

- **`:MATH:DISPlay {ON|OFF}` / `?`** — Enable/disable the math trace.
- **`:MATH:OPERate <operate>` / `?`** — Math operation: `A+B`, `A-B`, `AB` (multiply — the source
  shows the literal `AB` set-token but a returned value of `A*B`; send `AB`, expect `A*B` back), or
  `FFT`.
- **`:FFT:DISPlay {ON|OFF}` / `?`** — Enable/disable the FFT trace specifically.

### CHANnel Commands

All accept a channel selector `<n>` = `1` or `2`.

- **`:CHANnel<n>:BWLimit {ON|OFF}` / `?`** — 20 MHz bandwidth-limit filter.
- **`:CHANnel<n>:COUPling {DC|AC|GND}` / `?`** — `DC` passes AC+DC; `AC` blocks DC; `GND` cuts the
  input off entirely (for a zero-reference).
- **`:CHANnel<n>:DISPlay {ON|OFF}` / `?`** — Show/hide the channel trace.
- **`:CHANnel<n>:INVert {ON|OFF}` / `?`** — Invert the displayed waveform polarity.
- **`:CHANnel<n>:OFFSet <offset>` / `?`** — Vertical offset. Range depends on current vertical
  scale: if Scale ≥ 250 mV/div, `±40V`; if Scale < 250 mV/div, `±2V`.
- **`:CHANnel<n>:PROBe <attn>` / `?`** — Probe attenuation ratio: `1`, `5`, `10`, `50`, `100`,
  `500`, or `1000`. **Gotcha:** set this to match your physical probe *before* setting `:SCALe` or
  `:OFFSet` in real-world units — those are computed relative to the configured attenuation.
- **`:CHANnel<n>:SCALe <range>` / `?`** — Vertical scale (V/div). Valid range depends on the current
  probe attenuation setting: 1×→`2mV`–`10V`; 5×→`10mV`–`50V`; 10×→`20mV`–`100V`; 50×→`100mV`–`500V`;
  100×→`200mV`–`1000V`; 500×→`1V`–`5000V`; 1000×→`2V`–`10000V`.
- **`:CHANnel<n>:FILTer {ON|OFF}` / `?`** — Channel filter on/off.
- **`:CHANnel<n>:MEMoryDepth?`** — Query memory depth for this channel. No set form (set overall
  depth via `:ACQuire:MEMDepth`). Long memory: 1 Mpt (single channel) / 512 kpt (dual channel).
  Normal memory: 16 kpt (single) / 8 kpt (dual). Returned as a plain integer, e.g. `8192`.
- **`:CHANnel<n>:VERNier {ON|OFF}` / `?`** — Fine (`ON`) vs. coarse (`OFF`) vertical-scale
  adjustment granularity. Query returns `Fine`/`Coarse`.

### MEASure Commands

Automatic measurements — the source notes these are "only available for analog channel[s]"
(consistent with DS1102E having only analog channels) and generally return results in scientific
notation. All `?`-form commands below accept an optional `[<source>]` parameter, `CHANnel1` or
`CHANnel2`; if omitted, the currently-selected `:MEASure:SOURce` is used.

- **`:MEASure:CLEar`** — Clear all current measurement results. No query form.
- **`:MEASure:VPP? [<source>]`** — Peak-to-peak voltage, e.g. `5.12e+03` V.
- **`:MEASure:VMAX? [<source>]`** — Maximum voltage.
- **`:MEASure:VMIN? [<source>]`** — Minimum voltage.
- **`:MEASure:VAMPlitude? [<source>]`** — Amplitude (Vtop − Vbase).
- **`:MEASure:VTOP? [<source>]`** — Top (high-state) voltage.
- **`:MEASure:VBASe? [<source>]`** — Base (low-state) voltage.
- **`:MEASure:VAVerage? [<source>]`** — Average voltage.
- **`:MEASure:VRMS? [<source>]`** — RMS voltage.
- **`:MEASure:OVERshoot? [<source>]`** — Overshoot, as a fraction, e.g. `1.74e-02`.
- **`:MEASure:PREShoot? [<source>]`** — Preshoot, as a fraction.
- **`:MEASure:FREQuency? [<source>]`** — Frequency in Hz.
- **`:MEASure:RISetime? [<source>]`** — 10%–90% rise time, in seconds.
- **`:MEASure:FALLtime? [<source>]`** — 90%–10% fall time, in seconds.
- **`:MEASure:PERiod? [<source>]`** — Period, in seconds.
- **`:MEASure:PWIDth? [<source>]`** — Positive pulse width, in seconds.
- **`:MEASure:NWIDth? [<source>]`** — Negative pulse width, in seconds.
- **`:MEASure:PDUTycycle? [<source>]`** — Positive duty cycle, as a fraction.
- **`:MEASure:NDUTycycle? [<source>]`** — Negative duty cycle, as a fraction.
- **`:MEASure:PDELay? [<source>]`** — Delay relative to the rising edge of the other channel.
- **`:MEASure:NDELay? [<source>]`** — Delay relative to the falling edge of the other channel.
- **`:MEASure:TOTal {ON|OFF}` / `?`** — Enable/disable the "all measurements" info panel display.
- **`:MEASure:SOURce <source>` / `?`** — Set/query the default measured channel (`CHANnel1` or
  `CHANnel2`) used when a `:MEASure:*?` query omits `[<source>]`.

### WAVeform Command

- **`:WAVeform:DATA? [<source>]`** — Read waveform sample data from `<source>`. Returns up to 1024
  points in the response (more with `:POINts:MODE RAW`/`MAX`, per the table below).
  **Excluded source option:** the source also lists `DIGital` as a valid `<source>` here (alongside
  `CHANnel1`, `CHANnel2`, `MATH`, `FFT`) — not applicable on the DS1102E, no digital channels exist;
  use `CHANnel1`, `CHANnel2`, `MATH`, or `FFT`.
- **`:WAVeform:POINts:MODE <points_mode>` / `?`** — Set/query how many points a `:WAVeform:DATA?`
  read returns: `NORMal`, `MAXimum`, or `RAW`. Point counts by mode (DS1102E-relevant rows only —
  `CHx` = an analog channel, `Half-Channel` = only one channel open and MATH closed):

  | Source | NORMal | RAW (needs STOP) | MAX (RUN state) | MAX (STOP state) |
  |---|---|---|---|---|
  | MATH / FFT | 600 / 512 | 600 / 512 | same as NORMal | same as RAW |
  | CHx (dual-channel) | 600 | 16384 (16k, normal mem) / — | same as NORMal | same as RAW |
  | Half-Channel | 600 | 16384 (16k) | same as NORMal | same as RAW |

  **Gotcha:** `RAW` mode's higher point counts (up to 1 Mpt/512 kpt in long-memory mode — see
  `:CHANnel<n>:MEMoryDepth?`) are only actually available while the scope is stopped (`:STOP`); in
  `RUN` state, `MAX` behaves like `NORMal`. Excluded: the source's table also has a `DIGITAL` row
  (600 pts) — not applicable, no digital channels.

### KEY Commands

Simulate front-panel button/knob presses — useful for scripting a UI walkthrough remotely.
**Important operational gotcha:** most of these are **stateful toggles/cyclers**, not idempotent
set commands — sending the same one repeatedly cycles through states (on→off→on..., or through a
menu's option list) rather than forcing a specific state. Don't assume "sent once" means "now in
state X"; track state yourself or use the dedicated `:CHANnel`/`:TRIGger`/etc. commands instead when
you need a specific, verifiable end state.

- **`:KEY:LOCK {ENABle|DISable}` / `?`** — Enable/disable all front-panel buttons except "Force".
- **`:KEY:RUN`**, **`:KEY:AUTO`**, **`:KEY:CHANnel1`**, **`:KEY:CHANnel2`**, **`:KEY:MATH`**,
  **`:KEY:REF`**, **`:KEY:F1`**–**`:KEY:F5`**, **`:KEY:MNUoff`**, **`:KEY:MEASure`**,
  **`:KEY:CURSor`**, **`:KEY:ACQuire`**, **`:KEY:DISPlay`**, **`:KEY:STORage`**, **`:KEY:UTILity`**,
  **`:KEY:MNUTIME`**, **`:KEY:MNUTRIG`** — each simulates pressing the correspondingly-named
  front-panel key/menu button (toggle or cycle, per the button's normal front-panel behavior — F1–F5
  cycle through that softkey's current drop-down options).
- **`:KEY:Trig%50`** — Same as the top-level `:Trig%50` (set trigger level to signal midpoint).
- **`:KEY:FORCe`** — Unlock remote control (equivalent to the physical "Force" key, which stays live
  even when `:KEY:LOCK ENABle` has locked everything else).
- **`:KEY:V_POS_INC`** / **`:KEY:V_POS_DEC`** — Increase/decrease vertical position of the current
  channel.
- **`:KEY:V_SCALE_INC`** / **`:KEY:V_SCALE_DEC`** — Increase/decrease vertical scale.
- **`:KEY:H_SCALE_INC`** / **`:KEY:H_SCALE_DEC`** — Adjust horizontal scale (source states INC steps
  down in a 5-2-1 sequence and DEC steps up in 1-2-5 — this looks like a naming/description swap in
  the vendor text; verify actual direction against real hardware before relying on it).
- **`:KEY:TRIG_LVL_INC`** / **`:KEY:TRIG_LVL_DEC`** — Increase/decrease trigger level.
- **`:KEY:H_POS_INC`** / **`:KEY:H_POS_DEC`** — Increase/decrease horizontal offset.
- **`:KEY:PROMPT_V`** — Toggle vertical adjust granularity (Coarse ↔ Fine).
- **`:KEY:PROMPT_H`** — Toggle the Delayed-timebase function on/off.
- **`:KEY:FUNCtion`** — Enable the multi-function knob.
- **`:KEY:+FUNCtion`** / **`:KEY:-FUNCtion`** — Increase/decrease the multi-function knob's offset.
- **`:KEY:PROMPT_V_POS`** — Reset vertical offset to zero.
- **`:KEY:PROMPT_H_POS`** — Reset horizontal/trigger offset to zero.
- **`:KEY:PROMPT_TRIG_LVL`** — Reset trigger level to screen center.
- **`:KEY:OFF`** — Cycles through turning off CH1, CH2, MATH, REF (and LA, on D-series) one at a
  time per repeated send. **Gotcha:** on the DS1102E, the "LA" leg of this cycle has no
  corresponding hardware — expect the cycle to only meaningfully touch CH1/CH2/MATH/REF; exact
  behavior of the missing LA step is unverified from the source text (the guide doesn't special-case
  this command for the E-series the way it does for `:ACQuire:SAMPlingrate?`).

**Excluded:** **`:KEY:LA`** — explicitly "enables or disables the logic analyzer built-in the
oscilloscope"; excluded here since the DS1102E has no logic analyzer (same hardware-absence
reasoning as the `LA` command subsystem below).

### Other Commands

- **`:INFO:LANGuage <lang>` / `?`** — System UI language: `SIMPlifiedChinese`, `TRADitionalChinese`,
  `ENGLish`, `KORean`, `JAPanese`, `FRENch`, `GERMan`, `RUSSian`, `SPANish`, `PORTuguese`.
- **`:COUNter:ENABle {ON|OFF}` / `?`** — Enable/disable the built-in frequency counter.
- **`:BEEP:ENABle {ON|OFF}` / `?`** — Enable/disable the beeper.
- **`:BEEP:ACTion`** — Sound the beeper once immediately, regardless of the `:BEEP:ENABle` setting.
  No query form.

## Worked end-to-end example: capture and transfer a waveform

A typical automation sequence — configure CH1, arm an edge trigger, run, then pull back sample data
and a couple of automatic measurements:

```text
*IDN?
  -> RIGOL TECHNOLOGIES,DS1102E,DS1EB104702974,00.02.01.01.00

:CHAN1:PROB 10                   (tell the scope the probe is 10X, before setting scale/offset)
:CHAN1:COUP DC
:CHAN1:SCAL 1                    (1 V/div)
:CHAN1:DISP ON

:TIM:MODE MAIN
:TIM:SCAL 0.001                  (1 ms/div)

:TRIG:MODE EDGE
:TRIG:EDGE:SOUR CHAN1
:TRIG:EDGE:SLOP POS
:TRIG:EDGE:LEV 1                 (trigger at 1V)
:TRIG:EDGE:SWE AUTO

:RUN
:TRIG:STAT?
  -> T`D                          (poll until triggered, or just wait — AUTO sweep free-runs anyway)

:STOP                            (freeze the display so RAW-mode data is available)

:WAV:POIN:MODE NORM
:WAV:DATA? CHAN1
  -> <up to 1024 sample points, per the current NORMal-mode format>

:MEAS:SOUR CHAN1
:MEAS:VPP? CHAN1
  -> 5.12e+00                    (example reading, volts)
:MEAS:FREQ? CHAN1
  -> 1.00e+03                    (example reading, Hz)

:RUN                             (resume live acquisition when done)
```

For a deeper capture (beyond the default 1024-point `NORMal` read), switch to
`:WAV:POIN:MODE RAW` while stopped to pull up to the channel's full memory depth (see
`:CHANnel<n>:MEMoryDepth?` and the point-count table under `:WAVeform:POINts:MODE` above).

## Common gotchas

- **Numeric replies are scientific notation.** Virtually every numeric query returns a string like
  `1.000e+00`, not a plain decimal — parse accordingly.
- **Many valid ranges are relative to the current scale, not fixed.** Trigger levels, `SLOPe`
  Level A/B, and timebase offsets are frequently specified as `±6×Scale` (vertical) or scale-relative
  (horizontal) rather than fixed absolute ranges — read the current `:CHANnel<n>:SCALe?` or
  `:TIMebase:SCALe?` before computing a value to send, or a seemingly-reasonable value may be
  rejected or silently clamped.
- **Probe attenuation must be set first.** `:CHANnel<n>:PROBe` changes what real-world range
  `:CHANnel<n>:SCALe`/`:OFFSet` accept and how they're interpreted — set the probe ratio to match
  your physical probe before configuring scale/offset in real units.
- **`RAW`-mode / deep-memory waveform reads require the scope to be stopped** (`:STOP`) — while
  running, `:WAVeform:POINts:MODE MAX` behaves the same as `NORMal` (capped at ~600–1024 points, per
  source/mode). Always `:STOP` before a deep-memory `:WAVeform:DATA?` pull if you need more than the
  default point count.
- **KEY commands are mostly stateful toggles, not absolute sets** — see the gotcha under "KEY
  Commands" above. Prefer the dedicated `:CHANnel`/`:TRIGger`/`:DISPlay`/etc. commands over `:KEY:*`
  whenever you need a specific, verifiable end state rather than a simulated button press.
- **Abbreviation is all-or-nothing per keyword.** You can't abbreviate to something shorter than the
  capitalized portion of a keyword (e.g. `:TRIG` is valid for `:TRIGger`, but `:TRI` is not) — and
  you can't abbreviate *inconsistently* partway through a multi-keyword command; each keyword
  segment independently must be either its full spelling or exactly its capitalized abbreviation.
- **`:Trig%50` and `:FORCetrig` don't follow the normal command-tree shape** — no leading structure
  beyond a single `:`-prefixed token, and `:Trig%50` even embeds a `%` character. Don't assume every
  command in this set decomposes into `:root:sub:sub` keywords.

## What's excluded and why

- **No GPIB or LAN interface exists on this instrument at all.** The DS1000E/D series programming
  guide documents only USB and RS-232 as remote-control interfaces — there was nothing to exclude on
  this front the way there is for product lines where a higher tier adds GPIB/LAN (contrast this
  repo's `rigol-dm3058e` manual, where the base DM3058 has GPIB/LAN but the "E" variant doesn't).
- **The entire `LA` Commands subsystem** (`:LA:DISPlay`, `:DIGital<n>:TURN`, `:DIGital<n>:POSition`,
  `:LA:THReshold`, `:LA:POSition:RESet`, `:LA:GROUp`, `:LA:GROUp<n>:SIZe`) — the source states
  outright these commands control "the acquisition and analysis to digital signals executed by
  logic analyzer," hardware that exists only on the "D"-suffix (mixed-signal) tier of this product
  line. The DS1102E ("E"-suffix, analog-only) has no logic analyzer and no digital channels, so this
  entire subsystem is inapplicable — same hardware-absence pattern used elsewhere in this repo for
  GPIB/LAN exclusions.
- **`:KEY:LA`** — simulates the front-panel LA button; excluded for the same hardware-absence
  reason. (`:KEY:OFF`'s LA leg is *not* excluded outright since it's part of a documented cycle that
  otherwise applies — see its gotcha above instead.)
- **The `DIGITAL`/`DIGital<m>`/`DIG0`–`DIG15` parameter options** scattered across otherwise
  in-scope commands — `:ACQuire:SAMPlingrate? DIGITAL`, `:TRIGger<mode>:SOURce DIGital<m>` (EDGE/
  PULSe modes), `:WAVeform:DATA? DIGital`, the `DIGITAL` row of `:WAVeform:POINts:MODE`'s point-count
  table, and `:TRIGger:PATTern:PATTern`'s optional `[<edge source>,<edge>]` extension — are all
  excluded from their respective commands' documented value sets above for the same reason: they
  require digital channels that don't exist on this model. Where the source explicitly says "only
  for DS1000D series" (the `SAMPlingrate` case), that's quoted directly; the others are excluded by
  the same evidenced reasoning (explicit `DIG0`–`DIG15` / digital-channel references) even where the
  source doesn't repeat the "only for DS1000D" phrase verbatim.
- **Chapter 3's Visual C++ / Visual Basic / LabVIEW example code** is not reproduced. It's
  environment-specific glue code (DLL loading, MFC control wiring) rather than SCPI command
  reference material; the "Worked end-to-end example" section above gives an equivalent SCPI-level
  walkthrough instead, which is portable to any language/library.
- **The "Command Quick Reference A-Z" appendix** is not reproduced as a separate section — this
  manual's "Command reference" above, organized by the same functional categories as the vendor
  guide's Chapter 2, already serves as the complete reference; a flat A-Z index would be redundant.
