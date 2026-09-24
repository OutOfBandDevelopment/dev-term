# Rigol DM3058E — Programming Manual (USB / RS-232)

5½-digit digital multimeter. This manual covers the native RIGOL SCPI-style command set
reachable over the DM3058E's **USB** and **RS-232** interfaces — the two remote interfaces
this hardware actually has.

## Source document

**RIGOL Programming Guide — DM3058/DM3058E Digital Multimeter**, Jan. 2015, RIGOL
Technologies, Inc. (No separate publication number was printed in the extracted copyright
block of this edition.) This single guide documents both the DM3058 and DM3058E together,
calling out per-model interface differences inline — it is the sole, canonical, current
vendor source used here.

Critically, the guide states outright (Chapter 1, "Programming Introduction"):

> "Note\*: The GPIB and LAN interfaces are only supported by DM3058."

and, in the `:LXI Commands` section:

> "Note: :LXI commands are only applicable to DM3058 because DM3058E has no LAN interface."

So on the **DM3058E** specifically, only **USB** and **RS-232** are real remote-control
options — see "What's excluded and why" for how this shaped this manual's scope.

## Before you start

### Physical connection

- **USB** — a USB port on the rear panel presenting as a USB-TMC-class device; connect with
  a standard USB cable to a PC. No RIGOL-specific driver is required for the command
  protocol itself beyond a generic USB-TMC/VISA driver (e.g. NI-VISA or Rigol's own IVI
  driver package), the same pattern used by other Rigol USB-Device instruments.
- **RS-232** — a standard RS-232 port on the rear panel for direct serial connection to a PC
  (or via a USB-to-RS232 adapter).

Both interfaces carry the identical SCPI-style command set described below.

### RS-232 settings

| Setting | Value |
|---|---|
| Baud rate | Configurable: 1200 / 2400 / 4800 / 9600 / 19200 / 38400 / 57600 / 115200 (set via `:UTILity:INTErface:RS232:BAUD`) |
| Parity / data bits | Configurable as a coupled pair via `:UTILity:INTErface:RS232:PARIty` — see the command's own entry below; there is no independent data-bits setting |
| Stop bits | Not separately configurable in this command set — treat as fixed (1) |
| Flow control | Not documented as configurable — assume none |

**You must set these to match on both ends** (front-panel `Utility` menu on the instrument,
matching serial port config on the PC) before RS-232 communication will work; there is no
auto-bauding.

## Command syntax conventions

- Commands are ASCII strings, case-insensitive, with keyword abbreviation supported —
  wherever a command name has capitalized letters embedded (e.g. `:MEASure`, `:CONTinuity`),
  those capitalized letters are the minimum abbreviation; the rest is optional. Everything in
  this manual follows that convention in how commands are spelled.
- A command normally begins with a colon (`:`), separating the root keyword from
  lower-level keywords.
- A **query** is formed by appending `?` to the end of a command.
- **Braces `{ }`** with items separated by `|` mean choose exactly one, e.g. `{ON|OFF}`.
- **Triangle brackets `< >`** mark a placeholder to be replaced with an actual value.
- **Square brackets `[ ]`** mark an optional keyword or parameter — if omitted, the
  instrument applies a default, e.g. `STATus:OPERation[:EVENt]?` can be sent as either
  `STATus:OPERation?` or `STATus:OPERation:EVENt?`.
- Parameter types used throughout: **consecutive integer** (whole numbers only — sending a
  decimal where an integer is expected raises an error), **consecutive real number**
  (typically 6 significant digits of precision), **discrete** (one of a fixed enumerated set,
  often a small integer selecting a range/mode), **boolean** (`ON`/`OFF`/`1`/`0`), and
  **ASCII string**.
- **This instrument actually supports three separate command *languages*, selectable at
  runtime:**

  ```text
  CMDSET {RIGOL|AGILENT|FLUKE}
  CMDSET?
  ```

  RIGOL's native command tree (documented in full below) is active by default at power-on.
  Sending `CMDSET AGILENT` or `CMDSET FLUKE` switches the instrument to accept
  Agilent-34401A-compatible or Fluke-45-compatible syntax instead. This manual documents
  only the native RIGOL command set (see "What's excluded and why" for the other two) — but
  if you ever see unfamiliar non-colon-prefixed commands like `conf:volt:dc auto` or
  `calc:aver:max?` in captured traffic or an old script, that's a sign the instrument was left
  in `AGILENT` (or `FLUKE`) mode, not that this manual is wrong; send `CMDSET RIGOL` (or
  power-cycle, since RIGOL mode is the power-on default) to get back to the commands below.

## Command reference

### IEEE 488 Common Commands (Chapter 2)

Standard `*`-prefixed commands, identical in spirit to any SCPI instrument.

| Command | Form | Description |
|---|---|---|
| `*CLS` | set | Resets all Event Registers and clears the Error Queue. |
| `*ESE <enable_value>` / `*ESE?` | set/query | Sets/queries the Event Status Register (ESR) enable mask. `<enable_value>` range: 0–189. |
| `*ESR?` | query | Queries the ESR's current event value. |
| `*IDN?` | query | Queries instrument identification. Returns a comma-separated string: `RIGOL Technologies,DM3058,<serial>,<firmware>` — e.g. `RIGOL Technologies,DM3058,DM3A020080808,99.00.00.00.00.00` (confirmed via the vendor's own worked example; note the model field reads `DM3058` even on a DM3058E unit — the two share one identity string format). |
| `*OPC` / `*OPC?` | set/query | `*OPC` sets ESR bit 0 once the current operation completes; `*OPC?` queries completion and returns `1` when done. |
| `*PSC {0\|1}` / `*PSC?` | set/query | Enables/disables "power-on status clear." `*PSC 0` = registers retain LAST STATE across power-on; `*PSC 1` = registers reset at power-on. |
| `*RST` | set | Resets the instrument to its default state. |
| `*SRE <enable_value>` / `*SRE?` | set/query | Sets/queries the Status Byte Register (STB) enable mask. `<enable_value>` range: 0–188. |
| `*STB?` | query | Queries the STB's current condition value. |
| `*TRG` | set | Issues a trigger while the instrument is in "wait-for-trigger" state. |
| `*TST?` | query | Runs the self-test; returns `0` (pass) or `1` (failure). |
| `*WAI` | set | Blocks until all pending operations complete. |

**Use `*IDN?` as your connectivity smoke test** — it's read-only with a well-known reply
shape, the safest first command on a new connection.

### STATus Commands (Chapter 2)

Set/query the Questionable Status Register and Operation Status Register (separate from the
Event Status Register/Status Byte Register covered by the `*`-commands above).

| Command | Form | Description |
|---|---|---|
| `STATus:OPERation:CONDition?` | query | Queries the Operation Status Register's Condition Register. |
| `STATus:OPERation:ENABle <enable_value>` / `?` | set/query | Sets/queries the Operation Status Register's Enable Register. Range: 0–1841. |
| `STATus:OPERation[:EVENt]?` | query | Queries the Operation Status Register's Event Register. |
| `STATus:PRESet` | set | Resets the Enable Register in both the Operation and Questionable Status Registers. |
| `STATus:QUEStionable:CONDition?` | query | Queries the Questionable Status Register's Condition Register. |
| `STATus:QUEStionable:ENABle <enable_value>` / `?` | set/query | Sets/queries the Questionable Status Register's Enable Register. Range: 0–24375. |
| `STATus:QUEStionable[:EVENt]?` | query | Queries the Questionable Status Register's Event Register. |

**Gotcha:** the vendor guide describes the register *bit layout* only via a referenced
figure ("Figure 2-1, The Status Register diagram") that did not survive text extraction from
the source PDF — if you need specific bit-to-condition mappings (e.g. which bit means
"measurement overload"), consult the figure in the original PDF directly; this manual can
only confirm the command syntax and register names, not each bit's individual meaning.

### General SYSTem Commands (Chapter 2)

Distinct from the RIGOL-specific `:SYSTem` subsystem in Chapter 3 (below) — these are the
instrument-wide basics shared conceptually across all three command languages (RIGOL/
Agilent/Fluke).

| Command | Form | Description |
|---|---|---|
| `SYSTem:BEEPer` | set | Issues a single beep immediately. No effect if the beeper is currently disabled (see next). |
| `SYSTem:BEEPer:STATe {ON\|OFF\|1\|0}` / `?` | set/query | Enables/disables the beeper. Example: `SYSTem:BEEPer:STATe ON`. Gotcha: if you send `SYSTem:BEEPer:STATe OFF`, subsequent `SYSTem:BEEPer` calls are silently ineffective until re-enabled. |
| `SYSTem:ERRor?` | query | Pops the oldest entry off the Error Queue. Returns `<code>,"<description>"`, e.g. `0,"No error"` when the queue is empty. |
| `SYSTem:VERSion?` | query | Queries the instrument's SCPI standard version, e.g. `"1999.0"`. |

### `:CALCulate` Commands (math functions)

Sets up the multimeter's math/statistics layer (relative, dB, dBm, min/max/average
statistics, pass/fail limit testing). All of these operate on whatever measurement function
is currently active (set via `:FUNCtion`, below) — switching the active measurement function
generally resets or invalidates the math state, so apply `:CALCulate` commands *after*
selecting the measurement function you want to analyze.

| Command | Form | Description |
|---|---|---|
| `:CALCulate:FUNCtion {NONE\|REL\|DB\|DBM\|MIN\|MAX\|AVERAGE\|TOTAL\|PF}` / `?` | set/query | Selects which math operation(s) are active. `NONE` disables all. `TOTAL` turns on MIN+MAX+AVERAGE together. Multiple simultaneously-active operations can be reported combined, e.g. `REL+PF`. Default: `NONE`. |
| `:CALCulate:STATistic:MIN?` / `:MAX?` / `:AVERage?` | query | Returns the running min/max/average of the current statistics run. Each is valid **only** when its corresponding operation is enabled via `:CALCulate:FUNCtion`. |
| `:CALCulate:STATistic:COUNt?` | query | Returns how many measurements have been counted into the current statistics run. Resets when the measurement function changes. |
| `:CALCulate:STATistic:STATe {ON\|OFF\|1\|0}` / `?` | set/query | Enables/disables statistics collection outright. Example: `:CALCulate:STATistic:STATe OFF`. |
| `:CALCulate:REL:OFFSet {<range>\|MIN\|MAX\|DEF\|CURR}` / `?` | set/query | Sets the relative-measurement offset. Valid range and default depend on the active measurement type (DC voltage ±1200 V, AC voltage ±900 V, DC current ±12 A, AC current ±12 A, resistance ±1.2e8 Ω, capacitance ±1.2e-2 F, frequency ±1.2e6 Hz — all default to `0`). `CURR` sets the offset to the current live reading. |
| `:CALCulate:REL:STATe {ON\|OFF\|1\|0}` / `?` | set/query | Enables/disables the REL (relative) operation. Example: `:CALCulate:REL:STATe OFF`. |
| `:CALCulate:DB?` | query | Returns the calculated dB value. Valid only when dB operation is enabled. |
| `:CALCulate:DB:REFErence {<range>\|MIN\|MAX\|DEF}` / `?` | set/query | Sets the dB reference. Range −120 to +120 dBm, integer, default `0`. |
| `:CALCulate:DB:STATe {ON\|OFF\|1\|0}` / `?` | set/query | Enables/disables dB operation. |
| `:CALCulate:DBM?` | query | Returns the calculated dBm value. Valid only when dBm operation is enabled. |
| `:CALCulate:DBM:REFErence {<range>\|MIN\|MAX\|DEF}` / `?` | set/query | Sets the dBm reference resistance in Ω. Range 2–8000, integer, default `600`. |
| `:CALCulate:DBM:STATe {ON\|OFF\|1\|0}` / `?` | set/query | Enables/disables dBm operation. |
| `:CALCulate:PF?` | query | Returns the pass/fail (limit-test) result: `PASS`, `HI`, or `LO`. |
| `:CALCulate:PF:LOWEr {<range>\|MIN\|MAX\|DEF}` / `?` | set/query | Sets the lower limit for pass/fail testing. Range/unit varies by measurement type (see the DC/AC voltage/current/resistance/capacitance/period/frequency ranges under `:MEASure`, below); default `0`. |
| `:CALCulate:PF:UPPEr {<range>\|MIN\|MAX\|DEF}` / `?` | set/query | Sets the upper limit for pass/fail testing. Same ranges as `:LOWEr`; default `1`. |
| `:CALCulate:PF:STATe {ON\|OFF\|1\|0}` / `?` | set/query | Enables/disables pass/fail testing. Example: `:CALCulate:PF:STATe OFF`. |

### `:FUNCtion` Commands (primary/main-display measurement function)

Each of these is a bare set command (no parameters) that switches the primary measurement
mode — equivalent to pressing the corresponding front-panel button.

| Command | `:FUNCtion?` reply after sending it |
|---|---|
| `:FUNCtion:VOLTage:DC` | `DCV` |
| `:FUNCtion:VOLTage:AC` | `ACV` |
| `:FUNCtion:CURRent:DC` | `DCI` |
| `:FUNCtion:CURRent:AC` | `ACI` |
| `:FUNCtion:RESistance` (2-wire) | `2WR` |
| `:FUNCtion:FRESistance` (4-wire) | `4WR` |
| `:FUNCtion:FREQuency` | `FREQ` |
| `:FUNCtion:PERiod` | `PERI` |
| `:FUNCtion:CONTinuity` | `CONT` |
| `:FUNCtion:DIODe` | `DIODE` |
| `:FUNCtion:CAPacitance` | `CAP` |

`:FUNCtion?` (query, no arguments needed) returns the currently active function using the
same mnemonics as the right-hand column above. If double-display (vice-display) is active,
this query reports the **main** display's function only — see `:FUNCtion2` below for the
secondary display.

### `:FUNCtion2` Commands (secondary/vice-display measurement function)

Controls the DM3058E's second measurement display, which can show a related measurement
alongside the main one (e.g. frequency alongside AC voltage).

| Command | Form | Description |
|---|---|---|
| `:FUNCtion2?` | query | Returns the vice-display's active function (`DCV`, `ACV`, `DCI`, `ACI`, `2WR`, `CAP`, `4WR`, `FREQ`, `PERI`). Valid only when the vice-display is enabled. |
| `:FUNCtion2:VALUe1?` | query | Returns the main display's measured value. Valid only when the vice-display is enabled. |
| `:FUNCtion2:VALUe2?` | query | Returns the vice-display's measured value. Valid only when the vice-display is enabled. |
| `:FUNCtion2:VOLTage:DC` / `:VOLTage:AC` / `:CURRent:DC` / `:CURRent:AC` / `:FREQuency` / `:PERiod` / `:RESistance` / `:FRESistance` / `:CAPacitance` | set | Enables the vice-display with the named function. **Gotcha:** each of these *constrains* what the main-display function may simultaneously be — e.g. after `:FUNCtion2:VOLTage:DC`, the main display can only be DCV/DCI/ACV/ACI; after `:FUNCtion2:FREQuency`, the main display can only be ACV/FREQUENCY/PERIOD. Check the per-command explanation if you hit an unexpected function-selection failure. |
| `:FUNCtion2:ON?` | query | Queries the current vice-display function/state. |
| `:FUNCtion2:CLEar` | set | Disables the vice-display, returning to single-display mode. |

### `:MEASure` Commands (readings and per-function ranging)

The core measurement subsystem. Each measurement type follows a **consistent four-command
pattern** — this table shows the pattern once, then the per-type specifics:

- **`:MEASure:<type>?`** — query, returns the live measured value in scientific notation
  (e.g. `8.492853e-05`), in the type's natural unit. **Unavailable while the vice-display
  (`:FUNCtion2`) is active** for most types.
- **`:MEASure:<type> {<range>|MIN|MAX|DEF}`** — set command, selects the measurement range
  (a small discrete integer per the tables below) and, for several types, the resolution.
  Setting a range switches the measurement type to "Manual" automatically (see `:MEASure
  {AUTO|MANU}` below).
  Discrete input like `3` and the equivalent named `MIN`/`MAX`/`DEF` alias are interchangeable, e.g. `:MEASure:VOLTage:DC 3` and `:MEASure:VOLTage:DC MAX` are NOT
  necessarily the same value — check each type's table for which integer maps to `MIN`/`MAX`/`DEF`.
- **`:MEASure:<type>:RANGe?`** — query, returns which range index (from the same table) is
  currently active.
- A `:FILTer[:STATe]` sub-command exists for DC voltage and DC current only (see below).

| Command | Form | Description |
|---|---|---|
| `:MEASure?` | query | Returns `TRUE`/`FALSE` — whether new data has been acquired under the current trigger setting. |
| `:MEASure {AUTO\|MANU}` | set | Selects Auto or Manual measurement type. Example: `:MEASure MANU`. |

**DC Voltage** — `:MEASure:VOLTage:DC?` / `:MEASure:VOLTage:DC {<range>|MIN|MAX|DEF}` /
`:MEASure:VOLTage:DC:RANGe?`:

| `<range>` | Range | Resolution |
|---|---|---|
| 0 (MIN) | 200 mV | 100 nV |
| 1 | 2 V | 1 µV |
| 2 (DEF) | 20 V | 10 µV |
| 3 | 200 V | 100 µV |
| 4 (MAX) | 1000 V | 1 mV |

Plus `:MEASure:VOLTage:DC:IMPEdance {10M|10G}` / `?` — sets/queries input impedance; `10G`
(>10 GΩ) is only selectable when the DC voltage range is 200 mV or 2 V. And
`:MEASure:VOLTage:DC:FILTer[:STATe] {ON|OFF|1|0}` / `?` — the AC filter under DC voltage
measurement.

**AC Voltage** — `:MEASure:VOLTage:AC?` / `:MEASure:VOLTage:AC {<range>|MIN|MAX|DEF}` /
`:MEASure:VOLTage:AC:RANGe?`:

| `<range>` | Range |
|---|---|
| 0 (MIN) | 200 mV |
| 1 | 2 V |
| 2 (DEF) | 20 V |
| 3 | 200 V |
| 4 (MAX) | 750 V |

**DC Current** — `:MEASure:CURRent:DC?` / `:MEASure:CURRent:DC {<range>|MIN|MAX|DEF}` /
`:MEASure:CURRent:DC:RANGe?`:

| `<range>` | Range | Resolution |
|---|---|---|
| 0 (MIN) | 200 µA | 1 nA |
| 1 | 2 mA | 10 nA |
| 2 | 20 mA | 100 nA |
| 3 (DEF) | 200 mA | 1 µA |
| 4 | 2 A | 10 µA |
| 5 (MAX) | 10 A | 100 µA |

Plus `:MEASure:CURRent:DC:FILTer[:STATe] {ON|OFF|1|0}` / `?`, same semantics as the DC
voltage filter above.

**AC Current** — `:MEASure:CURRent:AC?` / `:MEASure:CURRent:AC {<range>|MIN|MAX|DEF}` /
`:MEASure:CURRent:AC:RANGe?`:

| `<range>` | Range |
|---|---|
| 0 (MIN) | 20 mA |
| 1 (DEF) | 200 mA |
| 2 | 2 A |
| 3 (MAX) | 10 A |

**2-Wire Resistance** — `:MEASure:RESistance?` / `:MEASure:RESistance {<range>|MIN|MAX|DEF}`
/ `:MEASure:RESistance:RANGe?` (4-wire is identical in range/shape — see next):

| `<range>` | Range |
|---|---|
| 0 (MIN) | 200 Ω |
| 1 | 2 kΩ |
| 2 | 20 kΩ |
| 3 (DEF) | 200 kΩ |
| 4 | 1 MΩ |
| 5 | 10 MΩ |
| 6 (MAX) | 100 MΩ |

**4-Wire Resistance** — `:MEASure:FRESistance?` / `:MEASure:FRESistance
{<range>|MIN|MAX|DEF}` / `:MEASure:FRESistance:RANGe?` — same range table as 2-wire above.

**Frequency** — `:MEASure:FREQuency?` / `:MEASure:FREQuency {<range>|MIN|MAX|DEF}` /
`:MEASure:FREQuency:RANGe?`. Measurable range 20 Hz–1 MHz. The `<range>` parameter here
actually selects the **input voltage range** (reuses the AC-voltage range table above, 0–4),
not a frequency range — a quirk worth flagging explicitly since the name suggests otherwise.

**Period** — `:MEASure:PERiod?` / `:MEASure:PERiod {<range>|MIN|MAX|DEF}` /
`:MEASure:PERiod:RANGe?`. Measurable range 1 s–50 ms. Same quirk as Frequency: `<range>`
selects input voltage range (AC-voltage table), not a period range.

**Continuity** — `:MEASure:CONTinuity?` (returns measured resistance under continuity test,
e.g. `8.888000e+03` Ω) / `:MEASure:CONTinuity {<range>|MIN|MAX|DEF}` — sets the resistance
threshold in Ω, a consecutive integer from 1 to 2000, default 10. Example:
`:MEASure:CONTinuity 1000` sets a 1 kΩ threshold.

**Diode** — `:MEASure:DIODe?` — returns the measured diode forward voltage. Beep threshold
is 1 V ≤ Vmeasured ≤ 2.4 V by default (fixed, not user-configurable via this command).

**Capacitance** — `:MEASure:CAPacitance?` / `:MEASure:CAPacitance {<range>|MIN|MAX|DEF}` /
`:MEASure:CAPacitance:RANGe?`:

| `<range>` | Range |
|---|---|
| 0 (MIN) | 2 nF |
| 1 | 20 nF |
| 2 (DEF) | 200 nF |
| 3 | 2 µF |
| 4 | 200 µF |
| 5 (MAX) | 10000 µF |

### `:RATE` Commands (measurement speed)

All follow the identical shape: `:RATE:<type> {F|M|S}` / `?`, where `F` = Fast (123
readings/s, 50 Hz refresh), `M` = Medium (20 readings/s, 20 Hz refresh), `S` = Slow (2.5
readings/s, 2.5 Hz refresh). Each query is valid only when its corresponding measurement
function is currently active.

| Command | Applies to |
|---|---|
| `:RATE:VOLTage:DC {F\|M\|S}` / `?` | DC voltage |
| `:RATE:VOLTage:AC {F\|M\|S}` / `?` | AC voltage |
| `:RATE:CURRent:DC {F\|M\|S}` / `?` | DC current |
| `:RATE:CURRent:AC {F\|M\|S}` / `?` | AC current |
| `:RATE:RESistance {F\|M\|S}` / `?` | 2-wire resistance |
| `:RATE:FRESistance {F\|M\|S}` / `?` | 4-wire resistance |
| `:RATE:SENSor {M\|S}` | Sensor measurement — note: **no Fast option** here, only Medium/Slow. |

Example: `:RATE:VOLTage:DC M` sets DC voltage measurement to Medium rate.

### `:SYSTem` Commands (RIGOL-specific, Chapter 3)

| Command | Form | Description |
|---|---|---|
| `:SYSTem:CONFigure:POWESwitch {ON\|OFF\|1\|0}` | set | Controls whether the instrument requires a manual front-panel power-switch press after mains power is applied. `OFF`/`0` = auto-starts on power application; `ON`/`1` = waits for the front-panel switch. |
| `:SYSTem:CONFigure:POWEron {LAST\|DEF}` | set | Sets power-on state to the last-used configuration (`LAST`) or factory defaults (`DEF`). |
| `:SYSTem:CONFigure:DEFault` | set | Resets system configuration to factory defaults immediately. |
| `:SYSTem:LANGuage {CHINESE\|ENGLISH}` / `?` | set/query | Sets/queries the front-panel UI language. |
| `:SYSTem:FORMat:DECImal {COMMA\|DOT}` / `?` | set/query | Sets/queries the decimal-point display character. **Gotcha:** the vendor guide itself warns "this command causes easily the format changes of data delimiter, please use it modestly" — changing this can also shift how returned numeric strings are delimited, so avoid toggling it mid-script. |
| `:SYSTem:FORMat:SEPArate {ON\|NONE\|SPACE}` / `?` | set/query | Sets/queries the delimiter used in displayed system data. `ON` = default `,`, `NONE` = no delimiter, `SPACE` = space-delimited. |
| `:SYSTem:DISPlay:BRIGht <value>` / `?` | set/query | Screen brightness, integer 0–32, default 22. |
| `:SYSTem:DISPlay:CONTrast <value>` / `?` | set/query | Screen contrast, integer 0–32, default 19. |
| `:SYSTem:DISPlay:INVErt` | set | Toggles inverted screen display. |

### `:TRIGger` Commands

| Command | Form | Description |
|---|---|---|
| `:TRIGger:SOURce {AUTO\|SINGLE\|EXT}` / `?` | set/query | Selects trigger source: automatic, single (software-triggered), or external (hardware line). |
| `:TRIGger:AUTO:INTErval <value>` / `?` | set/query | Sets the auto-trigger interval in ms. Valid range depends on the active `:RATE` — Fast: 8–2000 ms (default 8); Medium: 50–2000 ms (default 50); Slow: 400–2000 ms (default 400, and Slow is the instrument's overall default rate). |
| `:TRIGger:AUTO:HOLD {ON\|OFF\|1\|0}` / `?` | set/query | Enables/disables the "hold" function under auto trigger (holds the display when the reading is stable). |
| `:TRIGger:AUTO:HOLD:SENSitivity {<value>\|MIN\|MAX\|DEF}` / `?` | set/query | Sets hold sensitivity: `0`/MIN=0.01%, `1`=0.1%, `2`/DEF=1%, `3`/MAX=10%. |
| `:TRIGger:SINGle {<value>\|MIN\|MAX\|DEF}` / `?` | set/query | Sets the sample count for single-trigger mode. Integer 1–2000, default 1. |
| `:TRIGger:SINGle:TRIGgered` | set | Fires one manual trigger — equivalent to the front-panel single-trigger button. |
| `:TRIGger:EXT {RISE\|FALL\|HIGH\|LOW}` / `?` | set/query | Sets external trigger type (edge or level). Default: `RISE`. |
| `:TRIGger:VMComplete:POLAr {POS\|NEG}` / `?` | set/query | Sets the "voltage measurement complete" (VMC) output signal polarity. Default: `POS`. |
| `:TRIGger:VMComplete:PULSewidth <value>` / `?` | set/query | Sets VMC output pulse width in ms. Default 100 ms. **Gotcha:** valid range depends on the active `:RATE` — Slow: 1–399 ms, Medium: 1–49 ms, Fast: 1–7 ms — so changing measurement rate can silently invalidate a previously-set pulse width. |

### `:UTILity` Commands (interface & misc configuration)

Only the entries relevant to USB/RS-232 are detailed here — see "What's excluded and why"
for the LAN/GPIB entries this instrument doesn't support.

| Command | Form | Description |
|---|---|---|
| `:UTILity:INTErface:RS232:BAUD <value>` / `?` | set/query | Sets/queries the RS-232 baud rate. Discrete values: `1200\|2400\|4800\|9600\|19200\|38400\|57600\|115200`. |
| `:UTILity:INTErface:RS232:PARIty {NONE\|ODD\|EVEN}` / `?` | set/query | Sets/queries parity **and, coupled with it, the data-bit count** — this is one combined setting, not two independent ones. `NONE` = no parity check, 8 data bits. `ODD` = odd parity, 7 data bits. `EVEN` = even parity, 7 data bits. Query returns `NONE8BITS`, `ODD7BITS`, or `EVEN7BITS` — note the reply format bundles both pieces of information into one token; parse accordingly. |

The `:UTILity` section header itself warns: "Please make sure that communication interface
has been connected stably to avoid errors or abnormal phenomena" — i.e. don't reconfigure
the very interface you're issuing the command over without expecting the session to drop
(e.g. changing RS-232 baud rate via an RS-232 connection will desync your terminal from the
new rate immediately after the command is accepted).

## Worked end-to-end example: DC voltage max-statistics measurement

Adapted from the vendor guide's own "Example 1: Reading Statistic" (native RIGOL command
form) — resets the instrument, confirms identity, enables DC voltage measurement, and
tracks a running maximum:

```text
*RST                          (reset the instrument to a known state)
CMDSET RIGOL                  (explicitly select the native RIGOL command language)

*IDN?
  -> RIGOL Technologies,DM3058,DM3A020080808,99.00.00.00.00.00
                               (confirms connectivity and instrument identity)

:FUNCtion:VOLTage:DC          (enable DC voltage measurement)
:MEASure AUTO                 (use auto-ranging)

:CALCulate:FUNCtion MAX       (enable running-maximum statistics)

                               (... let the instrument take readings for a while ...)

:CALCulate:STATistic:MAX?
  -> 5.000064e-02              (current maximum DC voltage observed, in volts)

:CALCulate:STATistic:COUNt?
  -> 252                       (number of auto-measurements counted so far)

:CALCulate:FUNCtion NONE      (exit the math/statistics function when done)
```

Note the `*IDN?` reply reports the model as `DM3058` even when run against a DM3058E unit —
this is expected per the vendor guide's own example and is not a sign of misconfiguration.

## Common gotchas

- **Three command languages share one instrument.** `CMDSET RIGOL|AGILENT|FLUKE` switches
  the entire accepted syntax. If a script written against this manual stops working, check
  `CMDSET?` first — something may have left the instrument in Agilent or Fluke compatibility
  mode. RIGOL mode is the power-on default, so a `*RST`/power-cycle also resets this.
- **Setting a `:MEASure:<type>` range switches you into Manual mode automatically**, silently
  overriding whatever `:MEASure {AUTO|MANU}` state was active — if your script relies on
  auto-ranging, re-issue `:MEASure AUTO` after any explicit range-setting call, not before.
- **`:FUNCtion2` (vice-display) constrains what the main display can show**, and vice versa —
  the valid combinations are asymmetric and type-specific (see the `:FUNCtion2` table above).
  Trying to select an incompatible pairing will not necessarily error loudly; check
  `:FUNCtion?` / `:FUNCtion2?` after switching if the display doesn't show what you expect.
  Also, most `:MEASure:<type>?` value queries are **unavailable while the vice-display is
  active** — disable it (`:FUNCtion2:CLEar`) if you need the single-display query behavior.
- **`:MEASure:FREQuency` and `:MEASure:PERiod`'s `<range>` parameter selects an input
  *voltage* range, not a frequency/period range** — easy to misread from the command name
  alone; both reuse the AC-voltage range table (0–4, 200 mV–750 V).
- **Sending `:UTILity:INTErface:RS232:BAUD` over the RS-232 connection you're reconfiguring
  breaks that connection as soon as the instrument applies the new rate** — don't expect a
  reply over the old baud rate; reconnect your terminal/script at the new rate afterward.
- **The Error Queue (`SYSTem:ERRor?`) is FIFO and must be drained one entry at a time.** A
  single query pops the oldest error; if you suspect multiple things went wrong, poll it
  repeatedly until it returns `0,"No error"`.

## What's excluded and why

- **GPIB is excluded — hardware absence, not a scope choice.** The vendor guide states
  plainly that "The GPIB and LAN interfaces are only supported by DM3058" (the non-E base
  model). The DM3058E has no GPIB port at all, so `:UTILity:INTErface:GPIB:ADDRess` and any
  GPIB-specific framing are out of scope here.
- **LAN, and the entire `:LXI` command subsystem, are excluded for the same reason.** The
  guide's own note on the `:LXI` section reads: "`:LXI` commands are only applicable to
  DM3058 because DM3058E has no LAN interface." That covers `:LXI:IDENtify[:STATe]`,
  `:LXI:MDNS:*`, `:LXI:RESet`, `:LXI:RESTart`, and the LAN-configuration entries under
  `:UTILity:INTErface:LAN:*` (`DHCP`, `AUTOip`, `MANUip`, `IP`, `MASK`, `GATEway`, `DNS`) — all
  present in the source document (since it covers both models together) but inapplicable to
  this instrument.
- **Chapter 4 "Compatible Agilent Commands" is excluded from per-command treatment — a scope
  choice, not a hardware limitation.** This is a full alternate command-language layer (the
  guide documents it as compatible with the Agilent 34401A specifically) that the instrument
  accepts once you send `CMDSET AGILENT`. It exists and works on this hardware; it just isn't
  needed for native programmatic control and wasn't part of what was asked for. If a future
  need arises to interoperate with existing Agilent-34401A-targeted scripts unchanged, that
  chapter (source pages 4-1 through 4-52, covering `CALCulate`, `CONFigure`, `DATA`,
  `DISPlay`, `FETCh?`, `INITiate`, `INPut`, `MEASure`, `READ?`, `ROUTe`, `SENSe`, `SAMPle`, and
  `TRIGger` commands in Agilent's own syntax) would need a dedicated research/documentation
  pass — it is not simply "the same commands with periods instead of colons."
- **Chapter 5 "Compatible Fluke Commands" is excluded for the identical reason** — a Fluke-45
  -compatible alternate syntax (Function, Function Regulation, Range and Rate, Measurement,
  Compare, Trigger, Format, and Reading command groups, source pages 5-1 through 5-24),
  reachable via `CMDSET FLUKE`, out of scope unless specifically needed later.
- **The Appendix's "Incompatible Agilent/Fluke Commands" lists are excluded** since they only
  matter to someone actively using those two alternate command languages, which this manual
  doesn't cover in detail.
- **Status-register bit-level layout (Figure 2-1) could not be extracted** from the source
  PDF's text conversion — the command syntax for reading/writing the registers is fully
  documented above, but the specific bit-to-condition mapping is not; consult the original
  vendor PDF's figure directly if that level of detail is needed.
