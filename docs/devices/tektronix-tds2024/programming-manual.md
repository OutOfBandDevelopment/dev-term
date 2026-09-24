# Tektronix TDS2024 Remote Command Reference — RS-232 / TDS2CMA Configuration

This reference lists the commands supported by a **TDS2024** (4-channel, 200 MHz, 1 GS/s) fitted with a **TDS2CMA Communications Module**, controlled over **RS-232**. Source: Tektronix Programmer Manual 071-1075-02. Confirmed against real hardware: the scope's own `ID?` reply reports `TDS2CM:CMV:v1.04` (see `docs/changes/2026-09-24.md`), and `CM` is the module-type code the manual documents for a TDS2CM/TDS2CMA (as opposed to `MM` for a measurement module).

Two things this configuration implies:
- **Math/FFT is already native to the TDS2024.** Unlike the TDS200 series (TDS210/220/224), which needs a TDS2MM measurement module to gain Math/FFT/rise-fall-time/pulse-width measurement capability at all, every TDS1000/TDS2000-family scope — including the TDS2024 — has that capability built in natively, regardless of which communications module is installed. (TDS2MM itself isn't even a supported module for a TDS1000/TDS2000-series scope — the vendor manual's scope statement for this document lists only TDS2CM/TDS2CMA/TDS2MEM as compatible with these series.)
- **This module also has a GPIB port**, but since you're using it over RS-232, GPIB-only syntax details (EOI/END-message framing, GPIB device addressing) have been trimmed from this reference. Everything here is written for the RS-232 wire.

## Before you start

- Connect via the module's RS-232 (DB-9) port, not the front panel — the TDS2024 has no built-in serial or GPIB port.
- Set matching serial parameters on both ends using the `RS232:*` commands (§10) — baud rate, parity, and flow control must match your terminal/PC settings before anything else will respond.
- The TDS2CMA does **not** include the TDS2MEM's CompactFlash storage. That means `FILESystem:*`, `SAVe:IMAge`, `DATE`, `TIME`, and `HARDCopy:BUTTON` are **not available** in this configuration — they need a TDS2MEM module instead. They've been left out of this reference entirely; see §18 if you need them.

## Command syntax rules

- **Headers**: dotted/colon-separated hierarchy, e.g. `ACQuire:MODe`. Capital letters in a command name show the minimum abbreviation accepted — `ACQuire:MODe` can be sent as `ACQ:MOD`.
- **Set vs Query**: most commands have a set form (`CH1:SCAle 0.5`) and a query form ending in `?` (`CH1:SCAle?`). Some are set-only or query-only.
- **Concatenation**: chain multiple commands with `;`. If the headers differ beyond the last mnemonic, prefix the next command with `:`.
  ```
  ACQuire:MODe AVErage;:ACQuire:NUMAVg 16
  ```
  If only the last mnemonic differs, you can drop the repeated header:
  ```
  ACQuire:MODe AVErage;NUMAVg 16
  ```
  Never precede a `*`-star command with `:` or `;`.
- **RS-232 message terminators**: outgoing terminator is controlled by `RS232:TRANsmit:TERMinator` (CR, LF, or CRLF). Incoming, the scope accepts CR, LF, CRLF, or LFCR regardless of that setting. If you send a two-character combo (CRLF/LFCR), the scope treats the first character as the terminator and silently ignores the second as a null command.
- **Numeric argument types**:
  - `<NR1>` — signed integer (e.g. `16`)
  - `<NR2>` — float, no exponent (e.g. `1.25`)
  - `<NR3>` — float with exponent (e.g. `1.0E0`, `100E-3`)
- **`<QString>`** — ASCII text in single or double quotes, max 1000 chars on return.
- **`<Block>`** — binary data block: `#<n><len><data>`, where `<n>` is the digit-count of `<len>`, and `<len>` is the byte count that follows. Example: `#217<17 bytes>`. Over RS-232, only use *definite*-length blocks (`#<n><len>...`) — indefinite-length blocks (`#0...<terminator>`) rely on a GPIB EOI line RS-232 doesn't have, so there's no reliable way to embed a terminator byte inside the data.
- **Wildcard mnemonics**:
  - `CH<x>` — channel 1–4
  - `REF<x>` — reference waveform A–D
  - `<wfm>` — any of `CH<x>`, `MATH`, `REF<x>`
  - `MEAS<x>` — measurement slot 1–5
  - `POSITION<x>` — cursor 1 or 2

Out-of-range numeric arguments are clamped to the nearest valid value rather than rejected.

---

## 1. Acquisition Commands

| Command | Type | Description |
|---|---|---|
| `ACQuire?` | Query | Return all acquisition settings |
| `ACQuire:MODe` | Set/Query | Acquisition mode |
| `ACQuire:NUMACq?` | Query | Number of acquisitions since last start |
| `ACQuire:NUMAVg` | Set/Query | Number of waveforms averaged |
| `ACQuire:STATE` | Set/Query | Run/stop acquisition |
| `ACQuire:STOPAfter` | Set/Query | Stop condition (continuous vs single sequence) |

**`ACQuire:MODe { SAMple | PEAKdetect | AVErage }`**
Sets how the final value of each acquisition interval is derived from the raw samples.
- `SAMple` (default) — keeps the first sample in each interval. 8-bit precision.
- `PEAKdetect` — keeps the min/max range per interval; good for catching glitches/aliasing.
- `AVErage` — averages N consecutive waveform acquisitions (N set by `ACQuire:NUMAVg`).
```
ACQuire:MODe AVErage
ACQuire:MODe?      -> AVERAGE
```

**`ACQuire:NUMACq?`** (query only)
Returns `<NR1>`, the count of acquisitions since acquisition last started. Resets to 0 on most Acquisition/Horizontal/Vertical/Trigger changes (exceptions: trigger level/holdoff changes in Sample/Peak mode don't reset it). Any settings change while in Average mode aborts and resets the count.

**`ACQuire:NUMAVg <NR1>`**
Number of waveforms combined for averaging. Valid values: `4`, `16`, `64`, `128`.
```
ACQuire:NUMAVg 16
```

**`ACQuire:STATE { OFF | ON | RUN | STOP | <NR1> }`**
Equivalent to the front-panel RUN/STOP button.
- `OFF` / `STOP` / `0` — stop
- `ON` / `RUN` / nonzero — start (restarts sequence if mid-average, resets NUMACq)
Query returns `0` or `1`. Prefer `*OPC?` over polling `ACQuire:STATE?` to detect completion of a single-sequence acquisition.
```
ACQuire:STATE RUN
ACQuire:STATE?     -> 1
```

**`ACQuire:STOPAfter { RUNSTop | SEQuence }`**
- `RUNSTop` — free-running; stop/start controlled by `ACQuire:STATE` or the panel button.
- `SEQuence` — single-sequence: stop automatically once the acquisition mode's condition is satisfied (one trigger for Sample/Peak, N acquisitions for Average).
```
ACQuire:STOPAfter SEQuence
```

---

## 2. Calibration & Diagnostic Commands

| Command | Type | Description |
|---|---|---|
| `*CAL?` | Query | Run self-calibration, return pass/fail |
| `CALibrate:ABOrt` | Set | Abort in-progress factory calibration |
| `CALibrate:CONTINUE` | Set | Advance factory calibration one step |
| `CALibrate:FACtory` | Set | Start factory calibration sequence |
| `CALibrate:INTERNAL` | Set | Run self-cal, no status returned |
| `CALibrate:STATUS?` | Query | PASS/FAIL of last cal operation |
| `DIAg:RESUlt:FLAg?` | Query | PASS/FAIL of last diagnostic run |
| `DIAg:RESUlt:LOG?` | Query | Detailed diagnostic log |
| `ERRLOG:FIRST?` | Query | First entry in the error log |
| `ERRLOG:NEXT?` | Query | Next entry in the error log |

**`*CAL?`**
Runs internal self-calibration (equivalent to Utility menu → Do Self Cal). Disconnect all input signals first — takes several minutes, and the scope won't process other commands while running. Returns `0` on success, nonzero on failure.

**`CALibrate:INTERNAL`** (set only) — same self-cal as `*CAL?` but doesn't return a status; query `CALibrate:STATUS?` afterward.

**`CALibrate:STATUS?`** — returns `PASS` or `FAIL` for the most recent self- or factory-calibration since power-up.

**`CALibrate:FACtory` / `:CONTINUE` / `:ABOrt`** — service-level factory calibration sequence. Only synchronization commands (`*OPC`, `*WAI`, `BUSY?`) can be sent while a factory cal is in progress. Intended for qualified service use only.

**`DIAg:RESUlt:FLAg?`** — `PASS`/`FAIL` summary of the last diagnostic pass (auto-run at power-on, or via Service menu).

**`DIAg:RESUlt:LOG?`** — returns `<Status>,<Module name>[,...]`, e.g. `"pass-CPU, pass-ACQ1, pass-EXTENSION"`.

---

## 3. Cursor Commands

| Command | Type | Description |
|---|---|---|
| `CURSor?` | Query | All cursor settings |
| `CURSor:FUNCtion` | Set/Query | Cursor type: off / horizontal bars / vertical bars |
| `CURSor:HBArs?` | Query | Horizontal bar cursor settings |
| `CURSor:HBArs:DELTa?` | Query | Vertical distance between horizontal-bar cursors |
| `CURSor:HBArs:POSITION<x>` | Set/Query | Position of horizontal bar cursor x (1 or 2) |
| `CURSor:HBArs:UNIts?` | Query | Vertical units in use for cursors |
| `CURSor:SELect:SOUrce` | Set/Query | Waveform the cursors measure |
| `CURSor:VBArs?` | Query | Vertical bar cursor settings |
| `CURSor:VBArs:DELTa?` | Query | Time/frequency distance between vertical-bar cursors |
| `CURSor:VBArs:POSITION<x>` | Set/Query | Position of vertical bar cursor x |
| `CURSor:VBArs:UNIts` | Set/Query | Vertical-bar unit: seconds or Hertz |

**`CURSor:FUNCtion { HBArs | OFF | VBArs }`**
- `HBArs` — horizontal bar cursors, measure vertical quantities (V, A, div, dB)
- `VBArs` — vertical bar cursors, measure time or frequency
- `OFF` — hide cursors
Note: switching display to XY format removes cursors and setting `CURSor:FUNCtion` while in XY mode raises event 221 (settings conflict).
```
CURSor:FUNCtion VBArs
```

**`CURSor:SELect:SOUrce <wfm>`** — chooses which waveform's scale factors the cursors use.
```
CURSor:SELect:SOUrce CH1
```

**`CURSor:HBArs:POSITION<x> <NR3>`** — sets horizontal-bar cursor 1 or 2, in the units returned by `CURSor:HBArs:UNIts?` (volts/amps relative to ground, divisions from center, or dB relative to 1 Vrms for FFT sources).
```
CURSor:HBArs:POSITION1 25.0E-3
```

**`CURSor:HBArs:UNIts?`** — returns one of `VOLts`, `DIVs`, `DECIBELS`, `AMPS`, `VOLTSSQUARED`, `AMPSSQUARED`, `VOLTSAMPS`, or `UNKNOWN` (Trigger View active).

**`CURSor:VBArs:UNIts { SECOnds | HERtz }`** — sets time vs. frequency units for vertical-bar cursors (ignored/forced to Hz automatically if source is a Math FFT waveform).

**`CURSor:VBArs:POSITION<x> <NR3>`** — position relative to the trigger point (or in Hz if source is FFT).
```
CURSor:VBArs:POSITION2 9.00E-6
```

**`CURSor:HBArs:DELTa?` / `CURSor:VBArs:DELTa?`** — read-only differences between the two active cursors (vertical or horizontal, respectively). Return `9.9E37` and raise event 221 if Trigger View is active.

---

## 4. Display Commands

| Command | Type | Description |
|---|---|---|
| `DISplay?` | Query | All display settings |
| `DISplay:CONTRast` | Set/Query | LCD contrast |
| `DISplay:FORMat` | Set/Query | YT vs XY display mode |
| `DISplay:INVert` | Set/Query | Normal vs inverted mono display |
| `DISplay:PERSistence` | Set/Query | Waveform persistence/accumulate time |
| `DISplay:STYle` | Set/Query | Waveform draw style |

**`DISplay:FORMat { YT | XY }`** — `YT` is the standard time-domain view; `XY` plots CH1 vs CH2 (removes cursors).

**`DISplay:PERSistence <NR3>`** — sets how long old waveform data lingers on screen (accumulate/persist time), or off.

**`DISplay:STYle { VECtors | DOTs }`** — connect samples with lines, or show dots only.

**`DISplay:CONTRast <NR1>`** — LCD contrast, typically 0–100.
```
DISplay:FORMat YT
DISplay:PERSistence?   -> :DISPLAY:PERSISTENCE OFF
```

---

## 5. Hard Copy (Print) Commands

| Command | Type | Description |
|---|---|---|
| `HARDCopy` | Set | Start or terminate a hard copy (print/screen dump) |
| `HARDCopy:FORMat` | Set/Query | Output file/print format |
| `HARDCopy:INKSaver` | Set/Query | Ink-saver (inverted background) mode |
| `HARDCopy:LAYout` | Set/Query | Portrait vs landscape |
| `HARDCopy:PORT` | Set/Query | Output port |

**`HARDCopy { STARt | STOP }`** — begins or aborts a print/hardcopy job.

**`HARDCopy:FORMat { BMP | BMPColor | DESKJET | DPU411 | DPU412 | DPU3445 | EPSColor | EPSMono | EPSOn | INTERLEAF | JPEG | LASERJET | PCX | PCXCOLOR | PCXEPSON | RLE | THINKJET | TIFF }`** — output format (available options depend on connected printer).

**`HARDCopy:PORT { CENtronics | RS232 | GPIb }`** — output interface for the hardcopy job itself. On a TDS2CMA this can legitimately be `GPIb` too (the module has both a GPIB and a Centronics parallel port alongside RS-232), but since you're running over RS-232, set it to `RS232` to route hardcopy output down the same serial link you're already using. (The vendor manual's own constraint here is the opposite of what you might expect: it's `GPIb` that's unavailable — specifically on a TDS2MEM module or a TPS2000 — not `CENtronics`, which is available on any communications module including the TDS2CMA installed here.)

```
HARDCopy:PORT RS232
HARDCopy:FORMat BMP
HARDCopy STARt
```

---

## 6. Horizontal Commands

| Command | Type | Description |
|---|---|---|
| `HORizontal?` | Query | All horizontal settings |
| `HORizontal:DELay?` | Query | Window (delayed) time base settings |
| `HORizontal:DELay:POSition` | Set/Query | Window position |
| `HORizontal:DELay:SCAle` (`:SECdiv`) | Set/Query | Window time/division |
| `HORizontal:MAIn?` | Query | Main time base time/division |
| `HORizontal:MAIn:POSition` | Set/Query | Main time base trigger point position |
| `HORizontal:MAIn:SCAle` (`:SECdiv`) | Set/Query | Main time base time/division |
| `HORizontal:POSition` | Set/Query | Waveform display position |
| `HORizontal:RECOrdlength` | Query | Waveform record length |
| `HORizontal:SCAle` / `:SECdiv` | Set/Query | Alias for `HORizontal:MAIn:SCAle` |
| `HORizontal:VIEW` | Set/Query | Select Main or Window view |

**`HORizontal:MAIn:SCAle <NR3>`** — sets seconds/division of the main timebase. `SECdiv` is an accepted synonym for `SCAle` throughout the horizontal group (kept for compatibility with older Tek scopes).
```
HORizontal:MAIn:SCAle 500E-6
HORizontal:SCAle?      -> 5.00E-4
```

**`HORizontal:MAIn:POSition <NR3>`** — trigger point position along the record (percentage or time offset, per firmware).

**`HORizontal:RECOrdlength?`** (query only) — returns the number of samples in the waveform record (fixed at 2500 on this family).

**`HORizontal:DELay:SCAle` / `:POSition`** — same idea as `MAIn`, but for the zoomed "window"/delayed time base when Window view is active.

**`HORizontal:VIEW { MAIn | WINdow | ZONE }`** — chooses whether Main or Window (delayed) time base is displayed.

---

## 7. Math Commands

*(Native to the TDS2024's TDS2000-series firmware — fully present with the TDS2CMA installed, same as it would be with any other communications module.)*

| Command | Type | Description |
|---|---|---|
| `MATH?` | Query | Math waveform definition |
| `MATH:DEFINE` | Set/Query | Set the math expression |
| `MATH:FFT?` | Query | All FFT parameters |
| `MATH:FFT:HORizontal:POSition` | Set/Query | FFT horizontal display position |
| `MATH:FFT:HORizontal:SCAle` | Set/Query | FFT horizontal zoom |
| `MATH:FFT:VERtical:POSition` | Set/Query | FFT vertical display position |
| `MATH:FFT:VERtical:SCAle` | Set/Query | FFT vertical zoom |
| `MATH:VERtical?` | Query | All math vertical parameters |

**`MATH:DEFINE <QString>`** — sets the math expression, e.g. `"CH1-CH2"`, `"CH1+CH2"`, or `"FFT(CH1)"`. Available operators/functions depend on firmware but generally cover add/subtract between channels and FFT.
```
MATH:DEFINE "FFT(CH1)"
MATH:DEFINE?    -> "FFT(CH1)"
```

**`MATH:FFT:HORizontal:SCAle <NR3>`** — horizontal (frequency axis) zoom factor for the FFT display.

**`MATH:FFT:VERtical:SCAle <NR3>`** — vertical (amplitude axis) zoom/scale for the FFT display, typically in dB/division.

Once an FFT math waveform is defined, you can pull its samples the same way as any other waveform — set `DATa:SOUrce MATH` and run the normal `CURVe?` transfer sequence (§15). `WFMPre:XUNit` will read `"Hz"` and `WFMPre:YUNit` will typically read `"dB"` for an FFT trace instead of the usual `"s"`/`"V"`.

---

## 8. Measurement Commands

| Command | Type | Description |
|---|---|---|
| `MEASUrement?` | Query | All measurement parameters |
| `MEASUrement:IMMed?` | Query | Immediate-measurement parameters |
| `MEASUrement:IMMed:SOUrce1` | Set/Query | Source channel for immediate measurement |
| `MEASUrement:IMMed:TYPe` | Set/Query | Which measurement to compute immediately |
| `MEASUrement:IMMed:UNIts?` | Query | Units of the immediate measurement |
| `MEASUrement:IMMed:VALue?` | Query | Result of the immediate measurement |
| `MEASUrement:MEAS<x>?` | Query | All parameters for on-screen slot x (1–5) |
| `MEASUrement:MEAS<x>:SOUrce` | Set/Query | Source channel for slot x |
| `MEASUrement:MEAS<x>:TYPe` | Set/Query | Measurement type for slot x |
| `MEASUrement:MEAS<x>:UNIts?` | Query | Units for slot x |
| `MEASUrement:MEAS<x>:VALue?` | Query | Current value of slot x |

**Recommended workflow for programmatic readings:** use `MEASUrement:IMMed:*` rather than the on-screen `MEAS<x>` slots — immediate measurements have no front-panel display and are computed only on request, so they don't slow down the waveform update rate.

**`MEASUrement:IMMed:TYPe` / `MEASUrement:MEAS<x>:TYPe`**
```
{ FREQuency | MEAN | PERIod | PK2pk | CRMs | MINImum | MAXImum |
  RISe | FALL | PWIdth | NWIdth | PHAse | OFF }
```
- `FREQuency` — cycle frequency
- `PERIod` — cycle period
- `PK2pk` — peak-to-peak amplitude
- `CRMs` — cyclic RMS amplitude
- `MEAN` — arithmetic mean
- `MINImum` / `MAXImum` — min/max amplitude
- `RISe` / `FALL` — rise time / fall time
- `PWIdth` / `NWIdth` — positive/negative pulse width
- `PHAse` — phase between two sources
- `OFF` — turn the measurement slot off

```
MEASUrement:IMMed:SOUrce1 CH1
MEASUrement:IMMed:TYPe FREQuency
MEASUrement:IMMed:VALue?    -> 1.0000E3
```

**`MEASUrement:MEAS<x>:VALue?`** — returns `9.9E37` if the measurement can't be computed (e.g., no valid waveform edges).


---

## 9. Miscellaneous Commands

| Command | Type | Description |
|---|---|---|
| `AUTOSet` | Set | Trigger an autoset |
| `AUTOSet:SIGNAL?` | Query | Signal type found by the last autoset |
| `AUTOSet:VIEW` | Set/Query | Autoset display view |
| `*DDT` | Set/Query | Command(s) to run on trigger/GET |
| `FACtory` | Set | Reset to factory defaults |
| `HDR` | Set/Query | Alias for `HEADer` |
| `HEADer` | Set/Query | Include headers in query responses |
| `ID?` | Query | Identification string |
| `*IDN?` | Query | IEEE 488.2 identification string |
| `LANGUAGE` | Set/Query | Display message language |
| `LOCk` | Set | Lock the front panel |
| `*LRN?` | Query | Full instrument setup (learn string) |
| `REM` | Set | No-op / remark |
| `*RST` | Set | Reset to default state |
| `SET?` | Query | Alias for `*LRN?` |
| `*TRG` | Set | Force/software trigger (GET) |
| `*TST?` | Query | Run self-test, return result |
| `UNLock` | Set | Unlock the front panel |
| `VERBose` | Set/Query | Full vs. abbreviated command headers in responses |

**`AUTOSet EXECute`** — runs autoset (equivalent to pressing the AUTOSET button): adjusts vertical, horizontal, and trigger to find a stable display.

**`AUTOSet:SIGNAL?`** — returns `{ LEVEL | SINE | SQUARE | VIDPAL | VIDNTSC | OTHER | NONe }`, the signal type autoset last classified.

**`*IDN?`** — returns manufacturer, model, and firmware version, plus a module-identity suffix (`TDS2XX:XXV:v<module firmware>`, where `XX` is `CM` for a TDS2CM/TDS2CMA communications module or `MM` for a measurement module), e.g.:
```
*IDN?
-> TEKTRONIX,TDS 2024,0,CF:91.1CT FV:v22.01 TDS2CM:CMV:v1.04
```

**`ID?`** — Tektronix legacy equivalent of `*IDN?`, same information in a slightly different (comma-separated, non-IEEE-488.2) format. Confirmed against real hardware:
```
ID?
-> ID TEK/TDS 2024,CF:91.1CT,FV:v4.12 TDS2CM:CMV:v1.04
```

**`*RST`** — resets most settings to factory defaults (does not affect communication parameters like GPIB address).

**`*LRN?`** (a.k.a. `SET?`) — returns the complete current instrument setup as a string of set commands; useful for saving/restoring state at the application layer.

**`HEADer { ON | OFF }`** — when `ON`, query responses are prefixed with the command header (e.g. `:CH1:SCALE 1.0E0`); when `OFF`, only the value is returned (`1.0E0`). Combine with `VERBose` to control whether headers are abbreviated or spelled out.

**`LOCk { ALL | NONe }`** / **`UNLock ALL`** — disables/enables the front panel (local lockout), useful during automated test runs so an operator can't interfere.

**`*DDT { <Block> | <QString> }`** — defines a command sequence (≤80 chars) that executes whenever `*TRG` or GPIB GET is received.
```
*DDT #217ACQuire:STATE RUN
```

**`*TRG`** — executes whatever is defined by `*DDT` (default: forces a trigger event). Cannot be preceded by `:` or `;` in a concatenated string.

```
*IDN?
HEADer OFF
CH1:SCAle?     -> 1.0E0
*RST
```

---

## 10. RS-232 Commands

| Command | Type | Description |
|---|---|---|
| `RS232?` | Query | All RS-232 parameters |
| `RS232:BAUd` | Set/Query | Baud rate |
| `RS232:HARDFlagging` | Set/Query | Hardware flow control |
| `RS232:PARity` | Set/Query | Parity |
| `RS232:SOFTFlagging` | Set/Query | Software (XON/XOFF) flow control |
| `RS232:TRANsmit:TERMinator` | Set/Query | Outgoing end-of-line terminator |

**`RS232:BAUd { 1200 | 2400 | 4800 | 9600 | 19200 | 38400 }`** — typical supported rates (exact list depends on module firmware).

**`RS232:PARity { NONe | ODD | EVEN }`**

**`RS232:HARDFlagging { ON | OFF }`** / **`RS232:SOFTFlagging { ON | OFF }`** — hardware (RTS/CTS) vs. software (XON/XOFF) handshaking. Typically only one is enabled at a time.

**`RS232:TRANsmit:TERMinator { CR | LF | CRLF }`** — terminator appended to outgoing messages.

```
RS232:BAUd 9600
RS232:PARity NONe
RS232:HARDFlagging OFF
```

---

## 11. Save and Recall Commands

| Command | Type | Description |
|---|---|---|
| `*SAV <NR1>` | Set | Save current setup to internal slot |
| `*RCL <NR1>` | Set | Recall setup from internal slot |
| `RECAll:SETUp` | Set | Recall a named setup |
| `RECAll:WAVEform` | Set | Recall a stored waveform into a reference slot |
| `SAVe:SETUp` | Set | Save current setup |
| `SAVe:WAVEform` | Set | Save a live/math waveform to a reference slot |

**`*SAV <NR1>`** / **`*RCL <NR1>`** — save/recall the full instrument setup to/from one of the internal setup memory slots (`<NR1>` is the slot number, typically 1–10).
```
*SAV 1
*RCL 1
```

**`SAVe:WAVEform <wfm>,REF<x>`** — copies a live channel or math waveform into non-volatile reference memory.
```
SAVe:WAVEform MATH,REFA
```

**`RECAll:WAVEform REF<x>,<wfm>`** — the reverse: loads a stored reference waveform back into a channel/math slot for redisplay.

Note: `SAVe:IMAge` (screen-capture-to-file) isn't available here — it needs the TDS2MEM's CompactFlash storage, not present on a TDS2CMA. To get a screen image over this link, use the `HARDCopy` commands (§5) instead, which stream the image out over RS-232 rather than saving to a card.

---

## 12. Status and Error Commands

Standard IEEE 488.2 status/event commands (all begin with `*` except a few Tek extensions).

| Command | Type | Description |
|---|---|---|
| `ALLEv?` | Query | Dump and clear all queued events |
| `BUSY?` | Query | Is the scope currently busy |
| `*CLS` | Set | Clear status (event queue, SESR, status byte) |
| `DESE` | Set/Query | Device Event Status Enable Register mask |
| `*ESE` | Set/Query | Standard Event Status Enable Register |
| `*ESR?` | Query | Read + clear Standard Event Status Register |
| `EVENT?` | Query | Return next event code |
| `EVMsg?` | Query | Return next event message |
| `EVQty?` | Query | Number of events queued |
| `*OPC` | Set/Query | Operation-complete flag/query |
| `*PSC` | Set/Query | Power-on status clear behavior |
| `*SRE` | Set/Query | Service Request Enable Register |
| `*STB?` | Query | Read the Status Byte Register |
| `*WAI` | Set | Wait until all pending operations finish |

**`*CLS`** — clears the Event Queue, SESR, and Status Byte (except MAV). Use at the start of a test sequence to get a clean error-reporting slate.

**`*ESR?`** (query only) — reads and clears the Standard Event Status Register; the standard way to check whether the last set command executed cleanly (nonzero = an error/event occurred — follow up with `EVMsg?`/`ALLEv?` for details).

**`BUSY?`** — returns `1` while the scope is processing a long-running command (e.g. acquisition, calibration, hardcopy), `0` otherwise. Poll this (or better, use `*OPC?`) to synchronize your script with the scope.

**`*OPC`** (set) — sets the OPC bit in SESR once all pending overlapped operations complete (useful with SRQ).
**`*OPC?`** (query) — blocks/returns `1` once all pending operations finish; this is the recommended way to know when a single-sequence acquisition, hardcopy, or self-cal is done, rather than polling `ACQuire:STATE?` or `BUSY?`.
```
ACQuire:STATE RUN
*OPC?
-> 1        (returned only once acquisition finishes)
```

**`ALLEv?`** — drains the entire event queue in one call:
```
ALLEv?
-> 2225,"Measurement error, No waveform to measure;",420,"Query UNTERMINATED; "
```

**`DESE <NR1>`**, **`*ESE <NR1>`**, **`*SRE <NR1>`** — set 0–255 bitmasks controlling which categories of event propagate to SESR / generate SRQ. See the Status & Events chapter of the Tek manual for full bit definitions (PON, URQ, CME, EXE, DDE, QYE, RQC, OPC bits).

---

## 13. Trigger Commands

| Command | Type | Description |
|---|---|---|
| `TRIGger` | Set | Force a trigger event |
| `TRIGger:MAIn` | Set/Query | Set trigger level to 50% / return all main trigger settings |
| `TRIGger:MAIn:EDGE?` | Query | Edge trigger settings |
| `TRIGger:MAIn:EDGE:COUPling` | Set/Query | Edge trigger coupling |
| `TRIGger:MAIn:EDGE:SLOpe` | Set/Query | Rising or falling edge |
| `TRIGger:MAIn:EDGE:SOUrce` | Set/Query | Edge trigger source |
| `TRIGger:MAIn:FREQuency?` | Query | Measured trigger frequency |
| `TRIGger:MAIn:HOLDOff?` / `:VALue` | Query / Set/Query | Trigger holdoff time |
| `TRIGger:MAIn:LEVel` | Set/Query | Trigger level (volts) |
| `TRIGger:MAIn:MODe` | Set/Query | Auto vs Normal trigger mode |
| `TRIGger:MAIn:PULse?` | Query | Pulse-width trigger settings |
| `TRIGger:MAIn:PULse:SOUrce` | Set/Query | Pulse trigger source |
| `TRIGger:MAIn:PULse:WIDth?` | Query | Pulse width trigger parameters |
| `TRIGger:MAIn:PULse:WIDth:POLarity` | Set/Query | Positive/negative pulse |
| `TRIGger:MAIn:PULse:WIDth:WHEN` | Set/Query | Trigger-when condition (`<`,`>`,`=` width) |
| `TRIGger:MAIn:PULse:WIDth:WIDth` | Set/Query | Pulse-width threshold |
| `TRIGger:MAIn:TYPe` | Set/Query | Edge / Video / Pulse trigger |
| `TRIGger:MAIn:VIDeo?` | Query | Video trigger settings |
| `TRIGger:MAIn:VIDeo:LINE` | Set/Query | Video line number |
| `TRIGger:MAIn:VIDeo:POLarity` | Set/Query | Video sync polarity |
| `TRIGger:MAIn:VIDeo:SOUrce` | Set/Query | Video trigger source |
| `TRIGger:MAIn:VIDeo:STANdard` | Set/Query | NTSC / PAL / SECAM |
| `TRIGger:MAIn:VIDeo:SYNC` | Set/Query | Sync on field or line |
| `TRIGger:STATE?` | Query | Overall trigger system status |

**`TRIGger:MAIn:TYPe { EDGE | VIDeo | PULse }`** — selects the trigger class; the sub-branch you then configure depends on this (`:EDGE:*`, `:VIDeo:*`, `:PULse:*`).

**`TRIGger:MAIn:EDGE:SOUrce { CH1 | CH2 | CH3 | CH4 | EXT | EXT5 | EXT10 | ACLine }`** — trigger source. `EXT`/`EXT5`/`EXT10` refer to the external trigger BNC input, with attenuation factors.

**`TRIGger:MAIn:EDGE:SLOpe { RISe | FALL }`**

**`TRIGger:MAIn:EDGE:COUPling { AC | DC | NOISErej | HFRej | LFRej }`**

**`TRIGger:MAIn:LEVel <NR3>`** — trigger threshold voltage. `TRIGger:MAIn` (set, no argument) forces the level to 50% of the signal automatically.
```
TRIGger:MAIn:TYPe EDGE
TRIGger:MAIn:EDGE:SOUrce CH1
TRIGger:MAIn:EDGE:SLOpe RISe
TRIGger:MAIn:LEVel 1.2
```

**`TRIGger:MAIn:MODe { AUTO | NORMal }`** — `AUTO` free-runs (displays a waveform even with no valid trigger); `NORMal` only sweeps on a valid trigger event.

**`TRIGger:MAIn:HOLDOff:VALue <NR3>`** — minimum time the scope waits after one trigger before arming for the next; helps stabilize triggering on complex repetitive signals.

**`TRIGger:MAIn:PULse:WIDth:WHEN { LESSthan | MOREthan | EQual }`** and **`:WIDth <NR3>`** — pulse-width trigger condition and threshold time, used with `:POLarity { POSitive | NEGative }`.
```
TRIGger:MAIn:TYPe PULse
TRIGger:MAIn:PULse:SOUrce CH1
TRIGger:MAIn:PULse:WIDth:POLarity POSitive
TRIGger:MAIn:PULse:WIDth:WHEN LESSthan
TRIGger:MAIn:PULse:WIDth:WIDth 100E-6
```

**`TRIGger:MAIn:VIDeo:STANdard { NTSC | PAL | SECAM }`**, **`:SYNC { ALLLines | LINEnumber | ODD | EVEN }`**, **`:LINE <NR1>`** — configure broadcast-video triggering.

**`TRIGger:STATE?`** — returns `{ ARMed | AUTO | READY | SAVE | TRIGgered }` describing the trigger system's current state.

**`TRIGger`** (set only) — forces an immediate trigger, regardless of trigger conditions (like pressing FORCE TRIG).

---

## 14. Vertical Commands

| Command | Type | Description |
|---|---|---|
| `CH<x>?` | Query | All vertical settings for channel x |
| `CH<x>:BANdwidth` | Set/Query | 20 MHz bandwidth limit on/off |
| `CH<x>:COUPling` | Set/Query | AC / DC / GND coupling |
| `CH<x>:INVert` | Set/Query | Invert the channel |
| `CH<x>:POSition` | Set/Query | Vertical position (divisions) |
| `CH<x>:PRObe` | Set/Query | Probe attenuation factor |
| `CH<x>:SCAle` (`:VOLts`) | Set/Query | Volts/division |
| `SELect?` | Query | Which waveforms are displayed |
| `SELect:<wfm>` | Set/Query | Show/hide a specific waveform |

**`CH<x>:COUPling { AC | DC | GND }`**

**`CH<x>:BANdwidth { ON | OFF }`** — `ON` limits to 20 MHz (useful for reducing noise on low-bandwidth signals); `OFF` uses full bandwidth (up to 200 MHz on the TDS2024).

**`CH<x>:SCAle <NR3>`** — volts/division, 2 mV/div to 5 V/div range with a 1X probe (scales with probe attenuation).
```
CH1:SCAle 100E-3      ! 100 mV/div
```

**`CH<x>:POSition <NR3>`** — vertical position in divisions from center; valid range depends on the current `CH<x>:SCAle` (e.g. ±10 divs at 2 V/div, ±1000 divs at 2 mV/div — see manual Table 2-27 for the full table).

**`CH<x>:PRObe { 1 | 10 | 100 | 1000 }`** — sets the attenuation factor the scope assumes for the attached probe (most 1X/10X passive probes use `1` or `10`; `20`, `50`, and `500` are additional TPS2000-only factors and aren't valid on the TDS2024).
```
CH1:PRObe 10
```

**`CH<x>:INVert { ON | OFF }`** — not supported on early-firmware TDS210/220 + TDS2CMA combos, but present on the TDS2024.

**`SELect:<wfm> { ON | OFF | <NR1> }`** — shows or hides a channel, MATH, or REF waveform on screen (independent from whether it's acquiring).
```
SELect:CH1 ON
SELect:CH2 OFF
SELect?     -> :SELECT:CH1 1;CH2 0;CH3 0;CH4 0;MATH 0
```

---

## 15. Waveform Transfer Commands

This is the group you'll use most for pulling acquired data off the scope into a PC.

| Command | Type | Description |
|---|---|---|
| `CURVe` | Set/Query | Transfer the actual waveform data samples |
| `DATa` | Set/Query | Reinitialize / query the data-transfer setup |
| `DATa:DESTination` (`:TARget`) | Set/Query | Where incoming waveform data is stored (REF<x>) |
| `DATa:ENCdg` | Set/Query | Data encoding: ASCII or one of 4 binary formats |
| `DATa:SOUrce` | Set/Query | Which waveform `CURVe?` reads from |
| `DATa:STARt` | Set/Query | First data point to transfer (1–2500) |
| `DATa:STOP` | Set/Query | Last data point to transfer (1–2500) |
| `DATa:WIDth` | Set/Query | Bytes per data point: 1 or 2 |
| `WAVFrm?` | Query | Waveform preamble + curve data in one shot |
| `WFMPre?` | Query | Waveform preamble only |
| `WFMPre:BIT_Nr` | Set/Query | Bits per data point |
| `WFMPre:BN_Fmt` | Set/Query | Binary format: RI (signed) or RP (positive) |
| `WFMPre:BYT_Nr` | Set/Query | Bytes per data point |
| `WFMPre:BYT_Or` | Set/Query | Byte order: MSB or LSB first |
| `WFMPre:ENCdg` | Set/Query | ASC or BIN |
| `WFMPre:NR_Pt?` | Query | Number of points in the transferred curve |
| `WFMPre:PT_Fmt` | Set/Query | Point format |
| `WFMPre:PT_Off?` | Query | Trigger offset in the record |
| `WFMPre:WFId?` | Query | Waveform identifier string |
| `WFMPre:XINcr` | Set/Query | Time between points (seconds) |
| `WFMPre:XUNit` | Set/Query | Horizontal (X) units |
| `WFMPre:XZEro` | Set/Query | Time of the first point |
| `WFMPre:YMUlt` | Set/Query | Vertical scale factor (volts/level) |
| `WFMPre:YOFf` | Set/Query | Vertical offset (levels) |
| `WFMPre:YUNit` | Set/Query | Vertical (Y) units |
| `WFMPre:YZEro?` | Query | Vertical conversion factor |

### How the data path fits together

Internally every sample is one raw byte (0–255 or -128–127). `DATa:WIDth` lets you request 1 or 2 bytes per point on the wire; with width 2, the raw byte is left-shifted into the MSB and the LSB is padded with 0.

**`DATa:ENCdg { ASCIi | RIBinary | RPBinary | SRIbinary | SRPbinary }`**

| Setting | Meaning | Corresponds to WFMPre |
|---|---|---|
| `ASCIi` | comma-separated signed decimal text | `ENCdg=ASC` |
| `RIBinary` | signed int, MSB first (fastest for width=2) | `ENCdg=BIN;BN_Fmt=RI;BYT_Or=MSB` |
| `RPBinary` | positive int, MSB first | `ENCdg=BIN;BN_Fmt=RP;BYT_Or=MSB` |
| `SRIbinary` | signed int, LSB first (swapped, PC-friendly) | `ENCdg=BIN;BN_Fmt=RI;BYT_Or=LSB` |
| `SRPbinary` | positive int, LSB first | `ENCdg=BIN;BN_Fmt=RP;BYT_Or=LSB` |

Setting `DATa:ENCdg` updates the corresponding `WFMPre:*` fields automatically (and vice versa).

**`DATa:SOUrce <wfm>`** — `CH1`–`CH4`, `MATH`, or `REF<x>`; only one source per `CURVe?` call.

**`DATa:STARt <NR1>` / `DATa:STOP <NR1>`** — 1–2500. Use `1` / `2500` to always grab the full record. On read-in (sending data to the scope with `CURVe`), only `DATa:STARt` matters — the scope stops when it runs out of incoming data or hits 2500 points.

**`CURVe { <Block> | <NR1>[,<NR1>...] }`** and **`CURVe?`**
- Query pulls data **from** the scope, using `DATa:SOUrce`.
- Set pushes data **to** the scope's `DATa:DESTination` reference slot.
- Binary block format: `#<n><len><bytes>`, e.g. `#3500<500 bytes>` — `n=3` digits describing a length of `500`.
- If the requested source isn't currently displayed, `CURVe?` returns nothing and raises events 2244 + 420.

**`WFMPre?`** returns a preamble string with the scale factors needed to convert raw levels to real units, e.g.:
```
:WFMPRE:BYT_NR 1;BIT_NR 8;ENCDG BIN;BN_FMT RP;BYT_OR MSB;NR_PT 2500;
WFID "Ch1, DC coupling, 1.0E0 V/div, 5.0E-4 s/div, 2500 points, Sample mode";
PT_FMT Y;XINCR 2.0E-6;PT_OFF 0;XZERO 0.0E0;XUNIT "s";
YMULT 4.0E-2;YZERO 0.0E0;YOFF 127;YUNIT "V"
```

**Converting a raw sample to a real value:**
```
voltage = YZEro + YMUlt * (raw_value - YOFf)
time    = XZEro + XINcr * point_index
```

### Standard transfer sequence — reading a waveform out

```
DATa:SOUrce CH1          ! choose channel
DATa:ENCdg RIBinary       ! signed binary, MSB first
DATa:WIDth 1              ! 1 byte/point (2500 bytes total)
DATa:STARt 1
DATa:STOP 2500
WFMPre?                   ! capture scaling info first
CURVe?                    ! then pull the raw samples
```
On the PC side (pyvisa-style pseudocode):
```python
scope.write("DATA:SOURCE CH1")
scope.write("DATA:ENCDG RIBINARY")
scope.write("DATA:WIDTH 1")
scope.write("DATA:START 1")
scope.write("DATA:STOP 2500")
ymult = float(scope.query("WFMPRE:YMULT?"))
yzero = float(scope.query("WFMPRE:YZERO?"))
yoff  = float(scope.query("WFMPRE:YOFF?"))
xincr = float(scope.query("WFMPRE:XINCR?"))
raw = scope.query_binary_values("CURVE?", datatype='b', container=list)
volts = [yzero + ymult * (v - yoff) for v in raw]
times = [i * xincr for i in range(len(volts))]
```

### Sending a waveform into the scope (reference memory)

```
DATa:DESTination REFA
DATa:ENCdg RIBinary
DATa:WIDth 1
DATa:STARt 1
CURVe #3500<500 bytes of data>
```

Notes:
- The scope stores waveforms only up to 2500 points; longer ones are truncated.
- Only one waveform can be transferred at a time, in either direction.
- **In Scan mode** (sec/div ≥ 100 ms in Auto trigger mode), roughly one division's worth of points near the moving cursor are invalid/blanked.

---

## 16. Worked Example: Full Acquisition Script

A typical automated capture sequence, combining several command groups:

```
*CLS                          ! clear status
*IDN?                         ! confirm you're talking to the right scope
HEADer OFF                    ! simpler query responses

CH1:COUPling DC
CH1:SCAle 0.5                 ! 500 mV/div
CH1:PRObe 10                  ! 10X probe
HORizontal:MAIn:SCAle 1E-3    ! 1 ms/div

TRIGger:MAIn:TYPe EDGE
TRIGger:MAIn:EDGE:SOUrce CH1
TRIGger:MAIn:EDGE:SLOpe RISe
TRIGger:MAIn:LEVel 1.0
TRIGger:MAIn:MODe NORMal

ACQuire:STOPAfter SEQuence    ! single-shot capture
ACQuire:STATE RUN
*OPC?                         ! blocks until acquisition completes -> returns 1

DATa:SOUrce CH1
DATa:ENCdg RIBinary
DATa:WIDth 1
DATa:STARt 1
DATa:STOP 2500
WFMPre?
CURVe?
```

## 17. Common Gotchas

- **Serial settings must match on both ends first** — wrong baud/parity/flow-control means nothing responds at all, with no error to diagnose remotely; double check `RS232:*` (§10) against your terminal/PC settings before troubleshooting anything else.
- **Star commands (`*...`) can never be preceded by `:` or `;`** when concatenating — the scope silently ignores the star command if you do.
- **Only the *last* query in a concatenated string can return arbitrary/binary data** (e.g. `ID?`, `CURVe?`) — otherwise you'll get event 440 (Query UNTERMINATED... mid-string).
- **Trigger View mode** (front-panel TRIG VIEW button held) makes the scope ignore most *set* commands and forces many cursor/measurement queries to return `9.9E37` with event 221.
- **Waveform record is capped at 2500 points** — `DATa:STARt`/`DATa:STOP` can't exceed that regardless of acquisition mode.
- **Use `*OPC?`, not polling `ACQuire:STATE?`,** to know when a single-sequence acquisition, hardcopy, or self-cal has actually finished.
- **Abbreviate freely** — only the capitalized letters in each mnemonic are required, e.g. `ACQuire:NUMAVg` → `ACQ:NUMA`.
- **Binary block transfers over RS-232 are slower and less robust than GPIB** — no EOI line to mark end-of-data, and flow control (`RS232:HARDFlagging`/`:SOFTFlagging`) matters more at higher baud rates or with large `CURVe?` transfers (2500 bytes at width=1). If you see truncated/garbled waveform data, try a lower baud rate or `ASCIi` encoding first to rule out a flow-control mismatch.

## 18. What's Different With This Configuration (TDS2CMA, RS-232)

Everything in this document works as written with a TDS2CMA module over RS-232. Two things worth knowing if your setup ever changes:

- **If you switch to GPIB** (the TDS2CMA supports it too): binary block transfers become far more reliable (EOI marks the end of data cleanly), and you can drop the RS-232-specific caveats in §10/§18 above. `HARDCopy:PORT GPIb` becomes usable at that point.
- **If you add/swap to a TDS2MEM module**: you gain `FILESystem:*`, `SAVe:IMAge`/`:FILEFormat`, `DATE`, `TIME`, and `HARDCopy:BUTTON` (all omitted here since TDS2CMA doesn't support them) — but you lose GPIB, since TDS2MEM is RS-232/Centronics only (no GPIB port at all).

Commands excluded from this document entirely because the TDS2024 hardware can't support them under **any** module: `POWer:*`, `HARmonics:*`, `SWLoss:*`, `POWerANALYSIS:*`, `WAVEFORMANALYSIS:*` (TPS2000 + Power Analysis application only), `AUTORange:*`, `CH<x>:CURRENTPRObe`, `CH<x>:YUNit`, `DISplay:BRIGHTness`, `MATH:VERtical:POSition`/`:SCAle`, `MEASUrement:IMMed:SOUrce2`, `CURSor:VBArs:HDELTa?`/`:HPOS<x>?`/`:VDELTa?`/`:SLOPE?` — these are all TPS2000-only, several additionally requiring the TPS2PWR1 Power Analysis key.

---

*Reference: Tektronix TDS200/TDS1000/TDS2000/TPS2000 Series Programmer Manual, part number 071-1075-02.*