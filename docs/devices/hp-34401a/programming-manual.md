# HP/Agilent 34401A — Programming Manual (GPIB / RS-232)

6½-digit digital multimeter. This manual covers the native SCPI command set reachable
over **GPIB (HP-IB/IEEE-488)** and **RS-232** — both interfaces are built into this
instrument natively; no add-on module is required for either, unlike several other
instruments documented in this repo.

**Naming note:** this instrument was originally sold as the "HP 34401A." Hewlett-Packard's
test & measurement division was later spun off as Agilent Technologies (and still later
became part of Keysight Technologies), so the current canonical vendor document is branded
"Agilent 34401A." It is the same product and the same document lineage — use either name to
find this manual.

## Source document

**Agilent 34401A 6½ Digit Multimeter User's Guide**, Manual Part Number **34401-90004**,
**Seventh Edition, August 2007**, Agilent Technologies, Inc. (Originally published by
Hewlett-Packard; copyright notice reads "Agilent Technologies, Inc. 1991-2007.") This is
the current, canonical vendor source — it matches what Keysight's own product-support page
lists for this instrument today.

This is a **combined document**, like several others in this repo: general front-panel
operation and the full remote-command reference live in one manual, not separate documents.
Chapter 4, "Remote Interface Reference," is the primary command reference used throughout
this manual; Chapter 3, "Features and Functions," is used for physical-connection and
front-panel-menu material; Chapter 6, "Application Programs," supplies the worked examples.

## Before you start

### Physical connection

The rear panel has both a **GPIB (IEEE-488) interface connector** and an **RS-232 interface
connector (9-pin, DB-9)**. **Only one interface can be enabled at a time** — selecting one
from the front-panel I/O MENU disables the other. The instrument ships with GPIB selected by
default.

**GPIB:** standard IEEE-488 cabling to your bus controller. Each device on the bus needs a
unique address (0–31); the factory default is **22**. Avoid using your controller's own
address for any instrument (Agilent controllers commonly use address 21).

**RS-232:** the multimeter is a **DTE (Data Terminal Equipment)** device, like most PCs and
terminals, so you need a **DTE-to-DTE cable** — also called a null-modem, modem-eliminator,
or crossover cable — not a straight-through cable. Connector pinout matters as much as the
physical connector shape (9-pin vs. 25-pin): a cable with the right connectors but wrong
internal wiring will not work. Agilent sold a **34398A Cable Kit** (null-modem cables for
DB-9 and DB-25) and a **34399A Adapter Kit** (for less common configurations) for this
purpose; any equivalent null-modem cable/adapter works.

> **Caution (from the source manual):** do not use the RS-232 interface if the multimeter is
> configured to output pass/fail signals on pins 1 and 9 — internal RS-232 circuitry may be
> damaged.

### Front-panel I/O MENU (interface configuration)

All remote-interface configuration is done from the front panel — none of it is available as
a command from the interface you're configuring (for obvious bootstrapping reasons: you can't
use RS-232 to tell the instrument to switch to RS-232). Menu path: **Shift, Menu On/Off → E:
I/O MENU**.

| I/O MENU item | Sets | Factory default |
|---|---|---|
| 1: HP-IB ADDR | GPIB bus address, 0–31 | 22 |
| 2: INTERFACE | GPIB / RS-232 (mutually exclusive) | GPIB |
| 3: BAUD RATE | RS-232 baud rate: 300, 600, 1200, 2400, 4800, 9600 | 9600 |
| 4: PARITY | RS-232 parity/data-bits: None (8 data bits), Even (7 data bits), Odd (7 data bits) | Even / 7 data bits |
| 5: LANGUAGE | SCPI / Agilent 3478A / Fluke 8840A | SCPI |

All of these settings are stored in non-volatile memory and survive power cycles and remote
interface resets.

**RS-232 framing is otherwise fixed** (not configurable): 1 start bit, 2 stop bits — always.
Only baud rate and parity/data-bits are selectable.

**Interface/language restriction — relevant to this manual's scope:** the Agilent 3478A and
Fluke 8840A/8842A alternate command languages are **not supported over RS-232 at all**, only
over GPIB. RS-232 always speaks SCPI. (This manual documents SCPI only regardless — see
"What's excluded and why" — but it's worth knowing this restriction exists even if you never
touch the alternate languages.)

### Calibration security (context for the Calibration Commands section below)

The instrument ships **secured** against calibration, with a factory-default security code
of `"HP034401"`. You don't need to touch this for normal measurement programming — it's
called out here because it affects the Calibration Commands section further down.

## Command syntax conventions

- **Tree structure.** Commands are grouped into subsystems under a root keyword, with `:`
  separating each level, e.g. `SENSe:VOLTage:DC:RANGe`. A portion of the tree:
  ```
  SENSe:
      VOLTage:
          DC:RANGe {<range>|MINimum|MAXimum}
      FREQuency:
          VOLTage:RANGe {<range>|MINimum|MAXimum}
      DETector:
          BANDwidth {3|20|200|MINimum|MAXimum}
  ```
- **Case-insensitivity and abbreviation.** Commands are shown in this manual (and the
  vendor source) as a mix of upper- and lower-case letters — the upper-case letters are the
  minimum abbreviated form. `VOLT` and `VOLTAGE` are both valid; `VOL` or `VOLTAG` are not
  (they don't match either the abbreviated or full spelling exactly). Case itself doesn't
  matter — `volt`, `Volt`, and `VOLT` are all equivalent.
- **Symbol conventions:** `{ }` encloses parameter choices (not sent literally); `|`
  separates alternatives within braces; `< >` marks a value you must substitute (not sent
  literally); `[ ]` marks an optional keyword or parameter — if omitted, the instrument uses
  a default.
- **Separators:** a space separates a command keyword from its parameter. A comma separates
  multiple parameters to the same command (`CONF:VOLT:DC 10, 0.003`). A semicolon chains
  multiple commands within the *same* subsystem without repeating the subsystem root
  (`TRIG:DELAY 1; COUNT 10` is equivalent to two separate `TRIG:DELAY 1` / `TRIG:COUNT 10`
  commands). To chain commands from **different** subsystems in one line, use a semicolon
  *and* a leading colon on the next command: `SAMP:COUN 10;:TRIG:SOUR EXT` — omitting either
  the colon or the semicolon here is an error.
- **MIN / MAX / DEFault.** Most numeric parameters accept `MINimum` or `MAXimum` in place of
  a literal value to select that command's extreme, and many `MEASure?`/`CONFigure` commands
  additionally accept `DEFault` for range (meaning autorange).
- **Command terminators.** A command string must end with a `<new line>` character. The GPIB
  EOI (end-or-identify) bus message is treated as equivalent to `<new line>`. A `<carriage
  return><new line>` pair is also accepted. Terminating a command string always resets the
  current SCPI path back to the root level (so the next command doesn't inherit any partial
  subsystem path from a semicolon-chained command).
- **IEEE-488.2 common commands** always begin with `*`, are four to five characters, and may
  take a parameter separated by a space, e.g. `*ESE 32`. Chain them with `;` like any other
  command: `*RST; *CLS; *ESE 32; *OPC?`.
- **Parameter types:**
  - *Numeric* — standard decimal notation, optional sign/decimal point/scientific notation,
    plus engineering-unit suffixes (`M`, `K`, `u`, etc.) and the `MIN`/`MAX`/`DEF` keywords.
    Values outside what a command accepts are rounded to the nearest valid value rather than
    rejected.
  - *Discrete* — a fixed set of keyword choices (e.g. `BUS|IMMediate|EXTernal`), same
    abbreviation rules as commands. Query responses always come back as the short (upper-case)
    form.
  - *Boolean* — accepts `ON`/`1` or `OFF`/`0` when setting; a query always returns `"0"` or
    `"1"`.
  - *String* — enclosed in matching single or double quotes; include a literal quote
    character by doubling it.
- **Querying.** Append `?` to a command to query its current value. Many commands also accept
  `? MIN` / `? MAX` to query the extreme rather than the current setting.
  > **Caution (from the source manual):** don't send a second query before reading the first
  > query's response — you may get a mix of the first response's tail and the second
  > response. If you can't avoid this, send a device clear before the second query.
- **Output data formats:** a non-reading query returns an ASCII string up to 80 characters.
  A reading (single or multiple, comma-separated) is returned as `SD.DDDDDDDDESDD`, terminated
  with `<new line>` over GPIB or `<carriage return><new line>` over RS-232 — note the RS-232
  terminator difference, which matters if you're writing your own line-reader.

## Command reference

### The `MEASure?` and `CONFigure` Commands

The source manual itself contrasts three levels of control, and this distinction is genuinely
useful for deciding which to reach for:

1. **`MEASure?`** — the simplest: one command presets everything sensible for the requested
   function/range/resolution and immediately takes and returns a reading. No flexibility
   beyond function/range/resolution; every other parameter (AC filter, autozero, trigger
   count, etc.) is preset to a fixed default (table below). Equivalent to `CONFigure` followed
   immediately by `READ?`.
2. **`CONFigure`** — same presets as `MEASure?`, but does **not** trigger a measurement,
   leaving you free to tweak lower-level `SENSe`/`CALCulate`/`TRIGger` settings before you
   trigger with `INITiate`/`READ?`.
3. **Low-level `SENSe`/`CALCulate`/`TRIGger`/`INPut` commands** — full manual control, no
   presets applied. Use `SENSe:FUNCtion` to change the measurement function without going
   through `MEASure?`/`CONFigure` at all.

**`MEASure?` / `CONFigure` preset state** (applied automatically, not otherwise settable via
these commands):

| Setting | Preset value |
|---|---|
| AC filter (`DET:BAND`) | 20 Hz (medium) |
| Autozero (`ZERO:AUTO`) | OFF if resolution → NPLC < 1; ON if NPLC ≥ 1 |
| Input resistance (`INP:IMP:AUTO`) | OFF (fixed 10 MΩ on all DC volts ranges) |
| Samples per trigger (`SAMP:COUN`) | 1 |
| Trigger count (`TRIG:COUN`) | 1 |
| Trigger delay (`TRIG:DEL`) | Automatic |
| Trigger source (`TRIG:SOUR`) | Immediate |
| Math function | OFF |

- **`MEASure:VOLTage:DC? {<range>|MIN|MAX|DEF},{<resolution>|MIN|MAX|DEF}`** — and the
  parallel forms `MEASure:VOLTage:DC:RATio?`, `:VOLTage:AC?`, `:CURRent:DC?`, `:CURRent:AC?`,
  `:RESistance?` (2-wire ohms), `:FRESistance?` (4-wire ohms), `:FREQuency?`, `:PERiod?`,
  `:CONTinuity?`, `:DIODe?` — preset and immediately take one reading of the named type, sent
  to the output buffer. `range` DEF selects autoranging. `resolution` DEF is 5½ digits slow
  (10 PLC); you must specify a range to use the resolution parameter at all.
  - Gotcha: for AC voltage/current, resolution is actually fixed at 6½ digits regardless of
    what you specify — the parameter only affects the *front-panel display*.
  - Gotcha: frequency/period measurements use one fixed "range" internally covering their
    whole input band (3 Hz–300 kHz / 0.33 s–3.3 µs) — the range parameter only affects
    resolution, not an actual range switch. With no input signal, both return `"0"`.
  - `:CONTinuity?`/`:DIODe?` take no range/resolution arguments — both are fixed (1 kΩ / 5½
    digits for continuity; 1 Vdc with 1 mA source / 5½ digits for diode).
- **`CONFigure:VOLTage:DC {...}`** and the same parallel forms as `MEASure:*?` above (minus
  the `?`) — identical presets, but does not trigger a measurement.
- **`CONFigure?`** — query the present configuration, returns a quoted string.

### Measurement Configuration Commands

Low-level configuration, used either standalone or to fine-tune after a `CONFigure`.

- **`[SENSe:]FUNCtion "<function>"`** — select the measurement function. The function name
  must be quoted: `FUNC "VOLT:DC"`. Valid strings: `VOLTage:DC`, `VOLTage:DC:RATio`,
  `VOLTage:AC`, `CURRent:DC`, `CURRent:AC`, `RESistance` (2-wire), `FRESistance` (4-wire),
  `FREQuency`, `PERiod`, `CONTinuity`, `DIODe`.
  - `[SENSe:]FUNCtion?` — query, returns a quoted string.
- **`<function>:RANGe {<range>|MINimum|MAXimum}`** / **`? [MINimum|MAXimum]`** — select the
  range for the currently selected function. For frequency/period, ranging applies to the
  *input voltage*, not the measured frequency (use `FREQuency:VOLTage:RANGe` /
  `PERiod:VOLTage:RANGe`). [Stored in volatile memory]
- **`<function>:RANGe:AUTO {OFF|ON}`** / **`?`** — enable/disable autoranging. Autorange
  thresholds: down-ranges below 10% of range, up-ranges above 120% of range. Query returns
  `"0"`/`"1"`. [Stored in volatile memory]
- **`<function>:RESolution {<resolution>|MINimum|MAXimum}`** / **`? [MINimum|MAXimum]`** —
  set resolution in the *same units as the measurement*, not digit count (not valid for
  frequency, period, or ratio). [Stored in volatile memory]
- **`<function>:NPLCycles {0.02|0.2|1|10|100|MINimum|MAXimum}`** / **`? [MINimum|MAXimum]`** —
  integration time in power-line cycles (default 10 PLC). Valid only for DC volts, ratio, DC
  current, 2-wire ohms, 4-wire ohms. [Stored in volatile memory]
- **`FREQuency:APERture {0.01|0.1|1|MINimum|MAXimum}`** / **`PERiod:APERture {...}`** / query
  forms — gate/aperture time in seconds for frequency/period measurements (default 0.1 s =
  5½ digits; 0.01 s = 4½ digits; 1 s = 6½ digits). [Stored in volatile memory]
- **`[SENSe:]DETector:BANDwidth {3|20|200|MINimum|MAXimum}`** / **`?`** — lowest expected
  input frequency, used to select the slow/medium(default)/fast AC filter. Query returns one
  of `"+3.000000E+00"`, `"+2.000000E+01"`, `"+2.000000E+02"`. [Stored in volatile memory]
- **`[SENSe:]ZERO:AUTO {OFF|ONCE|ON}`** / **`?`** — autozero mode (default ON). `OFF` doesn't
  take a new zero measurement until the next wait-for-trigger transition; `ONCE` forces an
  immediate zero measurement. Query returns `"0"` (OFF or ONCE) / `"1"` (ON). [Stored in
  volatile memory]
- **`INPut:IMPedance:AUTO {OFF|ON}`** / **`?`** — DC voltage input resistance mode. `OFF`
  (default): fixed 10 MΩ on all ranges. `ON`: >10 GΩ on the 100 mV/1 V/10 V ranges. [Stored
  in volatile memory]
- **`ROUTe:TERMinals?`** — query only: which input terminals are active, `"FRON"` or `"REAR"`.

### Math Operation Commands

Only one math operation can be enabled at a time; changing function disables it. A math
operation that's incompatible with the newly selected measurement function triggers a
"Settings conflict" error and math is turned off.

- **`CALCulate:FUNCtion {NULL|DB|DBM|AVERage|LIMit}`** / **`?`** — select the math function
  (default `NULL`). [Stored in volatile memory]
- **`CALCulate:STATe {OFF|ON}`** / **`?`** — enable/disable the selected math function.
  [Stored in volatile memory] Gotcha: for `NULL` and `DB`, you must enable the math operation
  (`STATe ON`) *before* writing their registers (`NULL:OFFSet`/`DB:REFerence`).
- **`CALCulate:AVERage:MINimum?`** / **`:MAXimum?`** / **`:AVERage?`** / **`:COUNt?`** —
  query-only: min-max operation results. Cleared when min-max is (re-)enabled, on power
  cycle, or on remote interface reset. [Stored in volatile memory]
- **`CALCulate:NULL:OFFSet {<value>|MINimum|MAXimum}`** / **`? [MINimum|MAXimum]`** — the null
  register value, ±120% of the present function's highest range. [Stored in volatile memory]
- **`CALCulate:DB:REFerence {<value>|MINimum|MAXimum}`** / **`? [MINimum|MAXimum]`** — dB
  relative register, 0 to ±200 dBm. [Stored in volatile memory]
- **`CALCulate:DBM:REFerence {<value>|MINimum|MAXimum}`** / **`? [MINimum|MAXimum]`** — dBm
  reference resistance; must be one of 50, 75, 93, 110, 124, 125, 135, 150, 250, 300, 500,
  600, 800, 900, 1000, 1200, or 8000 Ω. [Stored in **non-volatile** memory — unlike most of
  this section]
- **`CALCulate:LIMit:LOWer {<value>|MINimum|MAXimum}`** / **`:LIMit:UPPer {...}`** / query
  forms — limit-test bounds, ±120% of the present function's highest range. [Stored in
  volatile memory]
- **`DATA:FEED RDG_STORE, {"CALCulate"|""}`** / **`DATA:FEED?`** — whether readings taken via
  `INITiate` are stored in internal memory (default, `"CALCulate"`) or discarded (`""`).
  Useful with min-max when you only want the running average, not every point. `MEASure?`/
  `CONFigure` always select `"CALC"`. Gotcha: with memory disabled, `FETCh?` errors out since
  there's nothing to fetch.

### Triggering Commands

The triggering model is a state machine: **Idle → (Initiate) → Wait-for-Trigger → (trigger
satisfied) → Delay → Sample**. You must configure the instrument, select a trigger source,
and then explicitly move it from Idle to Wait-for-Trigger before any trigger — including a
hardware pulse on the Ext Trig terminal — will be accepted. This differs from front-panel
operation, where the instrument is effectively always in Wait-for-Trigger.

- **`INITiate`** — Idle → Wait-for-Trigger. Readings go to **internal memory** (up to 512),
  retrieved later with `FETCh?`. After `INITiate`, no further commands are accepted until the
  measurement sequence completes — *except*, if `TRIGger:SOURce BUS`, the instrument still
  accepts `*TRG` or an IEEE-488 Group Execute Trigger. Firmware Revision 2+ lets you skip
  memory storage via `DATA:FEED RDG_STORE, ""` (see Math Operation Commands above).
- **`READ?`** — Idle → Wait-for-Trigger, but readings go straight to the **output buffer**
  instead of internal memory. Equivalent to `INITiate` immediately followed by `FETCh?`,
  except nothing is buffered internally along the way.
- **`TRIGger:SOURce {BUS|IMMediate|EXTernal}`** / **`?`** — trigger source: a software (bus)
  trigger, an internal immediate trigger (default), or a hardware pulse on the rear-panel Ext
  Trig terminal. Query returns `"BUS"`/`"IMM"`/`"EXT"`. [Stored in volatile memory]
- **`TRIGger:DELay {<seconds>|MINimum|MAXimum}`** / **`? [MINimum|MAXimum]`** — delay between
  trigger and each sample, 0–3600 s. If unset, an automatic delay (based on function/range/
  integration time/AC filter) is used instead. [Stored in volatile memory]
- **`TRIGger:DELay:AUTO {OFF|ON}`** / **`?`** — enable/disable the automatic delay; setting an
  explicit `TRIGger:DELay` value turns this off automatically. [Stored in volatile memory]
- **`SAMPle:COUNt {<value>|MINimum|MAXimum}`** / **`? [MINimum|MAXimum]`** — readings per
  trigger, 1–50,000. [Stored in volatile memory]
- **`TRIGger:COUNt {<value>|MINimum|MAXimum|INFinite}`** / **`? [MINimum|MAXimum]`** —
  triggers accepted before returning to Idle, 1–50,000, or `INFinite` (requires a device
  clear to return to Idle). A query with an infinite count set returns `"9.90000000E+37"`.
  Ignored while in local (front-panel) operation. [Stored in volatile memory]

### System-Related Commands

- **`FETCh?`** — transfer readings already in internal memory (placed there by `INITiate`) to
  the output buffer.
- **`READ?`** — see Triggering Commands above.
- **`DISPlay {OFF|ON}`** / **`?`** — front-panel display on/off. [Stored in volatile memory]
- **`DISPlay:TEXT <quoted string>`** / **`?`** / **`DISPlay:TEXT:CLEar`** — show up to 12
  characters of custom text on the front panel (longer strings are truncated). [Stored in
  volatile memory]
- **`SYSTem:BEEPer`** — issue one beep immediately (no parameter).
- **`SYSTem:BEEPer:STATe {OFF|ON}`** / **`?`** — enable/disable the beeper generally (min-max
  found, reading hold captured, limit exceeded, diode forward-biased). [Stored in
  **non-volatile** memory]
- **`SYSTem:ERRor?`** — pop one error off the FIFO error queue (up to 20 stored, 80 chars
  each). See "Common gotchas" for queue behavior.
- **`SYSTem:VERSion?`** — query the SCPI version this firmware conforms to.
- **`DATA:POINts?`** — query how many readings are currently in internal memory.
- **`*RST`** — reset to power-on configuration.
- **`*TST?`** — run a complete self-test; returns `"0"` (pass) or `"1"` (fail).
- **`*IDN?`** — identification string. Gotcha from the source manual: dimension your string
  variable for at least 35 characters when reading this over an older BASIC-style interface.

### The SCPI Status Model

Standard three-register-group SCPI status system: the **Status Byte** (summary register),
the **Standard Event** register, and the **Questionable Data** register. Each of the latter
two is actually a pair — an **event register** (read-only, latching: once a bit is set it
stays set until read/cleared, unaffected by `*RST`/device clear) and an **enable register**
(read/write, ORs selected event bits up into the corresponding Status Byte summary bit).

**Status Byte register** (query via serial poll or `*STB?`):

| Bit | Decimal | Meaning |
|---|---|---|
| 0–2 | — | Not used, always 0 |
| 3 | 8 | Questionable Data: one or more enabled bits set in that event register |
| 4 | 16 | Message Available (MAV): data waiting in the output buffer |
| 5 | 32 | Standard Event: one or more enabled bits set in that event register |
| 6 | 64 | Request Service (RQS): instrument is requesting SRQ service |
| 7 | — | Not used, always 0 |

- Cleared by `*CLS`, or per-bit by querying the corresponding event register.
- The *enable* register (which bits reach bit 6/RQS) is cleared at power-on only if
  `*PSC 1` was previously set (`*PSC 0` preserves it across power cycles), or by `*SRE 0`.

**Standard Event register:**

| Bit | Decimal | Meaning |
|---|---|---|
| 0 | 1 | Operation Complete — all commands up to and including an `*OPC` have executed |
| 1 | 2 | Not used |
| 2 | 4 | Query Error — output buffer read while empty, or new command line arrived before a pending query was read, or both buffers full |
| 3 | 8 | Device Error — self-test, calibration, or reading-overload error (error numbers 501–748) |
| 4 | 16 | Execution Error (error numbers -211 through -230) |
| 5 | 32 | Command Error — syntax error (error numbers -101 through -158) |
| 6 | 64 | Not used |
| 7 | 128 | Power On — power was cycled since this register was last read/cleared |

Gotcha: bits 2/3/4/5 (an actual error condition) always also push an entry into the error
queue (readable via `SYSTem:ERRor?`) — *except* a reading-overload condition, which sets bit
3 here and the corresponding Questionable Data bit, but records nothing in the error queue.

**Questionable Data register** (measurement-quality flags, enabled via
`STATus:QUEStionable:ENABle`):

| Bit | Decimal | Meaning |
|---|---|---|
| 0 | 1 | Voltage overload (DC/AC volts, frequency, period, diode, or ratio) |
| 1 | 2 | Current overload (DC/AC current) |
| 9 | 512 | Ohms overload (2-wire or 4-wire) |
| 12 | 4096 | Limit Fail HI — reading exceeds upper limit-test bound |
| 11 | 2048 | Limit Fail LO — reading below lower limit-test bound |
| (all other bits) | — | Not used |

### Status Reporting Commands

- **`SYSTem:ERRor?`** — see System-Related Commands above.
- **`STATus:QUEStionable:ENABle <value>`** / **`?`** — enable-register mask for the
  Questionable Data register, as a binary-weighted decimal sum.
- **`STATus:QUEStionable:EVENt?`** — query (and clear) the Questionable Data event register.
- **`STATus:PRESet`** — clear the Questionable Data enable register.
- **`*CLS`** — clear the Status Byte summary and all event registers.
- **`*ESE <value>`** / **`?`** — Standard Event enable-register mask.
- **`*ESR?`** — query (and clear) the Standard Event register.
- **`*OPC`** — set the Operation Complete bit once all prior commands finish (non-blocking).
- **`*OPC?`** — returns `"1"` to the output buffer once all prior commands finish (blocking —
  a synchronization point).
- **`*PSC {0|1}`** / **`?`** — whether the Status Byte / Standard Event enable masks clear at
  power-on (`1`, default behavior when set) or persist (`0`). [Stored in non-volatile memory]
- **`*SRE <value>`** / **`?`** — Status Byte enable-register mask (which summary bits can
  assert the hardware SRQ line).
- **`*STB?`** — query the Status Byte summary register like a serial poll, but as an ordinary
  command: it does **not** clear the RQS bit (a real serial poll does), and it's held until
  all prior commands have actually completed (a real serial poll is handled by bus hardware
  and isn't synchronized that way).

Using SRQ requires your controller to handle the IEEE-488 SRQ interrupt and a serial poll to
identify which instrument asserted it and to clear bit 6. A typical synchronization sequence
from the source manual: send a device clear, `*CLS`, set `*ESE 1` (enable Operation Complete)
and `*SRE 32` (route Standard Event summary bit to SRQ), `*OPC?` to confirm sync, then your
measurement commands ending in `*OPC`.

### Calibration Commands

These are part of the native command set — not excluded — but this is a sensitive,
infrequently-automated area. **Every command here except the two query-only ones requires
the instrument to be unsecured first** (`CALibration:SECure:STATe OFF,<code>`), and the
factory-default security code is `"HP034401"` (see "Before you start"). Don't wire these into
routine measurement scripts.

- **`CALibration?`** — perform a calibration using the value set via `CALibration:VALue`.
  Requires unsecured state.
- **`CALibration:COUNt?`** — query-only: total calibration count (wraps at 32,767; a single
  full calibration increments it by many counts, not just one). [Stored in non-volatile
  memory]
- **`CALibration:SECure:CODE <new code>`** — change the security code (up to 12 characters).
  Must be unsecured with the *old* code first. [Stored in non-volatile memory]
- **`CALibration:SECure:STATe {OFF|ON},<code>`** / **`?`** — secure/unsecure. Query returns
  `"0"`/`"1"`. [Stored in non-volatile memory]
- **`CALibration:STRing <quoted string>`** / **`?`** — a free-text calibration note (cal date,
  next-due date, serial number, contact info — up to 40 characters, though only 12 display on
  the front panel). Settable only from the remote interface; readable from either.
  [Stored in non-volatile memory]
- **`CALibration:VALue <value>`** / **`?`** — the known reference value used by the next
  `CALibration?` call.

### RS-232 Interface Configuration and Commands

RS-232 has real interface-specific framing beyond just "it's a serial port," which is why
it's broken out here separately from the GPIB material below.

**Cabling and framing:** see "Before you start" for the DTE-to-DTE cabling requirement, and
"Front-panel I/O MENU" for the fixed 1-start/2-stop-bit framing plus configurable baud/parity.

**DTR/DSR hardware handshake:** the instrument is a DTE device and uses DTR (pin 4, Data
Terminal Ready) and DSR (pin 6, Data Set Ready) for flow control:

- The instrument holds **DTR true** to signal it can accept input. It sets DTR **false**
  (a hold-off) in two cases: (1) its input buffer nears full (~100 characters received) —
  data must stop within 10 more characters once DTR drops, and it goes true again once
  there's room; (2) after processing a query and receiving the query's `<new line>`
  terminator — this means once you've sent a query, read its response before sending
  anything else, and your command string must actually end with `<new line>` for this
  mechanism to trigger correctly.
- The instrument monitors **DSR** before sending each character; if DSR is false, output is
  suspended until DSR goes true.
- **Deadlock and how to break it:** if output is suspended (DSR false) the instrument also
  holds DTR false, which can deadlock the link until the controller asserts DSR true. Send a
  `<Ctrl-C>` character to break this — it clears the in-progress operation and discards
  pending output, equivalent to an IEEE-488 device clear. Gotcha: for `<Ctrl-C>` to be
  reliably recognized while the instrument holds DTR false, **the controller must first set
  DSR false**.
- To disable DTR/DSR handshaking entirely: don't connect the DTR line, and tie DSR to logic
  true on the cable/adapter — but then also use a slower baud rate (300/600/1200) to keep
  data reliable without hardware flow control.

**Commands:**

- **`SYSTem:LOCal`** — local mode: all front-panel keys work normally.
- **`SYSTem:REMote`** — remote mode: all front-panel keys except LOCAL are disabled.
  **You must send this before RS-232 commands behave predictably** — the source manual calls
  this out explicitly: sending/receiving data over RS-232 while not in remote mode "can cause
  unpredictable results."
- **`SYSTem:RWLock`** — like `SYSTem:REMote`, but *including* the LOCAL key (full lockout).
- **`<Ctrl-C>`** — not a SCPI command, a raw control character: clears the in-progress
  operation and discards pending output, the RS-232 equivalent of IEEE-488 device clear.

### GPIB-specific setup

- **Addressing.** 0–31, factory default 22, set only from the front panel (I/O MENU →
  1: HP-IB ADDR). Displayed on power-up.
- **Address 31 — TALK ONLY mode.** Lets the instrument push readings straight to a printer
  without any bus controller addressing it — works over either GPIB or RS-232, provided the
  printer is set to "listen always." Not a valid address if you're actually running the
  instrument from a controller. If RS-232 is the selected interface and the GPIB address
  happens to be set to 31, the instrument sends readings over RS-232 while in local mode.
- **Device Clear.** A low-level IEEE-488 bus message (exposed differently by different
  interface cards/languages) that aborts any in-progress measurement, returns the trigger
  system to Idle, clears both buffers, and readies the instrument for a new command — without
  touching status registers, the error queue, or configuration state. The RS-232 equivalent
  is the `<Ctrl-C>` character described above.
- **IEEE-488.2 dedicated hardware lines:** ATN (Attention), IFC (Interface Clear), REN
  (Remote Enable), SRQ (Service Request Interrupt).
- **IEEE-488.2 addressed commands supported:** DCL (Device Clear), EOI (End-or-Identify
  terminator), GET (Group Execute Trigger), GTL (Go To Local), LLO (Local Lockout), SDC
  (Selected Device Clear), SPD/SPE (Serial Poll Disable/Enable).
- **IEEE-488.2 common commands supported:** `*CLS`, `*ESE`/`*ESE?`, `*ESR?`, `*IDN?`, `*OPC`/
  `*OPC?`, `*PSC`/`*PSC?`, `*RST`, `*SRE`/`*SRE?`, `*STB?`, `*TRG`, `*TST?` — all documented
  in their relevant sections above.

## Worked end-to-end example: configured measurement over RS-232

This is adapted from the vendor manual's own RS-232 QuickBASIC example (Chapter 6), which is
representative of this device's single most common task: put it in remote mode, confirm
you're talking to the right instrument, configure a measurement, and read results back.

```text
' Open the serial port matching your front-panel I/O MENU settings
' (this example: 9600 baud, even parity, 7 data bits, 2 stop bits — the factory defaults)

:SYST:REM
  ' Puts the instrument in remote mode. Required before anything else over RS-232 —
  ' see "RS-232 Interface Configuration and Commands" above.

*IDN?
  -> read one line, e.g.: HEWLETT-PACKARD,34401A,0,11-5-2
  ' Confirms you're talking to the right instrument before doing anything else.

:SYST:VERS?
  -> read one line, e.g.: 1994.0
  ' Confirms the SCPI conformance version, useful for compatibility logging.

:SYST:BEEP;:DISP:TEXT '34401A'
  ' Audible + visual confirmation on the instrument itself that a script has taken control.
  ' Two commands chained with ; and a leading : since SYSTem and DISPlay are different
  ' subsystems — see "Command syntax conventions" above.

:CONF:VOLT:DC 10,0.1;:SAMP:COUN 4
  ' Configure for DC voltage, 10V range, 0.1V resolution, 4 samples per trigger.
  ' CONFigure does not trigger a measurement by itself.

:READ?
  -> read one line, e.g.: +1.25000000E+00,+1.25100000E+00,+1.24900000E+00,+1.25200000E+00
  ' READ? both arms the trigger system (Idle -> Wait-for-Trigger, immediate trigger source
  ' by default) and sends the resulting readings straight to the output buffer, comma-
  ' separated, terminated with <CR><LF> over RS-232 (<LF> alone over GPIB).
```

The equivalent GPIB version differs only in the connection/addressing step and the message
terminator your read routine should expect (`<LF>`/EOI rather than `<CR><LF>`) — every SCPI
command above is identical over either interface, which is the whole point of documenting
them together in one command reference.

For a lower-level, manually-triggered variant using `INITiate`/`FETCh?` instead of `READ?`
(useful when you want readings buffered internally rather than streamed immediately), or one
using `TRIGger:SOURce EXTernal` to trigger off the rear-panel Ext Trig terminal, see the
"Using the MEASure? Command" / "Using the INITiate and FETCh? Commands" discussion folded
into "The `MEASure?` and `CONFigure` Commands" section above — the vendor manual presents
those as sequential refinements of the same underlying idea, which this manual's structure
mirrors.

## Common gotchas

- **RS-232 needs `SYSTem:REMote` first.** Sending commands before this is explicitly called
  out by the vendor as producing "unpredictable results" — this is the single most common
  RS-232 setup mistake.
- **DTR/DSR deadlock.** See the full mechanism under "RS-232 Interface Configuration and
  Commands" above. If a script seems to hang mid-transaction on RS-232, this handshake is the
  first thing to check, and `<Ctrl-C>` (with DSR first set false) is the escape hatch.
- **Don't send a second query before reading the first response.** Called out twice in the
  source manual, once generically and once specifically for `MEASure?`. If unavoidable, send
  a device clear (GPIB) or `<Ctrl-C>` (RS-232) before the second query.
- **`[Stored in volatile memory]` vs. `[Stored in non-volatile memory]` is a real, pervasive
  pattern, not a one-off annotation.** The great majority of measurement-configuration
  commands (range, resolution, NPLC, trigger settings, math registers, etc.) are volatile —
  they reset to defaults on `*RST` or power-cycle. A comparatively short list is
  non-volatile and *persists*: GPIB address, interface selection, baud rate, parity,
  programming language, beeper state, `*PSC` setting, the `CALCulate:DBM:REFerence` value,
  and everything under Calibration. When writing a script, don't assume yesterday's
  measurement configuration survived a power cycle — but don't assume the interface/
  calibration settings reset either.
- **Reading overload is invisible in the error queue.** A reading-overload condition sets
  Standard Event register bit 3 and a Questionable Data bit, but — unlike essentially every
  other error condition — records nothing retrievable via `SYSTem:ERRor?`. If you're only
  polling the error queue for problems, you'll miss overloads; check the Questionable Data
  register or watch for the `"OVLD"` display convention instead.
- **Calibration requires unsecuring first, and only works in the SCPI language.** See the
  Calibration Commands section — and note if you ever do switch languages, calibration
  commands from the other two (3478A/Fluke) modes are simply ignored, not translated.
- **`TRIGger:COUNt INFinite` has no soft exit.** Once set, the only way back to the Idle
  trigger state is a device clear (GPIB) or `<Ctrl-C>` (RS-232) — there's no "stop after this
  one" command.
- **AC voltage/current resolution parameter is cosmetic.** On `MEASure:VOLTage:AC?` /
  `CONFigure:VOLTage:AC` (and the AC current equivalents), the resolution you specify only
  changes the front-panel digit count — actual measurement resolution is always fixed at 6½
  digits regardless of what you pass.

## What's excluded and why

- **Alternate Programming Language Compatibility (Agilent 3478A / Fluke 8840A/8842A
  emulation modes)** — the instrument can be switched (via `L1`/`L2`/`L3`, or the front-panel
  LANGUAGE menu item) to accept those meters' native command syntax instead of SCPI. This is
  an unrequested compatibility layer for scripts written against a *different* instrument, not
  part of this instrument's own native command set — the same exclusion pattern used for the
  Rigol DM3058E's Agilent/Fluke-compatible command chapters elsewhere in this repo. Worth
  knowing it exists: virtually every command from either emulated instrument works as-is
  except self-test and calibration (always ignored/redirected — see the vendor manual's own
  compatibility tables if you ever need this mode), and this mode is **not available over
  RS-232 at all**, only GPIB.
- **Full enumeration of Chapter 5's ~80 individual error codes** — this manual documents the
  error-queue *mechanism* (FIFO, 20-deep, 80-char messages, `SYSTem:ERRor?`, cleared by power
  cycle or `*CLS`, `-350`/"Too many errors" overflow behavior) rather than transcribing every
  numbered error string; the vendor manual's Chapter 5 is the place to look up a specific
  error code's meaning once your script reports one.
- **BenchLink/Meter Software (34812A)** — an optional PC-side Windows application for
  logging/graphing readings. It's software that talks to the instrument using the same SCPI
  commands documented here; it doesn't add any instrument-side commands of its own, so
  there's nothing device-specific to document.
- **34398A Cable Kit / 34399A Adapter Kit** — passive RS-232 cabling accessories, not
  commands; covered by reference in "Before you start" only.
- **Calibration *procedure* detail** (what physical reference standards to apply, in what
  sequence) — the vendor manual explicitly defers this to the separate Service Guide, Chapter
  4. This manual documents the `CALibration` command *syntax* only, which is what a
  `DevTerm.Devices.Scpi` profile would actually need.
- **GPIB was not excluded** — the user asked for both GPIB and RS-232, and both got
  substantive, separately-documented interface-specific treatment above (RS-232 handshake and
  cabling in its own subsection; GPIB addressing, TALK ONLY, Device Clear, and the IEEE-488.2
  hardware-line/addressed-command tables in its own subsection) rather than one interface
  being an afterthought to the other.
