# Tektronix 2230 — Programming Manual (RS-232-C, Option 12)

100 MHz dual-channel analog/digital-storage oscilloscope, part of Tektronix's 1980s "2200
Family" of DSOs (siblings sharing this same command language include the 2220, 2221, and
others). This manual covers the command set reachable over the **RS-232-C interface
provided by the Option 12 add-on board** — the only remote interface installed in this
configuration.

## Source document

Tektronix distributes a file named **"Tektronix Model 2230 Digital Oscilloscope Programming
Manual"** (`Tektronix_2230_Programming_Manual.pdf`, from `download.tek.com`). However, its
internal running headers throughout read **"Options and Accessories—2230 Operators"** and
every page is numbered in the **7-XX** range — meaning this file is not a standalone
programmer's manual, but **Section 7 ("Options and Accessories") of the Tektronix 2230
Operator's Manual**, packaged and distributed by Tektronix as its own PDF. Both facts are
recorded here because they matter for anyone trying to locate this material in a full paper or
scanned copy of the Operator's Manual: look for Section 7, not a separate volume.

The source PDF is a **scanned document with no embedded text layer** (CCITT Group 4
fax-encoded page images). This manual was written from an OCR pass over that scan.
OCR quality was good for prose and for the well-spaced command tables (7-22 through 7-35),
but **unreliable for two areas**, flagged explicitly at point of use below rather than silently
corrected:
- The RS-232-C DTE/DCE connector pinout tables (7-9, 7-10) — pin-number-to-signal-name
  column alignment was inconsistent across OCR passes.
- The byte-by-byte waveform-transfer example tables (7-17 through 7-21) — these are dense
  numeric tables (byte offset / character / decimal value / GPIB-EOI-flag columns) that OCR'd
  as largely unusable noise. The *prose* description of each encoding format (BINary,
  HEXadecimal, ASCII) immediately preceding those tables OCR'd cleanly and is fully
  reproduced below; only the worked byte-by-byte example tables themselves are unreliable.

## Before you start

### Hardware requirement: this scope needs an add-on option board

**The base 2230 has no remote-control interface at all.** Its only rear/side-panel connector
without any option installed is an AUXILIARY connector for an external clock input and an
analog X-Y plotter output — not a command interface. Remote control requires one of two
**mutually exclusive** add-on option boards:

| Option | Provides | Board part number |
|---|---|---|
| Option 10 | GPIB + memory | 670-8900-00 |
| **Option 12 (this manual)** | **RS-232-C + memory** | 670-8899-00 |

This manual assumes **Option 12 is installed** and documents RS-232-C only. If the
instrument instead has Option 10, the command *language* below still mostly applies (the two
options share the same command dictionary — see "What's excluded and why"), but the
framing, connector, and setup details are different and not covered here.

### Physical connection

The Option 12 side panel provides three things: the AUXILIARY connector (unrelated to
remote command traffic), one RS-232-C port offering **both DTE and DCE connections**, and
a physical **PARAMETERS switch**.

> **NOTE (from the source manual):** Do not hook up external devices to the DTE connector
> and the DCE connector at the same time.

Connector pin functions (Tables 7-9/7-10 in the source) — **OCR confidence is low on the
exact pin-number-to-signal mapping** for these two tables; the signal set itself is a standard
RS-232-C complement:

- Chassis ground, Signal ground
- TXD (transmitted data), RXD (received data)
- RTS (request to send), CTS (clear to send)
- DSR (data set ready), DTR (data terminal ready)
- RLSD (received line signal detect, i.e. carrier detect)

The DCE connector mirrors the DTE connector's TXD/RXD (and RTS/CTS) assignment, as
expected for a DTE/DCE pair. **Before wiring a cable, verify actual pinout against the printed
manual or the physical connector's silkscreen** rather than trusting this OCR'd table for pin
numbers.

Two status LEDs relevant to this interface, above the CRT:
- **ADDR** — on when carrier is detected (also on by default with nothing connected to either
  port).
- **SRQ** — on only while an asynchronous status byte is actively being sent (see "Common
  gotchas" — it is not a persistent "something is pending" indicator for Option 12).
- **PLOT** — on while the option is sending waveform data; acquisitions are inhibited during
  this time.

### The RS-232-C PARAMETERS switch — configuration model is physical, not software

Unlike every other instrument documented in this repo's `docs/devices/`, **Option 12's serial
port parameters are set by a physical 10-section DIP switch, read once at power-up** — not by
a software command sent after connecting. Changing the switch after power-on has no effect
until the next power cycle.

| Switch section(s) | Function |
|---|---|
| 1–4 | Baud rate (4-bit code, see table below) |
| 5 | Parity enable/disable (`0` = disabled, 8-bit data word; `1` = enabled, 7-bit data + 1 parity bit) |
| 6–7 | Parity type when enabled (see table below) |
| 8 | Line terminator: `0` = CR only, `1` = CR-LF |
| 9–10 | Printer/plotter device selection at power-up (can be changed later via commands or, on the 2230 specifically, via the front-panel MENU controls) |

**Baud rate switch table — flagged low-confidence.** The OCR'd values below include several
figures (2000, 3600, 7200) that are not standard RS-232 baud rates and are very likely OCR
misreads of the true values (plausibly 2000→1200 transposition noise, or similar). Treat this
table as a structural guide only (4-bit switch code → some ordered rate) and **verify the actual
rate against a clean copy of the manual or empirically before relying on a specific switch
setting**:

| Switch (4 3 2 1) | Baud rate (as OCR'd — verify) |
|---|---|
| 0000 | 50 |
| 0001 | 75 |
| 0010 | 110 |
| 0011 | 134.5 |
| 0100 | 150 |
| 0101 | 300 |
| 0110 | 600 |
| 0111 | 1200 |
| 1000 | 1800 |
| 1001 | 2000 *(suspect — verify)* |
| 1010 | 2400 |
| 1011 | 3600 *(suspect — verify)* |
| 1100 | 4800 |
| 1101 | 7200 *(suspect — verify)* |
| 1110 | 9600 |
| 1111 | Off Line (instrument presents an active electrical load but sends/receives no traffic) |

Parity type (switch sections 6–7, only meaningful if section 5 enables parity):

| Switch (7 6) | Parity |
|---|---|
| 0 0 | ODD |
| 1 0 | EVEN |
| 0 1 | MARK (parity bit always 1) |
| 1 1 | SPACE (parity bit always 0) |

Printer/plotter selection (switch sections 9–10 — relevant only if you're using the analog
plotter output via `PLOt` commands, not for general command/query control):

| Switch 9 | Switch 10 | Device |
|---|---|---|
| 0 | 0 | HP-GL plotter |
| 1 | 0 | Epson (EPS7 or EPS8) |
| 0 | 1 | HP ThinkJet printer |
| 1 | 1 | X-Y Plotter |

Two further serial parameters are **not** set by the switch — they're set by software command
after the link is already up: **stop bits** (`STOP 1` or `STOP 2`, default `1`) and **flow control**
(`FLOw ON`/`OFF` — see "Common gotchas" for a real contradiction in the source about its
power-on default).

## Command syntax conventions

- **Case-insensitive, minimum-unique abbreviation.** Every command/query header has a
  required minimum set of characters, shown in upper case throughout the source tables and
  reproduced that way below (e.g. `VMOde?`); any additional lower-case letters shown may
  optionally be typed in full. `VMO?`, `VMOd?`, and `VMOde?` are all equivalent. This applies to
  both upper- and lower-case typed input — the oscilloscope accepts either case for input,
  but always replies in upper case.
- **`LONg ON` / `LONg OFF`** (default `ON` at power-on) controls how verbose *query replies*
  are: with `LONg OFF`, replies use only the short (all-caps minimum) form of each keyword;
  with `LONg ON`, replies spell out the full keyword. This does not affect what you may type as
  a command — only what comes back in a reply.
- **Headers and arguments.** A command consists of at least a header (e.g. `INIt`, `OPC`).
  Many commands need one or more arguments, separated from the header by a space.
  A second ("link") argument, when a command's argument itself takes a sub-argument, is
  separated from the first by a colon — e.g. `ACQuisition REPetitive:SAMple`,
  `WFMpre XINcr:1.0E-3`. Multiple independent arguments (or argument pairs) are
  comma-separated — e.g. `DATa ENCdg:BINary,CHAnnel:CH2`.
- **Numeric argument formats** (Table 7-16 in the source):

  | Argument | Format | Examples |
  |---|---|---|
  | `<NR1>` | Integer | `+1`, `2`, `-1`, `-10` |
  | `<NR2>` | Explicit decimal point | `-3.2`, `+5.1`, `1.2` |
  | `<NR3>` | Floating point, scientific notation | `+1.E-2`, `1.0E+2`, `0.02E+3` |

  Both signed and unsigned numbers are accepted; unsigned numbers are taken as positive.
- **Default arguments** are shown in square brackets in the command tables (e.g.
  `ACQuisition HSRec:[SAMple]`) — omitting that argument selects the bracketed default.
  `{choice1|choice2}`-style angle-bracket groups (e.g. `<AC, DC, or GND>`) list the valid
  discrete values for a link argument.
- **Command separator (`;`) — usable but not recommended for Option 12.** Multiple
  commands may be chained on one line with `;` (e.g.
  `DATa ENCdg:BINary,CHAnnel:CH2;WFMpre XINcr:1.0E-3`), and the waveform-preamble
  workflow is one legitimate use of this. **However, the source explicitly discourages
  multi-command lines for RS-232-C controllers**: the instrument acts on each command as
  soon as it recognizes the separator, without waiting for the line's terminator — if an earlier
  command in the line triggers an asynchronous status-byte response for any reason (error or
  otherwise), a controller that isn't ready to handle it mid-line can lose synchronization. **The
  documented recommended practice for RS-232-C is one command per message line.**
  (Contrast: the source notes GPIB/Option-10 controllers routinely use multi-command lines —
  that's out of scope here.)
- **Message terminator.** Configured by PARAMETERS switch section 8: either CR alone, or
  CR-LF. When CR-LF is selected, the instrument accepts either CR-LF or a bare LF as an
  incoming terminator, and always sends CR-LF at the end of every outgoing message. The
  instrument does **not** wait for the terminator to act on a command — it recognizes the `;`
  command separator (or, for a single-command line, simply parses as far as it can) and starts
  responding immediately. Don't double-terminate a single-command message; a second
  terminator can be interpreted as (and swallow) the start of a response.
- **Query-only vs. set-and-query.** Commands ending in `?` in the tables below are query-only.
  A header without `?` is a set command; most set-capable headers also support a `?` query
  form to read back the current value (shown as a separate table row where the source lists
  it that way).

## Command reference

Each functional group below corresponds to one table in the source (Tables 7-22 through
7-33). All commands shown are tagged in the source as valid for **ALL** 2200-Family models
or explicitly for the **2230** — commands tagged only for sibling models (2220-only,
2221-only, or "2220 and 2221" without 2230) have been excluded; see "What's excluded and
why" for the one place this filtering actually removed anything.

### Vertical Commands (Table 7-22)

- **`CH1?`** — Query only. Returns present CH1 settings: `CH1 VOL:<NR3>,COU:<AC, DC, or
  GND>`. `<NR3>` is the VOLTS/DIV setting.
- **`CH1? VOLts`** — Query only. Returns the CH1 VOLTS/DIV setting including probe
  attenuation, e.g. `CH1 VOL:5.0E-2` for a 50 mV/div setting. Generates an execution warning
  if the VOLTS/DIV variable (CAL) knob isn't in its detent position.
- **`CH1? COUpling`** — Query only. Returns `COU:<AC, GND, or DC>`.
- **`CH2?` / `CH2? VOLts` / `CH2? COUpling`** — Same as the CH1 forms, for channel 2.
- **`CH2? INVert`** — Query only. Returns `CH2 INV:<ON or OFF>`.
- **`VMOde?`** — Query only. Returns the vertical mode: `VMO:<CH1, CH2, ADD, CHOp, ALT,
  or XY>`.
- **`PROBe? <CH1 or CH2>`** — Query only. Returns probe attenuation coding: `CH<1 or 2>
  PROB:<NR1>`, where `<NR1>` is `1000`, `100`, `10`, `1`, `-1` (identify), or `-2` (unknown
  coding).

### Horizontal Commands (Table 7-23)

- **`DELAy?`** — Query only. Returns `DELA VAL:<NR3>,UNI:<S or DIV>`.
- **`DELAy? VALue`** — Query only. Returns `DELA VAL:<NR3>` in the units given by
  `DELAy? UNits`.
- **`DELAy? UNits`** — Query only. Returns `DELA UNI:<S or DIV>`; units are `DIV` when the
  SEC/DIV knob is at EXT CLK.
- **`HORizontal?`** — Query only. Returns all present horizontal settings.
- **`HORizontal? ASEdiv`** — Query only. Returns `HOR ASE:<NR3>` (A SEC/DIV setting; `0`
  when SEC/DIV is at EXT CLK).
- **`HORizontal? BSEdiv`** — Query only, 2230. Returns `HOR BSE:<NR3>` (B SEC/DIV
  setting).
- **`HORizontal? EXTclk`** — Query only. Returns `HOR EXT:<ON or OFF>`.
- **`HORizontal? HMAg`** — Query only. Returns `HOR HMA:<ON or OFF>` (×10 magnifier
  state).
- **`HORizontal? MODe`** — Query only, 2230. Returns `HOR MOD:<ASW, AIN, or BSW>`.

### Trigger Commands (Table 7-24)

- **`ATRigger? [MODe]`** — Query only. Returns `ATR MOD:<NOR, PPA, or SGL>`. `PPA`
  covers both Peak-to-Peak Auto and TV Field trigger modes; the reply is identical with or
  without the optional `MODe` argument.
- **`SGLswp ARM`** — Command only. Re-arms a completed single sweep. Execution error if
  not in SGL SWP mode; execution warning if already armed. With `OPC ON`, generates an
  operation-complete status byte when the rearmed sweep occurs.
- **`SGLswp?`** — Query only. Returns `SGL <ARM or DON>` when SGL SWP mode is active;
  otherwise returns `SGL` (bare) plus an execution warning.
- **`TRiggerd?`** — Query only. Returns `TRI <ON or OFF>` — state of the TRIG'D indicator.

### Cursor Commands (Table 7-25)

- **`CURSor CHAnnel:<CH1-CH2>`** — Selects which channel's cursor voltage difference
  `DELTAV?` reports. No warning if directed to an undisplayed channel.
- **`CURSor POSition:<NR1>`** — Sets the active cursor's horizontal data-point position. On a
  1 Kb record, a request past point 1023 is silently clamped to 1023 (no warning). On a 4 Kb
  record, a request past point 4095 generates a command-error service request and is
  ignored outright (different failure behavior depending on record length — a real gotcha).
- **`CURSor SELect:<CURS1-CURS2>`** — Selects which cursor `CURSor POSition` moves.
- **`CURSor TARget:ACQuisition`** — Attaches the displayed cursors to the live acquisition.
- **`CURSor TARget:<REF1-REF3>`** — 2230 only. Attaches cursors to the named reference
  waveform; ignored (no warning) if that reference isn't displayed.
- **`CURSor TARget:REF4`** — Attaches cursors to REF4; ignored if REF4 isn't displayed, but
  generates an execution-error service request if REF4 is empty (different failure behavior
  than the REF1-3 form above — another real gotcha).
- **`CURSor?`** — Query only. Returns all cursor argument states:
  `CURS SEL:CH1,TAR:ACQ,CHA:CH1,POS:1047`. Each argument may also be queried
  individually, e.g. `CURSOR? TAR`.
- **`DELTAV?` / `DELTAV? VALue` / `DELTAV? UNits`** — Query only. Full form returns
  `DELTAV VAL:<NR3>,UNI:<VOL or PER>`; `PERcent` units are returned instead of volts when
  the VOLT/DIV variable knob is out of its CAL detent.
- **`DELTAT?` / `DELTAT? VALue` / `DELTAT? UNits`** — Query only. Full form returns
  `DELTAT VAL:<NR3>,UNI:<SEC or DIV>`; units are divisions when SEC/DIV is at EXT CLK.

### Display Commands (Table 7-26)

- **`MESsage <NR1>:"message"`** — Writes text on display row `<NR1>` (16 = top, 1 =
  bottom). `MES [0]` (the `0` may be omitted) turns the message off and restores normal
  readouts. Messages over ~40 characters run off the CRT edge and are truncated, generating
  a service request if `RQS` is on. Changing a front-panel control that needs its own readout
  overrides and clears the message. Many message lines can cause display flicker or exceed
  display memory.
- **`PLOt ABOrt`** — Command only. Stops an in-progress plot and returns to the prior mode;
  this is the *only* command/query the instrument responds to while plotting. Also clears
  `PLOt AUTo`.
- **`PLOt AUTo:<ON or OFF>`** — When `ON`, every acquired waveform is auto-plotted (and
  the graticule too, if `PLOt GRAt` is `ON`).
- **`PLOt FORmat:<[XY], HPGl, EPS7, EPS8, or TJEt>`** — Output format for the selected
  printer/plotter (see the PARAMETERS switch table above for how the device itself is
  selected). Defaults to `XY` format if no specific device is selected.
- **`PLOt GRAt:<ON or OFF>`** — Include the graticule in plots.
- **`PLOt SPEed:<NR1>`** — Integer 1–10, analog plotter pen speed (roughly divisions/second).
- **`PLOt STArt`** — Command only. Starts a plot using the current `PLOt FORmat`/`GRAt`/
  `SPEed` settings. All commands except `PLOt ABOrt` are ignored while plotting.

### Acquisition Commands (Table 7-27)

- **`ACQuisition CURRent:<AVErage, [DEFault], PEAkdet, or SAMple>`** — Sets the mode for
  the current acquisition type/SEC-DIV combination; omitting the argument selects that
  combination's default. Generates a service request if the requested mode is invalid for the
  current type/speed.
- **`ACQuisition CURRent:ACCpeak`** — Selects accumulate-peak mode for the current
  acquisition type/speed.
- **`ACQuisition HSRec:<ACCpeak or AVErage>`** — High-speed-record mode selection for
  5 µs/div and 10 µs/div sweep speeds.
- **`ACQuisition HSRec:[SAMple]`** — Selects sample mode for 5–10 µs/div acquisitions; this
  is the default if the argument is omitted.
- **`ACQuisition LSRec:<ACCpeak or AVErage>`** — Low-speed-record mode for 0.02 ms/div
  to 50 ms/div.
- **`ACQuisition LSRec:<[PEAkdet] or SAMple>`** — Same speed range; `PEAkdet` is the
  default if omitted.
- **`ACQuisition NUMsweeps:<NR3>`** — Number of sweeps before halting; `0` = continuous.
- **`ACQuisition REPetitive:<ACCpeak or SAMple>`** — Repetitive-acquisition mode for
  0.05 µs/div–2 µs/div.
- **`ACQuisition REPetitive:[AVErage]`** — Same speed range; `AVErage` is the default if
  omitted.
- **`ACQuisition RESet`** — Command only. Resets sampling at all SEC/DIV settings to their
  bracketed defaults.
- **`ACQuisition ROLl:<[PEAkdet] or SAMple>`** — Mode for untriggered ROLL acquisitions,
  0.1–5 sec/div.
- **`ACQuisition SCAn:<[PEAkdet] or SAMple>`** / **`ACQuisition SCAn:<ACCpeak or
  AVErage>`** — Mode for SCAN acquisitions, 0.1–5 sec/div (the accumulate/average form
  requires NORM or SGL SWP trigger mode to see the change reflected in the readout).
- **`ACQuisition SMOoth:<ON or OFF>`** — Applies smoothing to acquired data.
- **`ACQuisition TRIGCount:<NR1>`** *(2230 variant only — see below)* — Sets pre-trigger
  point count. On the 2230, the valid `<NR1>` range depends on both record length and
  pre-/post-trigger selection: pretrigger `4`–`512` (1 K records) or `16`–`2048` (4 K records);
  post-trigger `512`–`1020` (1 K) or `2048`–`4080` (4 K); resolution ±4 counts.
- **`ACQuisition VECtors:<ON or OFF>`** — Point-to-point display vectors on/off.
- **`ACQuisition WElght:<NR1>`** — Number of acquisitions weighted into an averaged
  record. Valid values: `1, 2, 4, 8, 16, 32, 64, 128, 256`; any other value generates a service
  request and is ignored. Reverts to `4` if the argument is omitted.
- **`ACQuisition?`** — Query only. Returns the full short-form acquisition state, e.g.
  `ACQ REP:AVE,HSR:SAM,LSR:PEA,SCA:PEA,ROL:PEA,SMO:ON,WEI:4,SWP:1037,NUM:0,
  PO1:4096,TRIGM:POST,TRIGC:2000,SAV:OFF,DIS:SCA,VEC:ON`. Every argument except
  `RESet` may also be queried individually.
- **`ACQuisition? DiSplay`** — Query only. Returns `ACQ DIS:<ROL or SCA>`.
- **`ACQuisition? POInts`** — Query only. Returns `ACQ POI:<NR1>` — point count in the
  waveform record.
- **`ACQuisition? SAVE`** — Query only. Returns `ON` (SAVE) or `OFF` (CONTINUE).
- **`ACQuisition? SWPcount`** — Query only. Returns `ACQ SWP:<NR1>` — sweeps completed.
- **`ACQuisition? TRIGMode`** — Query only. Returns `ACQ TRIGM:<PRE or POST>`.
- **`STORe?`** — Query only. Returns `STOR <ON or OFF>` — STORE/NON-STORE button state.

### Save and Recall Reference Commands (Table 7-28)

- **`REFFrom [ACQ]`** — Selects the live acquisition as the data source for the next `SAVeref`.
  Default if omitted.
- **`REFFrom REF<1-4>`** — 2230 only. Selects a numbered reference as the `SAVeref` data
  source (acquisitions must first land in a numbered reference before being savable into a
  lettered one).
- **`REFFrom REF<A-Z>`** — 2230 only. Selects an extended (nonvolatile) reference as the
  `SAVeref` source. Total extended memory is 26 Kbytes; stored records run 1 K–8 K (4 K
  averaged acquisitions).
- **`REFDisp REF<1-3>:<ON, OFF, or EMPTY>`** — 2230 only. REF1–REF3 are 1024-point
  memories. `EMPTY` erases and turns off the named reference.
- **`REFDisp REF4:<ON, OFF, or EMPTY>`** — REF4 is a 4096-point memory; on the 2230 it
  occupies the same physical space as REF1–REF3 combined.
- **`REFDisp REF<A-Z>:EMPTY`** — 2230 only. Erases the named lettered reference if not
  write-protected (see `REFProt`). Lettered references can't be displayed directly — move to a
  numbered reference first.
- **`REFProt REF<A-Z>:<LOCked, PERM, or UNLocked>`** — 2230 only. Controls write
  protection on nonvolatile references. `LOCked`/`PERM` block further writes/erasure; `PERM`
  additionally can't be overwritten from the front panel either.
- **`REFOrmat CHAnnel:<[CH1] or CH2>`** — 2230 only. Selects which channel of a saved
  reference to reformat. `CH1` is the default; either channel may be selected for an XY
  waveform.
- **`REFOrmat HMAg:<ON or OFF>`** — 2230 only. ×10 horizontal magnification of the
  reformat target waveform set.
- **`REFOrmat VGAin:<NR3>`** — 2230 only. Adjusts vertical gain of the reformat
  target/channel. Not valid for XY waveforms. Max change is ±3 detent positions (1-2-5
  sequence) of the VOLT/DIV switch; out-of-range or non-1-2-5 values generate an execution
  error.
- **`REFOrmat VPOsition:<NR2>`** — 2230 only. Adjusts vertical position of the reformat
  target, ±10 divisions from original position, 1-bit resolution.
- **`REFDisp?`** — Query only. Returns REF1 status (`ON`/`OFF`/`EMPTY`) on the 2230, REF4
  status on 2220/2221.
- **`REFDisp? REF<1-3>`** — 2230 only. Query only.
- **`REFDisp? REF4`** — Query only.
- **`REFFrom?`** — Query only. Returns the current `SAVeref` data source.
- **`REFOrmat?`** — 2230 only. Query only. Returns e.g.
  `REFO TAR:REF4,CHA:CH2,VGA:0.5E+0,VPO:+3.96,HMA:OFF,BAS:0.2E+0,MOD:CH1`.
- **`REFOrmat? BASegain`** — 2230 only. Query only. Returns the vertical gain the reformat
  target was originally acquired at.
- **`REFOrmat? MODe`** — 2230 only. Query only. Returns the vertical mode the reformat
  target was acquired in.
- **`REFStat? FILl`** — 2230 only. Query only. Returns a 30-character fill-status string across
  reference memories REF1–REFZ (`0`=empty, `1`/`2`/`4`/`8` = stored Kbytes).
- **`REFStat? FREe`** — 2230 only. Query only. Returns free Kbytes (0–26) in nonvolatile
  reference memory.
- **`REFStat? PROTect`** — 2230 only. Query only. Returns a 30-character protection-status
  string (`U`/`L`/`P` per reference).
- **`SAVeref REF<1-3>`** — 2230 only. Command only. Saves the `REFFrom`-selected
  waveform into a 1024-point reference; any 1 K window of a 4 K acquisition may be saved
  this way, positioned by the active cursor.
- **`SAVeref REF4`** — Command only. The only reference memory on 2220/2221 (argument
  may be omitted there); a 4096-point memory on the 2230.
- **`SAVeref REF<A-Z>`** — 2230 only. Command only. Saves into a lettered (nonvolatile)
  reference. 4 K records saved as such can't later be moved into REF1–REF3 — they must go
  through REF4 to be displayed or transmitted.

### Waveform Commands (Table 7-29)

- **`CURVe`** — Used as a command to send waveform data to the instrument (destination set
  by `DATa TARget`/`DATa CHAnnel`, format by `DATa ENCdg`), or as a query
  (`CURVe?`, implied) to retrieve it (source set by `DATa SOUrce`/`DATa CHAnnel`). Wire
  format: `CURVE <data>;`, where `<data>` is `%<byte count><binary data><checksum>` for
  binary, `#H<byte count><hex data><checksum>` for hex, or comma-separated ASCII values
  for ASCII encoding.
- **`DATa CHAnnel:<[CH1] or CH2>`** — Selects the channel that `CURVe?`/`WAVfrm?`/
  `WFMpre?` read from, and the target channel for incoming waveform data. Querying a
  channel with no waveform present generates a service request. Power-on default `CH1`;
  `CH1` is required for an XY acquisition.
- **`DATa ENCdg:<ASCii, [BINary], or HEX>`** — Curve data encode/decode format. Power-on
  default `BINary`. Data points are unsigned integers in every format.
- **`DATa SOUrce:<REF1, REF2, or REF3>`** — 2230 only. Selects a numbered reference as
  the source for `WAV?`/`WFM?`/`CURV?`.
- **`DATa SOUrce:<[ACQ] or REF4>`** — Selects the live acquisition (default) or REF4 as the
  waveform-query source. A saved 4 K record is retrieved via `REF4`.
- **`DATa TARget:<REF1, REF2, or REF3>`** — 2230 only. Selects the reference that a
  `CURve`/`WFMpre` *command* (i.e. incoming data) writes into. At power-on, `REF1` is
  selected; there is no bracketed default shown for this form.
- **`DATa TARget:REF4`** — Selects REF4 as the write target. Only choice on 2220/2221; on
  the 2230, `REF4` must be explicitly selected to transfer a full 4 K waveform in.
- **`DATa?`** — Query only. Returns e.g. `DAT SOU:ACQ,TAR:REF1,CHA:CH1,ENC:BIN`. Each
  argument may also be queried individually.
- **`WAVfrm?`** — Query only. Returns the combined preamble + curve data:
  `WFM <ascii preamble>;CURV <waveform data>;`.

### Waveform Preamble Fields (Table 7-30)

> **Source note, reproduced verbatim in spirit:** these fields exist primarily to help interpret a
> `WFMpre?` reply. If sent individually as commands, a value isn't actually applied until the
> matching curve data is transferred to the selected `DATa TARget`; a bad numeric value is
> *accepted* at the time it's set, then rejected (with a waveform-preamble-error service
> request) only once the curve data itself is sent.

- **`WFMpre ENCdg:<ASCii, [BINary], or HEX>`** — Same effect/semantics as `DATa ENCdg`
  (they operate identically).
- **`WFMpre?`** — Query only. Returns the full preamble, e.g.:
  `WFM WFI:"ACQ,CH1,0.2mV,DC,0.5mS,AVERAGE,CRV# 3";NR.P:2048,PT.O:256,
  PT.F:ENV,XMU:1.0E+3,XOF:0,XUN:S,XIN:10.0E-6,YMU:8.0E-3,YOF:0,YUN:V,ENC:ASC,
  BN.F:RP,BYT:1,BIT:8,CRV:CHK;`. Each field may be queried individually.
- **`WFMpre? WFld`** — Query only. Returns an ID string:
  `WFM WFI:"ACQ,CH1,0.2mV,DC,0.5mS,AVERAGE,CRV# 3";` — source, channel, V/div,
  coupling, time/div, acquisition mode, curve number (plus CH2 V/div+coupling for XY mode).
  Ignored if sent as a command. All vertical info is omitted for a 2220.
- **`WFMpre NR.Pts:<NR1>`** — Number of points (`256`/`512`/`1024`/`2048`/`4096`,
  depending on channel count, acquisition mode, and smoothing). A record-length-to-NR.Pts
  ratio table exists in the source (single channel SAMple/AVErage/PEAkdet-with-smoothing =
  full record; 2-channel or peak-detect-without-smoothing divides the record by 2 or 4).
- **`WFMpre PT.Off:<NR1>`** — Trigger position relative to the record's first point. `4`–`1024`
  (1 K record) or `4`–`4096` (4 K record) in steps of 4. Can be negative if the trigger occurred
  before the record window; legal range is `-3096` to `+4096`; `-10000` is returned if unknown.
- **`WFMpre PT.Fmt:<Y, XY, or ENV>`** — `Y`: amplitude only, X implied from the preamble.
  `XY`: explicit X-Y pairs, X first. `ENV`: max-min pairs sent as `y1max,y1min,y2max,y2min,...`
  but *displayed* in the reverse order (`y1min,y1max,...`). `ENV` is valid only for
  `PEAkdet`/`ACCpeak` acquisition with smoothing `OFF`.
- **`WFMpre XUNits:<S or CLKs>`** — Units for `XINcr`. `CLKs` (and an unknown `XINcr`) occur
  when SEC/DIV is at EXT CLK.
- **`WFMpre XINcr:<NR3>`** — Time between data points. A value inconsistent with a legal
  SEC/DIV setting is rejected when the matching curve data arrives (command-argument-error
  service request). Reads back as `1` (`0.1E+0`) when genuinely unknown (EXT CLK).
- **`WFMpre YUNits:<V or DIVs>`** — `DIVs` is returned when the channel's VOLTS/DIV CAL
  knob is out of detent (scaling unknown), and always for the 2220.
- **`WFMpre YMUlt:<NR3>`** — Digitizer step size (volts between levels). Rejected at curve-
  transfer time if inconsistent with a legal VOLTS/DIV setting. Reads back `40.0E-3` when the
  source channel's CAL knob is out of detent.
- **`WFMpre YOFf:<NR1>`** — Y-coordinate of ground level; `-10000` if unknown.
- **`WFMpre XMUlt` / `WFMpre XOFf`** — Analogous to `YMUlt`/`YOFf`, added to the preamble
  for XY waveforms; `YUNits` applies to both X and Y in that case, and `XUNits` is referenced
  to sampling rate.
- **`WFMpre BN.Fmt:RP`** — `RP` (right-justified, unsigned/positive binary) is the only valid
  value.
- **`WFMpre BYT/nr:<NR1>`** — `1` or `2` bytes per data point (`2` for averaged data). If 2, the
  most-significant byte is sent first.
- **`WFMpre BIT/nr:<NR1>`** — `8` or `16` bits per data point. Note: the least-significant bits
  of a 16-bit point may or may not be meaningful, depending on how many acquisitions were
  averaged.
- **`WFMpre CRVchk:CHKsm0`** — Indicates the last byte of a binary curve is a checksum: the
  two's-complement of the modulo-256 sum of the binary-count bytes plus curve-data bytes
  (not including the `CURVE %` header itself).

### Miscellaneous Commands (Table 7-31)

- **`INIt`** — Command only. Reverts acquisition-mode settings to power-on defaults (see the
  full default list under "Reset Under Communication Option Control" in Common Gotchas)
  and re-initializes the 2230 menu system. Does not invoke the power-up self-test; generates
  no status byte or event code on completion.
- **`LONg <[ON] or OFF>`** — Query-reply verbosity, see "Command syntax conventions."
  Power-on default `ON`.
- **`ID?`** — Query only. Returns e.g. `ID TEK/2230,V81.1,VERS:09;` — instrument type and
  firmware version.
- **`HELp?`** — Query only. Returns every valid command header supported by the instrument,
  in short form (as under `LONg OFF`).
- **`SET?`** — Query only. Returns an ASCII string of every settable-via-interface control's
  current state; this exact string can be sent back as a command message to restore that
  state. Query-only settings aren't included. Per Tektronix Codes-and-Formats convention, no
  header is sent back with the settings string itself. Reply length varies with `LONg` state.

### Service Request Group Commands (Table 7-32)

- **`OPC <[ON] or OFF>`** — Power-on default `OFF`. When `ON` (and `RQS` also `ON`),
  generates a service request on completion of certain system events (acquisition complete,
  plot complete).
- **`RQS <[ON] or OFF>`** — Power-on default `ON`. When `ON`, the instrument proactively
  sends a status byte for any event to report. When `OFF`, events still accumulate and are
  retrievable via `EVEnt?`, but `STAtus?` always replies with a "no status" (`0`) code — see
  Common Gotchas for what this means for an Option-12 polling loop.
- **`EVEnt?`** — Query only. Returns the oldest pending event's `<NR1>` code (or `0` if none
  pending). Querying clears that event; repeat until `0` to drain the queue when multiple
  events of different priority are pending.

### RS-232-C Specific Commands (Table 7-33)

This group exists specifically for Option 12 and is unambiguously in scope.

- **`FLOw <[ON] or OFF>`** — DC1/DC3 (XON/XOFF) software flow control. See Common
  Gotchas — **the source contradicts itself on the power-on default**. With flow control on,
  `<control-S>` suspends output, `<control-Q>` resumes it, and `<control-D>` aborts the
  current command/query, clears both buffers, and resets the message processor.
  **Binary-encoded data transfers cannot be made with `FLOw ON`** — turn it off first if it was
  previously enabled.
- **`REMote <[ON] or OFF>`** — Enables/disables setting remote-controllable instrument
  state. Sending a control (state-changing) command while `REMote OFF` generates an
  execution-error service request. See Common Gotchas for a similar default-value
  discrepancy to `FLOw`.
- **`STOP <1 or 2>`** — Number of stop bits. Default/most-common value `1`; some printers
  need `2` at certain baud rates. (2220/2221 note from the source: stop-bit selection isn't
  available from their front panel at all — pick a baud rate needing only 1 stop bit when
  driving a printer/plotter on those models. Not applicable to the 2230, included here only
  because it's directly relevant context for shared cabling/peripheral setups.)
- **`STAtus?`** — Query only. Returns the instrument's current status byte (or a "no status"
  indication if nothing is pending). If `RQS` is off, use `EVEnt?` instead to learn whether/what
  happened — it carries more diagnostic detail than the raw status byte.

## Worked end-to-end example: capture and transfer a waveform (RS-232-C)

Assumes the PARAMETERS switch is already set (baud/parity/terminator) and the link is
physically connected via the DTE or DCE port, one command per line per the RS-232-C
convention above.

```text
ID?
  -> ID TEK/2230,V81.1,VERS:09;      (confirm you're talking to a 2230)

REMote ON                             (allow remote state changes)

DATa ENCdg:BINary
DATa CHAnnel:CH1
DATa SOUrce:ACQ                       (default; shown explicitly for clarity)
DATa TARget:REF1                      (only matters if you'll also send data in)

FLOw OFF                              (binary transfers cannot use FLOw ON)

WFMpre?
  -> WFM WFI:"ACQ,CH1,0.5V,DC,0.2mS,SAMPLE,CRV# 1";NR.P:4096,PT.O:122,PT.F:Y,
     XMU:0.0E0,XOF:0,XUN:S,XIN:2.0E-6,YMU:20.0E-3,YOF:-20,YUN:V,ENC:HEX,
     BN.F:RP,BYT:1,BIT:8,CRV:CHK;
  (parse NR.Pts, XINcr, YMUlt, YOFf here — you need these to convert raw
   curve values into real time/voltage)

CURVe?
  -> CURVE %<2-byte length><raw binary curve bytes><1-byte checksum>
  (validate: recompute the modulo-256 checksum over the length bytes + data
   bytes and compare to the trailing checksum byte before trusting the data)

STAtus?
  -> (check for a pending error before treating the transfer as clean; if
      RQS was left ON, an error mid-transfer would already have arrived as
      an asynchronous status byte instead)
```

To convert a raw curve value to a real voltage: `voltage = (raw_value - YOFf) * YMUlt`
(using the units from `YUNits`), and to a real time-since-trigger for point index `n`:
`time = (n - PT.Off) * XINcr` (using the units from `XUNits`). This is the standard
Tektronix waveform-preamble scaling model reflected in the field descriptions above.

## Common gotchas

- **Two genuine contradictions in the source document — flagged, not silently resolved:**
  - **`FLOw` power-on default.** The prose under "FLOW Control" (page 7 of the source
    section) states plainly: *"the power-on setting of FLOW is set to off"* — explicitly because
    flow control can't coexist with binary-encoded data, which is itself the power-on default
    encoding. But the `FLOw` entry in Table 7-33 (RS-232-C Specific Commands) states just as
    plainly: *"FLOW ON is the default and power-on state."* These cannot both be true. Don't
    assume either — **query `FLOw?` right after establishing the link** before trusting its state,
    and always send `FLOw OFF` explicitly before a binary transfer regardless of what you
    assume the default is.
  - **`REMote` power-on default.** Prose under "Remote-Local Operating States" states *"the
    instrument powers up with REMote OFF."* The Table 7-33 entry shows the default-argument
    bracket on `ON` (`REMote <[ON] or OFF>`), which by this manual's own bracket convention
    would mean `ON` is the default. Likely an OCR artifact misplacing the bracket rather than a
    real second contradiction, but unverified — **query `REM?` before sending any
    state-changing command**, and don't assume you're already in remote state.
- **RS-232 has no SRQ hardware line.** Unlike GPIB/Option 10, where a hardware SRQ line
  backs a genuine interrupt, Option 12's `SRQ` panel LED just indicates "a status byte is
  currently being transmitted" — it's not a level you poll externally. Detecting an event under
  RS-232-C is inherently software-polling: send `EVEnt?` (or `STAtus?`) and inspect the reply.
  With `RQS OFF`, `STAtus?` always reports "no status" — you must use `EVEnt?` instead in
  that mode. With `RQS ON`, the instrument *does* proactively send an unsolicited status byte
  when something happens, but your controller still has to be reading the serial port when it
  arrives rather than waiting on an interrupt line.
- **One command per line, strictly, for RS-232.** Re-stated here because it's the single
  biggest structural difference from how you'd script this instrument over GPIB: the
  instrument doesn't buffer/stack incoming or outgoing messages. Sending two commands on
  one line risks losing the first command's response when the output buffer is reinitialized
  for the second command's response.
- **`CURSor` and `SAVeref` range-violation behavior is inconsistent by design, not by
  accident** — some out-of-range requests are silently clamped (e.g. cursor position past the
  end of a 1 K record), others generate a hard command-error and are ignored outright (same
  request against a 4 K record). Don't assume uniform "clamp" or uniform "reject" behavior
  across this command family — check each command's own description.
- **`WFMpre` fields set individually aren't validated until curve data actually arrives.** A bad
  `XINcr`/`YMUlt` value is accepted when you set it, then only rejected (with a
  waveform-preamble-error service request) once you send the matching `CURVe` data. If
  you're setting preamble fields individually rather than all at once via a captured
  `WFMpre?` string, don't assume a lack of immediate error means the value was valid.
- **`INIt` resets a specific, documented set of acquisition/plot/data defaults** (repetitive
  average mode, high-speed sample, low-speed peak-detect, scan peak-detect, roll peak-
  detect, smoothing on, weight 4 — 16 on 2220/2221, 0 sweeps, vectors on, binary encoding,
  ACQ data source, REF1 data target — REF4 on 2220/2221, plot graticule off, readout on, plus
  a menu-system reset) — it is not a full instrument reset and does not invoke self-test.

## What's excluded and why

- **GPIB / Option 10** — mutually exclusive alternate hardware option to the Option 12 this
  manual covers; the user's instrument has Option 12 installed. Where the source draws an
  explicit contrast (GPIB's EOI-based message termination and multi-argument-per-query
  convenience, vs. RS-232-C's terminator-agnostic parsing and one-command-per-line
  discipline), only the Option 12 / RS-232-C behavior is documented above as actionable
  guidance; the GPIB side is mentioned only where needed for contrast. The `STATUS BYTES
  AND EVENT CODES` section's "Option 10" subsection (SRQ-reassertion-until-all-priorities-
  reported behavior) was likewise excluded in favor of the "Option 12" subsection's polling-
  based behavior, which is what's documented under Common Gotchas above.
- **Sibling-model-only command variants** — the vast majority of commands in Tables 7-22
  through 7-33 are tagged `ALL` or explicitly include `2230`, and are included above. Exactly
  one command had model-specific variants that required filtering: **`ACQuisition
  TRIGCount`**, which has three separately-tagged forms (`2220`, `2221`, `2230`) with different
  valid-range rules per model. Only the `2230` form is documented above; the `2220`-only and
  `2221`-only forms were excluded as not applicable to this instrument.
- **Tables 7-14/7-15 (CRT-readout character-set translation table, standard ASCII chart)** —
  low-value appendix material (a character-code lookup table) not reproduced here; relevant
  only if you're sending/receiving `MESsage` text containing non-standard characters and need
  the exact CRT-glyph mapping, in which case consult the original scanned manual directly —
  this table's OCR quality was too degraded to trust a transcription.
- **Exact byte-by-byte waveform-transfer example tables (7-17 through 7-21)** — the *prose*
  description of each encoding format (BINary/HEXadecimal/ASCII) is fully reproduced in the
  Waveform Commands section and the worked example above; the source's worked
  byte-offset example tables themselves were too OCR-garbled to transcribe reliably and were
  omitted rather than presented as fact. If you need the literal worked byte sequence, consult
  a clean copy of the original manual.
- **Exact RS-232-C DTE/DCE connector pin-number assignments (Tables 7-9/7-10)** — the
  signal set (TXD/RXD/RTS/CTS/DSR/DTR/RLSD/grounds) is included above as a reliable list,
  but the specific pin-number-to-signal mapping in the source tables had inconsistent OCR
  results across passes and is flagged rather than presented as authoritative; verify against
  the physical connector or a clean manual copy before wiring a cable.
- **Baud-rate switch-position table numeric values** — flagged with individual inline caveats
  above rather than excluded outright, since the *structure* (4-bit switch code, ascending rate,
  `1111` = Off Line) is certain even though 3 of the 15 specific rate values look like probable
  OCR corruption of standard rates.