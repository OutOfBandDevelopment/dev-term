# Tektronix TDS2024 — Programming Manual (GPIB / RS-232, via TDS2CMAX)

4-channel, 200 MHz, 2 GS/s digital storage oscilloscope, TDS2000 series. This manual covers
the command set reachable over **GPIB** and **RS-232**, using a **TDS2CMAX** rear-panel
communications extension module.

**This is the plain TDS2024, not the TDS2024B.** The "B" series is a later, different hardware
revision with a built-in USB port and its own (different) programmer manual — do not use this
document for a TDS2024B.

## Source document

**Tektronix Programmer Manual — TDS200, TDS1000, TDS2000, and TPS2000 Series Digital
Oscilloscopes**, Tektronix part number **071-1075-03**. This is the sole, canonical, current
vendor source for the TDS2024's command set. Two facts from the document confirm it applies
directly here, verbatim from its "Conventions" section:

> "References to the TDS2CMA Communications Extension Module include the TDS2CM and
> TDS2CMAX modules."

> "References to the TDS2014 and TDS2024 models include the TDS2004."

In other words: this manual's TDS2CMA command descriptions apply unchanged to a TDS2CMAX
module (the module the target unit uses), and its TDS2014/TDS2024 command descriptions apply
to this exact model. Where the source gives model- or module-specific syntax variants, this
document keeps only the TDS2024 + TDS2CMAX-relevant variant and notes in that command's own
gotcha that a different variant exists for a different model/module (rather than presenting an
ambiguous merged syntax).

## Before you start

### Physical connection

The base TDS2024 has **no built-in remote-control interface** — every communications port
comes from an optional rear-mounted extension module. This manual assumes a **TDS2CMAX**
module is installed, which provides:

| Port | Purpose |
|---|---|
| GPIB | IEEE-488 remote control (in scope) |
| RS-232 | Serial remote control (in scope) |
| Centronics | Parallel printer port (hardware present, but not a control interface — see "What's excluded and why") |

(The TDS2CMAX supersedes the older TDS2CM/TDS2CMA hardware but is treated as command-set
identical by the vendor manual, per the quote above. A different module, TDS2MEM, instead
provides RS-232 + Centronics + a CompactFlash card slot and *no* GPIB — this manual does not
cover a TDS2MEM setup; see "What's excluded and why.")

Connect either:
- **GPIB**: a standard IEEE-488 cable between the TDS2CMAX's GPIB port and the controller's
  GPIB interface. Each device on a GPIB bus needs a unique address; set the TDS2024's address
  from the front panel (Utility menu) or query/set it with the module's own address command
  per the TDS200 Series Extension Modules Instructions Manual (071-0409-XX) — GPIB address
  configuration itself is not part of the oscilloscope's SCPI-style command set documented
  here.
- **RS-232**: a standard RS-232 serial cable between the TDS2CMAX's RS-232 port and the
  controller's serial port (or a USB-to-RS232 adapter). Configure the port with the `RS232:*`
  commands documented below (baud, parity, flagging, terminator) so both ends agree.

### Driver / software prerequisites

No oscilloscope-specific driver is required for either interface beyond your GPIB
interface-card driver (for GPIB) or a standard serial port (for RS-232). Tektronix's own
"OpenChoice" PC communication software (TDSPCS1, bundled with the TDS2CMAX) or any terminal
program (e.g. HyperTerminal on Windows, `tip` on Unix) can talk to the RS-232 port directly;
any GPIB library (NI-VISA, Agilent/Keysight IO Libraries, or a raw GPIB driver) works for GPIB.

## Command syntax conventions

This is a **Tektronix** instrument — its command-syntax conventions differ in specific ways
from other vendors' SCPI dialects (e.g. Rigol). Conventions below are exactly as the vendor
manual states them, not adapted to match this repo's other manuals.

- **Command tree and colons.** Commands are hierarchical: a `<Header>` made of one or more
  `<Mnemonic>`s separated by `:` (e.g. `TRIGger:MAIn:EDGE:SLOpe`). A leading `:` returns to the
  root of the command tree; it is required when concatenating a command whose header differs
  from the previous one, and it must **never** precede a `*`-prefixed common command.
- **Set vs. query.** A command with no `?` is a set command. Appending `?` makes it a query.
  Not all commands have both forms — some are "Set Only," some "Query Only." A handful (e.g.
  `*CAL?`) both perform an action *and* return a result.
- **Abbreviation.** Every command may be sent using just its capitalized-letter abbreviation
  (shown throughout this manual as e.g. `ACQuire`, meaning `ACQ` also works) or spelled out in
  full; case is insensitive either way.
- **Headers in query responses.** The `HEADer` command controls whether query replies include
  the command header (`HEADer ON` → `:CH1:COUPLING DC`) or just the value (`HEADer OFF` → `DC`).
  `VERBose` separately controls whether those headers, when present, are abbreviated or spelled
  out in full.
- **Concatenation.** Chain multiple commands/queries with `;`. Rules (all from the vendor
  manual, verbatim in spirit):
  - Different headers need both `;` and a leading `:` on every command after the first:
    `TRIGger:MODe NORMal;:ACQuire:NUMAVg 16`
  - If the next command differs only in its last mnemonic, you may abbreviate and drop the
    leading `:`: `ACQuire:MODe AVErage;NUMAVg 16`
  - Never precede a `*` command with `:` or `;:` — `ACQuire:MODe AVErage;*TRG` is valid,
    `ACQuire:MODe AVErage;:*TRG` is not.
  - Concatenated queries return one combined response: `CH1:COUPling?;BANdwidth?` →
    `:CH1:COUPLING DC;:CH1:BANDWIDTH ON` (headers on).
  - A query returning arbitrary/indefinite-length data (e.g. `ID?`) must be **last** in a
    concatenated message, or the oscilloscope raises event 440.
- **Message terminators — this is genuinely interface-specific, unlike most of this manual:**
  - **GPIB**: terminated by the END message (EOI asserted with the last data byte), an ASCII
    line feed (LF) as the last byte, or both. The oscilloscope always sends LF+EOI.
  - **RS-232**: terminated by CR, LF, CRLF, or LFCR — the oscilloscope accepts all four as
    input regardless of the currently configured terminator (`RS232:TRANsmit:TERMinator`
    controls only what it *sends*). For a two-character combination, the second character is
    treated as a null command.
  - Indefinite-length block arguments (`#0...<terminator>`) should **not** be used over RS-232,
    since there is no way to embed a literal terminator byte inside RS-232 block data; GPIB
    signals block end via the EOI line instead, so it's safe there.
- **Numeric argument types**: `<NR1>` = signed integer, `<NR2>` = floating point without
  exponent, `<NR3>` = floating point with exponent (e.g. `2.5E-6`). These are also the formats
  the oscilloscope uses in its own query replies. Out-of-range numeric arguments are clamped to
  the nearest valid value (not rejected) and the command still executes.
- **Quoted strings** (`<QString>`): ASCII text in matching single or double quotes; embed a
  quote character by doubling it (`"here is a "" mark"`). Max 1000 characters on a query
  response. A `<QString>` cannot itself contain the GPIB END message before its closing quote.
- **Block arguments** (`<Block>`): binary data framed as `#<N><count><data>`, e.g.
  `#217ACQuire:STATE RUN` (the `2` says 2 digits follow, `17` is the byte count, then 17 bytes
  of data). Used for `CURVe`/`*DDT` binary payloads.
- **Constructed mnemonics** relevant on a 4-channel TDS2024: `CH<x>` is `CH1`–`CH4`; `REF<x>`
  is `REFA`–`REFD`; `<wfm>` is any of `CH<x>`, `MATH`, or `REF<x>`; `MEAS<x>` is `MEAS1`–`MEAS5`
  (TDS2000 series allows 5 concurrent on-screen measurements, vs. 4 on TDS200 series).

## Command reference

Commands are grouped by the vendor manual's own "Command Groups" chapter structure, in the
same order. Per-command detail (syntax, description, query form, example, gotchas) is drawn
from the manual's alphabetical "Command Descriptions" reference.

### Acquisition Commands

- **`ACQuire?`** *(Query Only)* — Returns all current acquisition settings, e.g.
  `ACQUIRE:STOPAFTER RUNSTOP;STATE 1;MODE SAMPLE;NUMAVG 16`.
- **`ACQuire:MODe { SAMple | PEAKdetect | AVErage }` / `?`** — Sets how each acquisition
  interval's final displayed value is derived from the underlying samples. `SAMple` (default)
  keeps the first sample per interval; `PEAKdetect` shows the high-low range (reveals
  aliasing); `AVErage` averages N acquisitions (N set by `ACQuire:NUMAVg`). Waveform data is
  always 8-bit precision regardless of mode; a `CURVe?` with 16-bit width zero-pads the low
  byte.
- **`ACQuire:NUMACq?`** *(Query Only)* — Returns (`<NR1>`) the count of acquisitions since
  acquisition started; resets to 0 on most Acquisition/Horizontal/Vertical/Trigger changes
  (exceptions documented per-model in the source; e.g. changing trigger level in Sample/Peak
  Detect mode does not reset it). Always 0 in Scan mode. **Gotcha:** any settings change while
  in Average mode aborts the running average and resets this to 0.
- **`ACQuire:NUMAVg <NR1>` / `?`** — Number of acquisitions averaged together in Average mode.
  Valid values: **4, 16, 64, 128** only.
- **`ACQuire:STATE { OFF | ON | RUN | STOP | <NR1> }` / `?`** — Starts/stops acquisition;
  equivalent to the front-panel RUN/STOP button. `RUN`/`ON` mid-sequence restarts the sequence
  and resets `NUMACq`. **Gotcha:** to detect completion of a single-sequence acquisition, use
  `*OPC?` rather than polling `ACQuire:STATE?` — the vendor manual explicitly recommends this.
- **`ACQuire:STOPAfter { RUNSTop | SEQuence }` / `?`** — `RUNSTop`: acquisition state is
  governed purely by the RUN/STOP button / `ACQuire:STATE`. `SEQuence`: "single sequence" —
  stop automatically once enough waveforms are acquired to satisfy the current acquisition mode
  (1 trigger for Sample/Peak Detect, N triggers for Average of N). This is the standard setup
  for a scripted single-shot capture.

### Calibration and Diagnostic Commands

- **`*CAL?`** *(Query Only, but performs an action)* — Runs internal self-calibration; returns
  `0` on success, nonzero on failure. **Gotcha:** takes several minutes; the oscilloscope
  executes no other commands meanwhile. Disconnect all input signals first.
- **`CALibrate:ABOrt`** *(Set Only)* — Aborts an in-progress **factory** calibration sequence,
  restoring prior calibration constants. Service-environment use only.
- **`CALibrate:CONTINUE`** *(Set Only)* — Advances to the next step of a factory calibration
  sequence. Service-environment use only.
- **`CALibrate:FACtory`** *(Set Only)* — Starts the factory calibration sequence (a series of
  steps advanced via `CALibrate:CONTINUE`). Only synchronization commands (`*OPC`, `*OPC?`,
  `*WAI`, `BUSY?`) are accepted while it runs. Service-environment use only.
- **`CALibrate:INTERNAL`** *(Set Only)* — Same as `*CAL?` (self-calibration) but returns no
  status. Same multi-minute caveat.
- **`CALibrate:STATUS?`** *(Query Only)* — `PASS`/`FAIL` status of the last self- or factory
  calibration since power-up.
- **`DIAg:RESUlt:FLAg?`** *(Query Only)* — `PASS`/`FAIL` summary of the last diagnostic test
  run (power-on or Service Menu).
- **`DIAg:RESUlt:LOG?`** *(Query Only)* — Full results log as
  `<Status>,<Module name>[,<Status>,<Module name>...]`, e.g. `"pass-CPU, pass-ACQ1, pass-EXTENSION"`.
- **`ERRLOG:FIRST?`** *(Query Only)* — First entry in the internal error log (empty string if
  none). Use with `ERRLOG:NEXT?` to walk the whole log.
- **`ERRLOG:NEXT?`** *(Query Only)* — Next entry in the error log; empty string at end.

### Cursor Commands

- **`CURSor?`** *(Query Only)* — All current cursor settings.
- **`CURSor:FUNCtion { HBArs | OFF | VBArs }` / `?`** — Selects horizontal-bar (vertical-unit)
  or vertical-bar (time/frequency-unit) cursors, or turns cursors off. **Gotcha:** setting this
  while `DISplay:FORMat` is `XY` generates event 221 (Settings conflict) and is ignored — `XY`
  mode has no cursors.
- **`CURSor:HBArs?`** *(Query Only)* — Current horizontal-bar cursor settings.
- **`CURSor:HBArs:DELTa?`** *(Query Only)* — Vertical difference (`<NR3>`) between the two
  horizontal-bar cursors. Returns `9.9E37` and raises event 221 if Trigger View is active.
- **`CURSor:HBArs:POSITION<x> <NR3>` / `?`** (`<x>` = 1 or 2) — Positions one horizontal-bar
  cursor, in the units reported by `CURSor:HBArs:UNIts?` (volts/amps/divisions/dB depending on
  source), relative to ground or screen center as appropriate. Clamped to the graticule.
- **`CURSor:HBArs:UNIts?`** *(Query Only)* — `VOLts`, `DIVs`, `DECIBELS` (FFT sources only), or
  `UNKNOWN` (Trigger View active, also raises event 221).
- **`CURSor:SELect:SOUrce <wfm>` / `?`** — Sets which waveform's scale factors the cursors
  measure against.
- **`CURSor:VBArs?`** *(Query Only)* — Current vertical-bar cursor position/units settings.
- **`CURSor:VBArs:DELTa?`** *(Query Only)* — Time/frequency difference (`<NR3>`) between the
  two vertical-bar cursors, in the unit set by `CURSor:VBArs:UNIts` (always Hz if source is an
  FFT math waveform, regardless of that setting). `9.9E37` + event 221 if Trigger View active.
- **`CURSor:VBArs:POSITION<x> <NR3>` / `?`** (`<x>` = 1 or 2) — Positions a vertical-bar
  cursor in seconds or Hz (per `CURSor:VBArs:UNIts`), relative to the trigger point (except for
  an FFT math source). Clamped to the graticule.
- **`CURSor:VBArs:UNIts { SECOnds | HERtz }` / `?`** — Sets vertical-bar cursor unit. Raises
  event 221 on query while Trigger View is active.

### Display Commands

- **`DISplay?`** *(Query Only)* — All current display settings.
- **`DISplay:CONTRast <NR1>` / `?`** — LCD contrast, integer **1–100**.
- **`DISplay:FORMat { XY | YT }` / `?`** — `YT` (default) is voltage-vs-time. `XY` plots CH1
  (horizontal) against CH2 (vertical) and **turns cursors off**; sending `CURSor:FUNCtion`
  while in `XY` raises event 221 and is ignored.
- **`DISplay:INVert { ON | OFF }` / `?`** — **Gotcha, TDS2024-specific:** on the TDS2000 series
  (which includes the TDS2024), this command is accepted for compatibility but **has no actual
  effect** — the query always returns `OFF`. It genuinely works only on the TDS1000 series. Do
  not expect a white-on-black display from this command on this instrument.
- **`DISplay:PERSistence { 1 | 2 | 5 | INF | OFF }` / `?`** — How long waveform points persist
  on screen. Query returns `0` (off), `2`/`5` (seconds), or `99` (infinite).
- **`DISplay:STYle { DOTs | VECtors }` / `?`** — Individual points vs. connected vectors.

### Hard Copy Commands

These commands are sent over GPIB or RS-232 like any other command — only the printed *output*
itself, when you route it to `CENtronics`, leaves via the Centronics port; the command channel
is unaffected. `GPIb` and `RS232` are also valid hard-copy *output* destinations in their own
right (e.g. transferring a BMP screen capture back over the same RS-232 link you're already
using for control).

- **`HARDCopy { ABOrt | STARt }` / `?`** — Starts or aborts a hard-copy operation to the port
  set by `HARDCopy:PORT`. **Not IEEE 488.2 compatible.** Query returns format/layout/port.
  **Gotcha:** a GPIB Device Clear (DCL) does *not* abort an in-progress hard copy — you must
  send `HARDCopy ABOrt` first, then DCL to clear the output queue. Use `*WAI` between
  successive `HARDCopy STARt` commands so the first finishes before the next starts.
- **`HARDCopy:FORMat { BMP | BUBBLEJet | DESKJet | DPU3445 | DPU411 | DPU412 | EPSC60 | EPSC80 |
  EPSIMAGE | EPSOn | LASERJet | PCX | RLE | THINKjet | TIFF }` / `?`** — Output file/printer
  format. (`INTERLEAF` is TDS200-only, omitted here.) `RLE` and `TIFF` are TDS1000/TDS2000/
  TPS2000-only — both valid on the TDS2024. `EPSC60`/`EPSC80` require firmware 2.12+ (2-channel)
  or 4.12+ (4-channel, i.e. this instrument) — check `*IDN?`'s firmware field if these fail.
- **`HARDCopy:INKSaver { ON | OFF }` / `?`** — **In scope for TDS2024**: this option is
  documented as "(TDS2000 and TPS2000 only)" and genuinely functions on this instrument (it
  has no effect specifically on plain TDS1000 units, which is a different exclusion than it
  first appears). `ON` (default) prints on a white background; `OFF` prints WYSIWYG
  (color-on-black).
- **`HARDCopy:LAYout { LANdscape | PORTRait }` / `?`** — Print orientation.
- **`HARDCopy:PORT { CENtronics | RS232 | GPIb }` / `?`** — Where the next `HARDCopy STARt`
  sends its output. **Gotcha:** if you route a `BMP` image over `RS232`, remember BMP is
  binary and contains no line-feed terminator to detect end-of-transfer — either parse the BMP
  header's byte count, or set a generously long RS-232 read timeout (the vendor manual's own
  example: ~300 seconds for an 80 kB file at 9600 baud).

### Horizontal Commands

You may substitute `SECdiv` for `SCAle` in any of these (kept for compatibility with older
Tektronix scopes) — both forms are shown below where the source lists them.

- **`HORizontal?`** *(Query Only)* — All horizontal settings (main + window time base).
- **`HORizontal:DELay?`** *(Query Only)* — Window (delayed) time base settings.
- **`HORizontal:DELay:POSition <NR3>` / `?`** — Window position in seconds, relative to the
  trigger point at center graticule; positive places the trigger before center.
- **`HORizontal:DELay:SCAle <NR3>` / `?`** (also `HORizontal:DELay:SECdiv`) — Window time base
  seconds/division. Values snap to a **1-2.5-5 sequence**; out-of-sequence values round to the
  nearest valid step. If set slower than the main time base, both main and window scale to
  match.
- **`HORizontal:MAIn?`** *(Query Only)* — Main time base settings.
- **`HORizontal:MAIn:POSition <NR3>` / `?`** (also plain `HORizontal:POSition`) — Main trigger
  position in seconds from center graticule; positive = trigger before center.
- **`HORizontal:MAIn:SCAle <NR3>` / `?`** (also `HORizontal:MAIn:SECdiv`, `HORizontal:SCAle`,
  `HORizontal:SECdiv`) — Main time base seconds/division, 1-2.5-5 sequence, rounded to nearest
  valid value if out of sequence.
- **`HORizontal:RECOrdlength?`** *(Query Only)* — Always returns **2500** on this whole product
  family (even in FFT mode) — provided only for cross-model compatibility. Prefer
  `WFMPre:NR_Pt?` for the actual transferred point count.
- **`HORizontal:VIEW { MAIn | WINDOW | ZONE }` / `?`** — `MAIn`: standard single time base.
  `WINDOW`: acquire/display using the delayed (window) time base. `ZONE`: same as `MAIn` but
  overlays cursor bars showing what the window time base would capture.

### Math Commands

- **`MATH?`** *(Query Only)* — Current math waveform definition and display parameters.
- **`MATH:DEFINE <QString>` / `?`** — Defines the math waveform. **TDS2024-specific valid
  values** (the source gives a different list per model/module — this is the TDS2014/TDS2024
  set):
  `"CH1+CH2"`, `"CH3+CH4"`, `"CH1-CH2"`, `"CH2-CH1"`, `"CH3-CH4"`, `"CH4-CH3"`,
  `"FFT(CH<x>[, <window>])"` where `<window>` is `HANning`, `FLATtop`, or `RECTangular`.
  Activate/deactivate the result with `SELect:MATH`. **Gotcha:** other TDS200/TDS1000/TDS2000/
  TPS2000 model-and-module combinations accept a *different* argument list (e.g. plain
  subtraction-via-invert-then-add on older TDS210/220 firmware) — don't reuse this exact list
  for a different model.
- **`MATH:FFT?`** *(Query Only)* — Current FFT display settings. In scope for the TDS2024
  (documented as available on "TDS1000, TDS2000, and TPS2000... as well as TDS200 with a
  TDS2MM module").
- **`MATH:FFT:HORizontal:POSition <NR3>` / `?`** — Percent-of-record-length (0–100, default 50)
  centered on the display; rounds to nearest workable value.
- **`MATH:FFT:HORizontal:SCAle <NR3>` / `?`** — Horizontal zoom factor. Valid: **1, 2, 5, 10**
  (rounds to nearest).
- **`MATH:FFT:VERtical:POSition <NR3>` / `?`** — Vertical position in divisions from center.
- **`MATH:FFT:VERtical:SCAle <NR3>` / `?`** — Vertical zoom factor. Valid: **0.5, 1, 2, 5, 10**
  (rounds to nearest).

### Measurement Commands

Up to **5** concurrent automated measurements (`MEAS1`–`MEAS5`) on this TDS2000-series
instrument. The vendor manual strongly recommends `MEASUrement:IMMed:*` over
`MEASUrement:MEAS<x>:*` for programmatic use — immediate measurements have no front-panel
display equivalent, are computed only on demand, and so cost less waveform-update overhead.

- **`MEASUrement?`** *(Query Only)* — All measurement settings (all `MEAS<x>` plus immediate).
- **`MEASUrement:IMMed?`** *(Query Only)* — All immediate-measurement setup parameters.
- **`MEASUrement:IMMed:SOUrce1 CH<x>` / `?`** — Source channel for the immediate measurement.
- **`MEASUrement:IMMed:TYPe { FREQuency | MEAN | PERIod | PHAse | PK2pk | CRMs | MINImum |
  MAXImum | RISe | FALL | PWIdth | NWIdth }` / `?`** — Measurement type. `MINImum`/`MAXImum`
  are TDS1000/TDS2000/TPS2000-only (in scope). `RISe`/`FALL`/`PWIdth`/`NWIdth` require an
  edge/pulse to actually be displayed to measure. (TPS2000-Power-Analysis-only types —
  `WFCREST`, `WFFREQ`, `WFCYCRMS`, `TRUEPOWER`, `VAR`, `POWERFACTOR`, `PFPHASE` — are omitted;
  see "What's excluded and why.")
- **`MEASUrement:IMMed:UNIts?`** *(Query Only)* — `"V"`, `"s"`, or `"Hz"` as appropriate.
- **`MEASUrement:IMMed:VALue?`** *(Query Only)* — Executes and returns (`<NR3>`) the immediate
  measurement. Returns `9.9E37` + raises event 2225 if the source channel isn't displayed, or
  event 221 if Trigger View/Scan/XY mode is active. Check `*ESR?`/`ALLEv?` after any measurement
  query that might have failed silently on the wire.
- **`MEASUrement:MEAS<x>?`** *(Query Only, `<x>` = 1–5)* — All parameters for that periodic
  on-screen measurement slot.
- **`MEASUrement:MEAS<x>:SOUrce CH<y>` / `?`** — Source channel for measurement slot `<x>`.
- **`MEASUrement:MEAS<x>:TYPe { FREQuency | MEAN | PERIod | PK2pk | CRMs | MINImum | MAXImum |
  RISe | FALL | PWIdth | NWIdth | NONe }` / `?`** — Measurement type for slot `<x>`; `NONe`
  disables that slot. Setting anything but `NONe` displays the MEASURE menu on-screen.
- **`MEASUrement:MEAS<x>:UNIts?`** *(Query Only)* — Units for that slot, or empty string if
  type is `NONe`.
- **`MEASUrement:MEAS<x>:VALue?`** *(Query Only)* — Current displayed value (`<NR3>`), updated
  roughly every 0.5s while displayed. Returns `9.9E37` + event 2231 if type is `NONe`, or event
  2225 if the source channel isn't displayed.

### Miscellaneous Commands

- **`AUTOSet EXECute`** *(Set Only)* — Auto-adjusts vertical/horizontal/trigger for a stable
  display; equivalent to the front-panel AUTOSET button.
- **`AUTOSet:SIGNAL?`** *(Query Only, TDS1000/TDS2000/TPS2000 — in scope)* — Type of signal the
  last Autoset found: `LEVEL | SINE | SQUARE | VIDPAL | VIDNTSC | OTHER | NONe`.
- **`AUTOSet:VIEW { MULTICYcle | SINGLECYcle | FFT | RISINGedge | FALLINGedge | FIELD | ODD |
  EVEN | LINE | LINENum | DCLIne | DEFault | NONE }` (TDS1000/TDS2000/TPS2000 — in scope)** —
  Controls which specific view Autoset selects for the detected signal type. Set is ignored
  outside the Autoset menu / for an invalid view (raises event 221).
- **`*DDT { <Block> | <QString> }` / `?`** — Defines the command sequence that `*TRG` (or a
  GPIB Group Execute Trigger) runs, e.g. `*DDT #217ACQuire:STATE RUN<EOI>`. Max 80 characters.
- **`FACtory`** *(Set Only)* — Resets to factory defaults. Clears/resets status-reporting
  registers, `HEADer`, `*DDT`; leaves RS-232/GPIB state, GPIB address, front-panel lock,
  `VERBose`, calibration data, stored settings/waveforms, hard-copy params, and language
  selection untouched. Effectively performs `DATa INIT` too.
- **`HDR`** — Alias for the `HEADer` query, kept for cross-model compatibility.
- **`HEADer { ON | OFF | <NR1> }` / `?`** — Whether query responses include command headers.
  Does not affect `*`-prefixed common commands, which never return headers regardless.
- **`ID?`** *(Query Only)* — Identification in Tektronix Codes and Formats notation. With a
  TDS2CMA/TDS2CMAX module: `TEK/<model>,CF:91.1CT,FV:v<fw>,TDS2CM:CMV:v<module fw>`. **Gotcha:**
  must be the last command in a concatenated message (raises event 440 otherwise).
- **`*IDN?`** *(Query Only)* — IEEE 488.2-style identification, e.g.
  `TEKTRONIX,TDS 2024,0,CF:91.1CT FV:v2.12 TDS2CM:CMV:v1.04`. Same last-in-concatenation rule
  as `ID?`.
- **`LANGuage { ENGLish | FRENch | GERMan | ITALian | PORTUguese | SPANish | JAPAnese | KOREan |
  TRADitionalchinese | SIMPlifiedchinese }` / `?`** — On-screen message language.
- **`LOCk { ALL | NONe }` / `?`** — Disables/enables all front-panel buttons and knobs; no
  front-panel equivalent action exists for this (it's remote-only). `NONe` = `UNLock ALL`.
- **`*LRN?`** *(Query Only)* — Identical to `SET?` (below); returns most current settings as a
  sendable command string.
- **`REM <QString>`** *(Set Only)* — Comment; ignored by the oscilloscope. Max 80 characters.
- **`SET?`** *(Query Only)* — Returns most oscilloscope settings as a concatenated command
  string you can resend to restore state. **Gotcha:** always returns full headers regardless of
  the `HEADer` setting (since the point is round-tripping); `VERBose` still controls whether
  those headers are abbreviated.
- **`*TRG`** *(Set Only)* — Immediately executes the command sequence defined by `*DDT`.
- **`*TST?`** *(Query Only)* — Self-test; always returns `0`.
- **`UNLock ALL`** *(Set Only)* — Re-enables all front-panel controls; equivalent to
  `LOCk NONe`. **Gotcha:** has no effect if the bus has put the instrument in GPIB's Remote
  With Lockout State (RWLS) per IEEE-488.1 §2.8.3.
- **`VERBose { ON | OFF | <NR1> }` / `?`** — Full-length vs. minimum-length keywords in
  applicable query responses. Does not affect `*`-prefixed common commands.

### RS-232 Commands

This whole group is directly in scope — it's the configuration surface for the interface you're
using.

- **`RS232?`** *(Query Only)* — All current RS-232 settings, e.g. `:RS232:BAUD 9600;
  SOFTFLAGGING 0;HARDFLAGGING 1;PARITY NONE;TRANSMIT:TERMINATOR LF`.
- **`RS232:BAUd <NR1>` / `?`** — Baud rate: **300, 600, 1200, 2400, 4800, 9600, or 19200**
  only. **Gotcha:** if no flow control is used and you send another command immediately after
  changing baud (before the new rate has taken effect), the first few characters of that next
  command can be lost.
- **`RS232:HARDFlagging { ON | OFF }` / `?`** — Hardware (RTS/CTS/DTR) flow control. Sending
  waits for CTS asserted; receiving asserts RTS until the input buffer nearly fills, then stops
  asserting it (further incoming data past that point overruns the buffer and raises an error).
  DTR is asserted whenever the oscilloscope is powered on. **Mutually exclusive with soft
  flagging** — enabling one disables the other.
- **`RS232:PARity { EVEN | ODD | NONe }` / `?`** — Parity for all RS-232 transfers. A mismatch
  on input raises a parity error. Same "first few characters may be lost if you don't wait"
  caveat as baud rate.
- **`RS232:SOFTFlagging { ON | OFF }` / `?`** — Software (XON/XOFF) flow control. **Gotcha:**
  if enabled while transferring binary data that happens to contain an XON/XOFF byte value,
  transmission will lock up — disable soft flagging before any binary transfer (e.g. `CURVe?`
  with binary encoding, or a `BMP` hard copy over RS232). Mutually exclusive with hard flagging.
- **`RS232:TRANsmit:TERMinator { CR | LF | CRLf | LFCr }` / `?`** — EOL terminator the
  oscilloscope appends when *sending*. It accepts all four as valid *input* regardless of this
  setting (see "Command syntax conventions" above).

### Save and Recall Commands

Memory-location ranges below are the **TDS1000/TDS2000/TPS2000-series** values (1–10) — the
older TDS200 series uses a smaller range (1–5); don't reuse the wrong range for a different
model.

- **`*RCL <NR1>`** *(Set Only)* — Restores oscilloscope state from setup memory location
  **1–10**. Equivalent to `RECAll:SETUp`.
- **`RECAll:SETUp { FACtory | <NR1> }`** *(Set Only)* — `FACtory` restores factory defaults
  (equivalent to the front-panel DEFAULT SETUP button); `<NR1>` (1–10) recalls a saved setup.
  (The source's `<file path>` variant is TDS2MEM/TPS2000-only — excluded here, see "What's
  excluded and why.")
- **`*SAV <NR1>`** *(Set Only)* — Saves current oscilloscope state to setup memory location
  **1–10**, overwriting anything already there.
- **`SAVe:SETUp <NR1>`** *(Set Only)* — Same as `*SAV`. (The `<file path>` variant is
  TDS2MEM-only — excluded here.)
- **`SAVe:WAVEform <wfm>, REF<x>`** *(Set Only)* — Stores `CH<y>` or `MATH` into one of the
  nonvolatile `REF<x>` waveform slots. (The `<file path>`-to-CompactFlash variant is
  TDS2MEM/TPS2000-only — excluded here.) Use `SELect:REF<x>` to display it afterward.

### Status and Error Commands

Common to GPIB and RS-232 alike — Tektronix's IEEE 488.2 + Tek Standard Codes and Formats
status/event system. See "Common gotchas" below for the full register/queue model and
synchronization guidance; per-command summaries:

- **`ALLEv?`** *(Query Only)* — Drains and returns *all* queued events as
  `<code>,<QString>[,<code>,<QString>...]`, each message optionally including the offending
  command text. Equivalent to repeated `EVMsg?` calls.
- **`BUSY?`** *(Query Only)* — `1` if the oscilloscope is mid-way through one of the
  operations in the *OPC table (single-sequence acquisition, hard copy, self-cal), else `0`.
- **`*CLS`** *(Set Only)* — Clears the Event Queue, SESR, and SBR (except the MAV bit, which
  only clears immediately after an `<EOM>`, or via a GPIB Device Clear). **Gotcha:** can
  suppress a pending `*OPC`-triggered service request if issued while a hard copy or single
  sequence is still in flight.
- **`DESE <NR1>` / `?`** — Device Event Status Enable Register (0–255 bitmask); gates which
  event *types* get summarized into the SESR and Event Queue at all.
- **`*ESE <NR1>` / `?`** — Event Status Enable Register (0–255 bitmask); gates which SESR bits
  get summarized into the ESB bit of the Status Byte Register.
- **`*ESR?`** *(Query Only)* — Reads (and clears) the Standard Event Status Register — "the
  usual way to determine whether a set command executed without error," per the vendor manual.
- **`EVENT?`** *(Query Only)* — Pops one event code (`<NR1>`) from the Event Queue.
- **`EVMsg?`** *(Query Only)* — Pops one event, code + human-readable message.
- **`EVQty?`** *(Query Only)* — Count (`<NR1>`) of events currently queued — useful to know how
  many `ALLEv?` will return.
- **`*OPC` / `*OPC?`** — Two different synchronization mechanisms; see "Common gotchas."
- **`*PSC <NR1>` / `?`** — Power-on status-clear flag. `1` (or nonzero): DESER/SRER/ESER reset
  to fixed defaults at power-on and SRQ cannot fire immediately after power-up. `0`: those
  registers persist across power cycles in nonvolatile memory, and a PON event can trigger SRQ
  if separately enabled.
- **`*RST`** *(Set Only)* — Resets to factory-default *settings* (a subset of `FACtory`) but
  does not purge stored setups/waveforms, calibration data, GPIB address, or the various
  enable-register/lock/verbose states — see the per-command notes above under `FACtory` for the
  full untouched list (largely the same list).
- **`*SRE <NR1>` / `?`** — Service Request Enable Register (0–255 bitmask); gates which SBR
  bits can assert a GPIB service request / set the MSS bit.
- **`*STB?`** *(Query Only)* — Reads the Status Byte Register (bit 6 = MSS via this query, vs.
  RQS if read via a GPIB serial poll instead).
- **`*WAI`** *(Set Only)* — Blocks further command processing until all pending
  operation-complete-generating operations finish. See "Common gotchas."

### Trigger Commands

Two trigger families apply here: **Edge** (default, all models) and **Pulse Width** and
**Video** (TDS1000/TDS2000/TPS2000 — both in scope for the TDS2024; Pulse trigger is *not*
available on the plain TDS200 series).

- **`TRIGger FORCe` / `TRIGger?`** — Forces a trigger event (only takes effect if
  `TRIGger:STATE` is `REAdy`); query returns full current trigger settings.
- **`TRIGger:MAIn SETLevel` / `TRIGger:MAIn?`** — Sets trigger level to 50% of the source
  signal's min/max; query returns main trigger settings. **Gotcha:** raises event 221 if sent
  while acquisition state is `STOP`.
- **`TRIGger:MAIn:EDGE?`** *(Query Only)* — Coupling/source/slope for the edge trigger.
- **`TRIGger:MAIn:EDGE:COUPling { AC | DC | HFRej | LFRej | NOISErej }` / `?`** —
  `HFRej`/`LFRej` remove high/low-frequency components before triggering; `NOISErej` needs more
  signal amplitude for a stable trigger (less false-triggering).
- **`TRIGger:MAIn:EDGE:SLOpe { FALL | RISe }` / `?`**
- **`TRIGger:MAIn:EDGE:SOUrce { CH<x> | EXT | EXT5 | EXT10 | LINE }` / `?`** — `EXT10` is
  TPS2000-only (omit on TDS2024). `LINE` (power-line trigger) is not available on TPS2000 but
  is available here.
- **`TRIGger:MAIn:FREQuency?`** *(Query Only, TDS1000/TDS2000/TPS2000 — in scope)* — Edge or
  pulse-width trigger frequency (matches the front-panel readout). Returns `9.9E37` + event
  2207 below 10 Hz, or `9.9E37` + event 221 if trigger type is Video.
- **`TRIGger:MAIn:HOLDOff?`** *(Query Only)* — Current holdoff value.
- **`TRIGger:MAIn:HOLDOff:VALue <NR3>` / `?`** — Trigger holdoff, **500ns to 10s**.
- **`TRIGger:MAIn:LEVel <NR3>` / `?`** — Trigger level in volts, for Edge (any model) and Pulse
  Width (TDS1000/TDS2000/TPS2000) triggers. **Gotcha:** ignored (raises event 221) if edge
  source is `LINE`; query then returns 0.
- **`TRIGger:MAIn:MODe { AUTO | NORMal }` / `?`** — `AUTO` free-runs if no trigger is seen
  within a timeout (also enables Scan mode at ≥100 ms/div); `NORMal` waits indefinitely for a
  real trigger.
- **`TRIGger:MAIn:PULse?`** *(Query Only, TDS1000/TDS2000/TPS2000 — in scope)* — Current pulse
  trigger settings.
- **`TRIGger:MAIn:PULse:SOUrce { CH<x> | EXT | EXT5 | EXT10 }` / `?`** — `EXT10` is
  TPS2000-only.
- **`TRIGger:MAIn:PULse:WIDth?`** *(Query Only)* — Current pulse-width trigger settings.
- **`TRIGger:MAIn:PULse:WIDth:POLarity { POSITIVe | NEGAtive }` / `?`**
- **`TRIGger:MAIn:PULse:WIDth:WHEN { EQual | NOTEqual | INside | OUTside }` / `?`** — `EQual`:
  trigger on trailing edge at exactly the specified width. `NOTEqual`: trailing edge before
  spec width, or pulse continues past spec width without a trailing edge. `INside`: narrower
  than spec. `OUTside`: pulse runs longer than spec ("time-out" trigger).
- **`TRIGger:MAIn:PULse:WIDth:WIDth <NR3>` / `?`** — Width in seconds, **33ns to 10s**;
  resolution varies, value forced to nearest achievable.
- **`TRIGger:MAIn:TYPe { EDGE | VIDeo | PULse }` / `?`** — `PULse` is not available on the
  plain TDS200 series but is available here.
- **`TRIGger:MAIn:VIDeo?`** *(Query Only)* — Current video trigger settings.
- **`TRIGger:MAIn:VIDeo:LINE <NR1>` / `?`** *(TDS1000/TDS2000/TPS2000 — in scope)* — Line
  number when `VIDeo:SYNC` is `LINENum`. Range 1–525 (NTSC) or 1–625 (PAL/SECAM).
- **`TRIGger:MAIn:VIDeo:POLarity { INVert | NORMal }` / `?`** — `INVert`/`NORMal` (older
  TDS210/220 firmware below V2.00 with a TDS2CMA used `INVERTed` instead of `INVert` — not
  applicable to this instrument, noted only because the vendor manual calls it out explicitly).
- **`TRIGger:MAIn:VIDeo:SOUrce { CH<x> | EXT | EXT5 | EXT10 }` / `?`** — `EXT10` is
  TPS2000-only.
- **`TRIGger:MAIn:VIDeo:STANdard { NTSc | PAL }` / `?`** *(TDS1000/TDS2000/TPS2000 — in scope)*
  — `NTSC` is default; `PAL` also covers SECAM.
- **`TRIGger:MAIn:VIDeo:SYNC { FIELD | LINE | ODD | EVEN | LINENum }` / `?`** — `ODD`/`EVEN`/
  `LINENum` are TDS1000/TDS2000/TPS2000-only (all in scope here).
- **`TRIGger:STATE?`** *(Query Only)* — `ARMED | READY | TRIGGER | AUTO | SAVE | SCAN`.
  **Gotcha:** real-time reporting accuracy within a single acquisition is limited by sweep
  speed and bus/task latency — for reliably detecting single-sequence completion, use `*OPC?`
  instead of polling this.

### Vertical Commands

- **`CH<x>?`** *(Query Only, `<x>` = 1–4 on this 4-channel model)* — All vertical settings for
  that channel, e.g. `CH1:SCALE 1.0E0;POSITION 0.0E0;COUPLING DC;BANDWIDTH OFF;PROBE 1.0E0`.
  (`CH<x>:VOLts` and `CH<x>:SCAle` are identical; only `SCAle` is ever returned.)
- **`CH<x>:BANdwidth { ON | OFF }` / `?`** — `ON` limits to 20 MHz; `OFF` uses full bandwidth
  (up to 200 MHz on this model). **Gotcha:** at vertical scales 2.00–4.99 mV/div (at the BNC,
  after probe factor), full bandwidth is itself capped at 20 MHz regardless of this setting —
  a TDS1000/TDS2000/TPS2000-specific threshold (the TDS200 series uses a different threshold).
- **`CH<x>:COUPling { AC | DC | GND }` / `?`**
- **`CH<x>:INVert { ON | OFF }` / `?`** — Not usable with a TDS210/TDS220 + firmware below
  V2.00 + TDS2CMA (not relevant to this TDS2024/TDS2CMAX combination, called out for
  cross-model completeness only).
- **`CH<x>:POSition <NR3>` / `?`** — Vertical position in divisions from center graticule,
  applied *before* digitization. Valid range depends on `CH<x>:SCAle` (with a 1X probe: ±1000
  divs at 2 mV/div down to ±10 divs at 5 V/div — see the vendor manual's Table 2-27 for the
  full step table if precise limits matter).
- **`CH<x>:PRObe { 1 | 10 | 100 | 1000 }` / `?`** — **TDS2024-specific value set**: the source
  also lists `20`, `50`, and `500`, but marks them "(TPS2000 Series)" — not valid on this
  instrument. Stick to `1`/`10`/`100`/`1000` here.
- **`CH<x>:SCAle <NR3>` / `?`** (also `CH<x>:VOLts`, identical, kept for compatibility) —
  Volts (or amps) per division; 1X-probe range is 2 mV/div to 5 V/div.
- **`SELect?`** *(Query Only)* — Display on/off state of every waveform slot, e.g. (4-channel):
  `:SELECT:CH1 1;CH2 1;CH3 1;CH4 1;MATH 0;REFA 1;REFB 0;REFC 0;REFD 1`.
- **`SELect:<wfm> { ON | OFF }` / `?`** — Show/hide a specific `CH<x>`, `MATH`, or `REF<x>`
  waveform; equivalent to activating it from the front panel.

(`CH<x>:CURRENTPRObe` and `CH<x>:YUNit` are TPS2000-only — omitted; see "What's excluded and
why.")

### Waveform Commands

The command group used for capturing and transferring waveform data — see the worked example
below for the full end-to-end sequence.

- **`CURVe { <Block> | <asc curve> }` / `CURVe?`** — Transfers waveform data. `CURVe?` sends
  from the source set by `DATa:SOUrce`, over the points range set by `DATa:STARt`/`DATa:STOP`,
  in the format set by `DATa:ENCdg`/`DATa:WIDth`. The set form loads external data into the
  slot set by `DATa:DESTination`, starting at `DATa:STARt`. **Gotcha:** `CURVe?` on an
  undisplayed source returns nothing and raises events 2244 + 420; in Scan mode, roughly one
  division's worth of points near the moving cursor are invalid (blanked).
- **`DATa { INIT }` / `DATa?`** — `DATa INIT` resets all `DATa:*` settings to factory defaults
  (`DESTINATION=REFA`, `ENCDG=RIBINARY`, `SOURCE=CH1`, `START=1`, `STOP=2500`, `WIDTH=1`).
- **`DATa:DESTination REF<x>` / `?`** (also `DATa:TARget`, identical) — Where incoming `CURVe`
  data is stored.
- **`DATa:ENCdg { ASCIi | RIBinary | RPBinary | SRIbinary | SRPbinary }` / `?`** — `ASCIi`:
  comma-separated signed-integer text. `RIBinary`/`RPBinary`: signed/positive-integer binary,
  MSB first (fastest at `WIDth=2`). `SRIbinary`/`SRPbinary`: same but LSB first (useful for
  little-endian/PC controllers). Changing this also updates the corresponding `WFMPre:ENCdg`,
  `WFMPre:BN_Fmt`, `WFMPre:BYT_Or` values (they're two views of the same state).
- **`DATa:SOUrce <wfm>` / `?`** — Which waveform `CURVe?`/`WFMPre?`/`WAVFrm?` read from.
- **`DATa:STARt <NR1>` / `?`** — First point transferred, **1–2500**.
- **`DATa:STOP <NR1>` / `?`** — Last point transferred, **1–2500**. To always get the full
  waveform, set `START=1` and `STOP=2500`. If `STOP < STARt`, they're swapped internally for
  `CURVe?`.
- **`DATa:WIDth <NR1>` / `?`** — Bytes per point: **1** (8-bit) or **2** (16-bit, LSB always
  zero on send; on receive, incoming data is right-shifted/truncated). Changing this can shift
  `WFMPre:BIT_Nr`, `BYT_Nr`, `YMULt`, `YOFf`, `YZEro`.
- **`WAVFrm?`** *(Query Only)* — Shorthand for `WFMPre?;CURVe?` combined into one query.
- **`WFMPre?`** *(Query Only)* — Full preamble for the `DATa:SOUrce` waveform: `BYT_NR`,
  `BIT_NR`, `ENCDG`, `BN_FMT`, `BYT_OR`, `NR_PT`, `WFID`, `PT_FMT`, `XINCR`, `PT_OFF`, `XZERO`,
  `XUNIT`, `YMULT`, `YZERO`, `YOFF`, `YUNIT` — everything needed to convert raw curve values
  into real voltage/time.
- **`WFMPre:BIT_Nr <NR1>` / `?`** — 8 or 16; tied to `DATa:WIDth * 8`.
- **`WFMPre:BN_Fmt { RI | RP }` / `?`** — Tied to `DATa:ENCdg`.
- **`WFMPre:BYT_Nr <NR1>` / `?`** — 1 or 2; same as `DATa:WIDth`.
- **`WFMPre:BYT_Or { LSB | MSB }` / `?`** — Tied to `DATa:ENCdg`.
- **`WFMPre:ENCdg { ASC | BIN }` / `?`** — Tied to `DATa:ENCdg`.
- **`WFMPre:NR_Pt?`** *(Query Only)* — Points in the *transmitted* record: max 2500 for YT
  data, max 1024 for FFT data, minimum 1.
- **`WFMPre:PT_Fmt { ENV | Y }` / `?`** — `Y`: one value per record point (use
  `Xn = XZEro + XINcr*(n - PT_OFf)` and `Yn = YZEro + YMUlt*(yn - YOFf)`). `ENV`: min/max pairs
  (used automatically for Peak Detect waveforms), up to 1250 pairs, `2*XINcr` between pairs.
- **`WFMPre:PT_Off?`** *(Query Only)* — Always returns `0` (set form is a no-op, kept for
  compatibility). Use `XINcr`/`XUNit`/`XZEro` to actually locate the trigger point.
- **`WFMPre:WFId?`** *(Query Only)* — Human-readable descriptor, e.g.
  `"Ch1, DC coupling, 1.0E0 V/div, 5.0E-4 s/div, 2500 points, Sample mode"`.
- **`WFMPre:XINcr <NR3>` / `?`** — Seconds/point (Hz/point for FFT).
- **`WFMPre:XUNit <QString>` / `?`** — `"s"` or `"Hz"`.
- **`WFMPre:XZEro <NR3>` / `?`** — Time (or Hz) of the first point, relative to trigger.
- **`WFMPre:YMUlt <NR3>` / `?`** — Scale factor, YUnits per digitizer level. Formula:
  `value = ((raw - YOFf) * YMUlt) + YZEro`. **Gotcha:** a query result of exactly `0` on this
  TDS200/TDS1000/TDS2000 family signals *unknown* scaling (e.g. a `CH1+CH2` math waveform where
  CH1 and CH2 have different V/div) — TPS2000 never returns 0 for this.
- **`WFMPre:YOFf <NR3>` / `?`** — Offset in digitizer levels (affects cursor readouts, not the
  actual displayed trace).
- **`WFMPre:YUNit <QString>` / `?`** — `"Volts"`, `"U"` (unknown/divisions), or `"dB"`.
- **`WFMPre:YZEro <NR3>` / `?`** — Zero-reference in YUnits, used in the same conversion
  formula as `YMUlt`/`YOFf`.
- **Per-waveform variants**: every `WFMPre:*` command above (except the plain query-all form)
  also exists as `WFMPre:<wfm>:*`, operating on an explicit `<wfm>` (any `CH<x>`, `MATH`, or
  `REF<x>`) instead of implicitly on `DATa:SOUrce`/`DATa:DESTination`. Set forms raise error
  2241 if `<wfm>` isn't a reference waveform. Functionally identical otherwise — not
  re-documented per-command here.
- **Legacy no-op commands** (accepted for compatibility only, set form ignored, query raises
  events 100 + 420): `WFMPre:XMUlt`, `WFMPre:XOFf`, `WFMPre:ZMUlt`, `WFMPre:ZOFf`,
  `WFMPre:ZUNit`, `WFMPre:ZZEro`. Don't rely on these for anything.

## Worked end-to-end example: capture and transfer a waveform

Adapted directly from the vendor manual's own Chapter 4 example. `>` marks what the controller
sends; unmarked lines are the oscilloscope's replies. This uses the concatenation and
synchronization conventions covered above.

```text
> rem "Check for any pending status messages and clear them."
> *esr?
128
> allev?
:ALLEV 401,"Power on; "

> rem "Reset to a known state, then set only what differs from defaults."
> factory
> ch1:volts 2.0
> hor:main:scale 100e-6
> trig:main:level 2.4

> rem "Start a single-sequence acquisition."
> acquire:stopafter sequence
> acquire:state on

> rem "Wait for the acquisition to finish before reading anything back.
>      Set your controller's read timeout longer than the expected
>      acquisition time before sending this."
> *opc?
1

> rem "Take a built-in measurement on the captured waveform."
> measu:immed:type mean
> measu:immed:value?
:MEASUREMENT:IMMED:VALUE 2.4631931782E0

> rem "Always check *ESR? after a measurement query that might have
>      failed silently -- e.g. requesting a frequency measurement on a
>      DC-like signal with no periodic content."
> measu:immed:type freq
> measu:immed:value?
:MEASUREMENT:IMMED:VALUE 9.9E37
> *esr?
16
> allev?
:ALLEV 2202,"Measurement error, No period found; "

> rem "Pull the raw waveform points for offline analysis."
> data:encdg ascii
> curve?
:CURVE 7,6,5,5,5,6,6,6,8, ... (2500 comma-separated values)

> rem "Pull the preamble needed to convert those raw points into real
>      volts and seconds."
> wfmpre?
:WFMPRE:BYT_NR 1;BIT_NR 8;ENCDG ASC;BN_FMT RP;BYT_OR MSB;NR_PT 2500;
WFID "Ch1, DC coupling, 2.0E0 V/div, 1.0E-4 s/div, 2500 points, Sample
mode";PT_FMT Y;XINCR 4.0E-8;PT_OFF 0;XZERO -5.0E-5;XUNIT "s";
YMULT 7.8125E-2;YZERO 0.0E0;YOFF 127;YUNIT "Volts"
```

To convert the raw comma-separated `CURVe?` values into real units on the controller side, for
each raw point `yn` at index `n`:

```text
voltage = YZERO + YMULT * (yn - YOFF)
time    = XZERO + XINCR * (n - PT_OFF)
```

For a binary transfer instead of ASCII (faster, and the norm for larger records or 16-bit
data), set `DATa:ENCdg RIBinary` (or `RPBinary`/`SRIbinary`/`SRPbinary`) before `CURVe?` and
parse the returned `#<N><count><bytes>` IEEE block instead of a comma-separated string — the
conversion formula above is unchanged either way.

## Common gotchas

- **No acknowledgement for set commands.** Like most Tektronix scopes of this era, a plain set
  command (e.g. `CH1:SCAle 0.5`) produces no reply at all — success or failure is silent on the
  wire. To verify a set command actually took effect or errored, either follow it with the
  matching query, or check `*ESR?`/`ALLEv?` afterward.
- **Synchronizing with long-running operations.** A handful of operations — internal
  self-calibration (`*CAL?`/`CALibrate:*`), single-sequence acquisition
  (`ACQuire:STATE ON`/`RUN` when `ACQuire:STOPAfter SEQuence`), and hard copy (`HARDCopy STARt`)
  — take real time, and the oscilloscope keeps processing other commands while they run. If a
  later command depends on one of these having finished (the classic case: acquire, then
  immediately measure), you need one of four synchronization methods, in increasing
  sophistication and decreasing bus overhead:
  1. **`*WAI`** — simplest to reason about, but blocks the oscilloscope from doing anything
     else and can fill the controller's write buffer if it keeps sending commands.
  2. **Poll `BUSY?`** in a loop — avoids the write-buffer risk, costs more bus traffic.
  3. **`*OPC` + serial poll or SRQ (GPIB only)** — enable the OPC bit via `DESE`/`*ESE`, then
     either serial-poll or wait for a service request; least bus traffic, most setup.
  4. **`*OPC?`** — simplest of all: blocks until it can place a `1` in the output queue, but
     you must set your controller's read/response timeout longer than the operation you're
     waiting on, or you'll time out before it completes.
- **The status/event system has five registers and two queues**, and getting the read order
  wrong silently drops information: reading the **SESR** via `*ESR?` both summarizes pending
  events *and* clears the SESR — you must read `*ESR?` before an event becomes visible to
  `EVENT?`/`EVMsg?`/`ALLEv?`, and any event that arrives *after* an `*ESR?` read isn't visible
  until the *next* `*ESR?` read, even though it's sitting in the Event Queue. The **Output
  Queue** is cleared every time a new command/query arrives — always read a query's response
  before sending the next command, or you lose it (and get a Query Error).
- **`9.9E37` is this instrument's sentinel for "no valid measurement value.**" It shows up from
  `MEASUrement:IMMed:VALue?`, `MEASUrement:MEAS<x>:VALue?`, and several `CURSor:*?` queries
  whenever the underlying source isn't displayed, Trigger View is active, or the display is in
  an incompatible mode (XY, Scan). Always pair a numeric measurement query with an `*ESR?`/
  `ALLEv?` check when the value could plausibly be this sentinel.
- **Concatenation footguns**: forgetting the leading `:` before a differently-rooted command
  (`CH1:COUPling DC;ACQuire:NUMAVg 16` — invalid, missing `:` before `ACQuire`), or putting a
  `:`/`;:`before a `*`-command (`CH1:COUPling DC;:*TRG` — invalid), are both silent-ish
  failures that raise a command-syntax event rather than doing what you probably intended.
  Check `*ESR?` after any concatenated message you're not 100% sure of.
- **RS-232 flow control is mutually exclusive**: hard and soft flagging cannot both be on.
  Enabling one silently disables the other. If you're transferring binary data (a binary
  `CURVe?`, or a `BMP` hard copy over RS232), turn *off* soft flagging first — XON/XOFF bytes
  occurring naturally in binary data will otherwise stall the link.
- **GPIB Device Clear (DCL) does not abort an in-progress hard copy.** You must send
  `HARDCopy ABOrt` first; only then does a DCL correctly clear the output queue.

## What's excluded and why

- **Centronics as a *control* interface** — out of scope. The TDS2CMAX module physically has a
  Centronics port, and `HARDCopy:PORT` can still target it as a hard-copy *output*
  destination (that specific command value is documented above), but no commands can be sent
  *to* the oscilloscope over Centronics — it's a parallel printer port, not a control channel.
  This manual only covers commands entered via GPIB or RS-232, matching what was requested.
- **TDS2MEM-module-only commands** — hardware-absence exclusion, not a scope choice. The
  target setup uses a TDS2CMAX (GPIB + RS-232 + Centronics), not a TDS2MEM (RS-232 +
  Centronics + CompactFlash, no GPIB). Entirely excluded: the whole **File System Commands**
  group (`FILESystem?`, `:CWD`, `:DELEte`, `:DIR?`, `:FORMat`, `:FREESpace?`, `:MKDir`,
  `:REName`, `:RMDir`) — there's no CompactFlash card to manage. Also excluded piecemeal within
  otherwise-in-scope groups: `SAVe:IMAge`, `SAVe:IMAge:FILEFormat`, `RECAll:WAVEform`,
  `HARDCopy:BUTTON`, and the `<file path>`-to-CF-card argument variants of `RECAll:SETUp`,
  `SAVe:SETUp`, and `SAVe:WAVEform` (their memory-location-number variants remain in scope and
  are documented above). If a TDS2MEM module is used instead of TDS2CMAX in the future, GPIB
  drops out of scope entirely and this whole set of file-system commands comes into scope in
  its place.
- **TPS2000-Series-only commands** — hardware-absence exclusion (TDS2024 is not a TPS2000; the
  TPS2000 is a battery-powered isolated-input scope, a genuinely different product). Entirely
  excluded: the **Power and Battery-Related Commands** group (`POWer?`, `AC:PRESENt?`,
  `BATTERY<x>:GASgauge?`, `BATTERY<x>:STATUS?`, `BATTERIES:TIME?`, `BUTTONLIGHT`) and the
  **Power Measurement** group (all `HARmonics:*`, `SWLoss:*`, `POWerANALYSIS:SOUrces`,
  `WAVEFORMANALYSIS:SOUrce` — the last of these also requires the separate TPS2PWR1 license key
  even on a TPS2000). Also excluded piecemeal: `AUTORange?`/`:STATE`/`:SETTings`,
  `CH<x>:CURRENTPRObe`, `CH<x>:YUNit`, `CURSor:VBArs:HDELTa?`/`:HPOS<x>?`/`:SLOPE?`/`:VDELTa?`,
  `DATE`/`TIMe` (also gated on TDS2MEM, doubly excluded), `DISplay:BRIGHTness`,
  `MATH:VERtical:POSition`/`:SCAle` (the top-level, non-FFT math vertical controls — note
  `MATH:FFT:VERtical:*` is *different* and stays in scope), `MEASUrement:IMMed:SOURCE2` and the
  TPS2000-Power-Analysis-only `MEASUrement:IMMed:TYPe` values, and the extended `CH<x>:PRObe`
  values `20`/`50`/`500`.
- **GPIB-specific `EXT10` trigger-source option** — excluded from the Edge/Pulse/Video trigger
  source argument lists above for the same TPS2000-only reason (`EXT10` is a TPS2000-specific
  external-trigger attenuation option; this instrument's external-trigger attenuation choices
  are `EXT`/`EXT5` only).
- **TDS200-series-only syntax variants** — not applicable, opposite direction: where the source
  gives a *different, narrower* command set for the older TDS200 series (e.g. `MATH:DEFINE`'s
  argument list, or the 4-measurement-slot vs. 5-measurement-slot limit), this manual uses the
  TDS1000/TDS2000/TPS2000-series values throughout, since the TDS2024 is a TDS2000-series
  instrument.
- **Chapter 4's Visual C++ / Visual Basic / LabVIEW glue-code listings** were not reproduced
  here. They're generic bus-driver boilerplate (opening a VISA/GPIB session, sending strings)
  around the same commands already documented above, not additional oscilloscope commands —
  reproducing them would duplicate the vendor manual's own Chapter 4 without adding SCPI-level
  information relevant to building a `DevTerm.Devices.Scpi` profile.
- **Appendix A (ASCII code chart) and Appendix B (factory settings table)** were not
  reproduced. Appendix A is the standard 7-bit ASCII table with no device-specific content;
  Appendix B is a long settings-by-settings factory-default listing better consulted directly
  in the vendor PDF if a specific default value is needed than duplicated in full here.
