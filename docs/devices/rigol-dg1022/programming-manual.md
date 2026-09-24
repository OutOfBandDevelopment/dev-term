# Rigol DG1022 — Programming Manual (USB-TMC)

Dual-Channel Function/Arbitrary Waveform Generator. This manual covers the SCPI-style
remote command set reachable over the instrument's **USB-TMC** interface, which is the
*only* remote interface this hardware has (see "What's excluded and why" at the end).

## Source documents

1. **RIGOL Programming Guide — DG1000 Series Dual-Channel Function/Arbitrary Waveform
   Generator**, Jan. 2014, Publication Number **PGB06109-1110**, RIGOL Technologies, Inc.
   This document explicitly states it covers **DG1022 and DG1022A**, using the DG1022 as
   the worked example throughout its command reference. Treated here as the primary,
   canonical source — it is the newest of the three and the one to trust on any conflict.
2. RIGOL Programming Guide — DG1022 Function/Arbitrary Waveform Generator, Aug. 2009
   (older, DG1022-specific). Used only to cross-check source 1; no material differences
   were found in the command set.
3. RIGOL User's Guide — DG1000 Series Dual-Channel Function/Arbitrary Waveform
   Generator, 2008/2011 editions. Used for physical panel layout and connector
   identification.

Rigol's own note in source 1: *"DG1000 series Dual-channel Function/Arbitrary Waveform
Generator includes DG1022 and DG1022A. In this manual, DG1022 is taken as an example to
illustrate the command system and its using method."* No DG1022-vs-DG1022A command
differences are documented anywhere in these sources.

## Before you start

### Physical connection

The DG1022's only remote-control connector is the **USB Device** port, located on the
**rear panel** next to the main power switch/socket. Connect it to a PC's USB port with a
standard USB cable (Type A–Type B). Rigol's programming guide states this outright:

> "Computers communicate with the generator by sending and receiving commands over USB
> interface. Command is sent and identified in the form of ASCII character strings for
> users to easily control the generator and do user-defined development."

Rear panel layout (left to right, per the User's Guide's rear-panel diagram): 10 MHz
Reference Input/Output, [Sync Out] connector, [Modulation In] (External modulation
input), [Ext Trig/FSK/Burst] connector, **USB Device**, Main Power Switch, Power Socket.

There is also a **USB Host** port on the **front panel**. This is *not* a remote-control
interface — it's for plugging in a USB flash drive (waveform/state storage, firmware
update) or for connecting to a Rigol PA1011 power amplifier accessory (the generator acts
as USB host, the PA as USB device). Do not confuse it with the rear-panel USB Device port
used for PC control.

### Driver / software prerequisites

The DG1022 enumerates as a USB-TMC (Test & Measurement Class) device. To talk to it from
a PC you need a VISA layer (NI-VISA, Keysight/Agilent IO Libraries, or an open equivalent
such as pyvisa-py with a libusb backend) or a raw USB-TMC driver, plus the instrument's
*IDN?* string to confirm enumeration before sending any other command.

### Model scope note

Everything below applies to the plain DG1022. Rigol's DG1000-series guide states the
DG1022A shares the same command system; no DG1022A-only commands were found in any of
the three source documents, so nothing has been held back on that basis.

## Command syntax conventions

(Source: Programming Guide §1, "Commands Introduction")

- **Tree structure.** Each command is a "root" keyword followed by one or more sub-keywords,
  separated by `:`. A trailing `?` turns the command into a query. The command and its
  parameter(s) are separated by a space.
  ```
  FUNCtion:SQUare:DCYCle {<percent>|MINimum|MAXimum}
  FUNCtion:SQUare:DCYCle? [MINimum|MAXimum]
  ```
  `FUNCtion` is the root keyword; `SQUare` and `DCYCle` are the second- and third-level
  keywords.
- **Multiple parameters** in one command are comma-separated:
  ```
  DATA VOLATILE,<value>,<value>,...
  ```
- **Braces `{ }`** enclose a required choice; only one option may be selected, options are
  `|`-separated. Example: `{ON|OFF}`.
- **Square brackets `[ ]`** enclose an optional keyword/parameter; the command executes
  correctly whether it's present or omitted. Example: `DATA:COPY <destination arb
  name>[,VOLATILE]` — the `,VOLATILE` may be dropped.
- **Angle brackets `< >`** mark a placeholder that must be replaced with an actual value.
  Example: `DISPlay:CONTRAST <value>` → `DISPlay:CONTRAST 25`.
- **Parameter types** (5 kinds):
  1. **Boolean** — `OFF`/`ON` (equivalently `0`/`1`), e.g. `AM:STATe {OFF|ON}`.
  2. **Consecutive integer** — any integer in a stated range, e.g. `DISPlay:CONTRAST
     <value>` (0–31).
  3. **Consecutive real number** — any value within range/precision, e.g. `FREQuency
     {<frequency>|MINimum|MAXimum}` (1 µHz–20 MHz for a sine wave).
  4. **Discrete** — one of an enumerated set of values, e.g. `MEMory:STATe:NAME?
     {0|1|2|...|10}`.
  5. **ASCII character string** — user-defined text, e.g. the `<destination arb name>` in
     `DATA:COPY`.
- **Abbreviation.** All commands are case-insensitive. If you abbreviate a keyword, you
  must keep exactly the capitalized letters shown in this manual — no fewer, no more.
  `FUNCtion:SQUare:DCYCle?` can be sent as `FUNC:SQU:DCYC?` or `func:squ:dcyc?`, but not
  `FUN:SQ:DCYC?`.

## Command reference

Commands are grouped by subsystem, matching the vendor manual's own chapter structure.
Most subsystems provide a CH1 form and an equivalent CH2 form (the CH2 form is the same
keyword suffixed with `:CH2`, e.g. `FUNCtion:CH2`, `VOLTage:CH2`). Where the CH2 form's
behavior is identical to CH1 apart from which channel it targets, its full syntax is
still given but the prose isn't repeated.

Subsystems with **no CH2 form** — because the underlying feature is CH1-only on this
instrument: AM, FM, PM, FSKey, SWEep, TRIGger, BURSt, COUNter, PHASe:ALIGN,
COUPling (channel coupling/copy inherently spans both channels), OUTPut:SYNC,
OUTPut:TRIGger(:SLOPe), SYSTem, DISPlay, MEMory.

### IEEE 488.2

Standard IEEE-488.2 common commands, prefixed with `*`, 3-character keyword.

**1. `*IDN?`**
- Function: query the instrument identification string.
- Return: 4 comma-separated fields — manufacturer, model, serial number, firmware
  revision (dot-separated numbers).
- Example: `*IDN?` → `RIGOL TECHNOLOGIES,DG1022,DG1D100,00.02.00.06.00.02.06`
- Gotcha: send this first on every new session to confirm the USB-TMC link is live and
  to see the exact firmware revision, since minor behavior can vary by revision.

### APPLy

Quick, one-line waveform setup — the fastest way to get a signal out. Each `APPLy:<shape>`
command sets frequency, amplitude and offset in one shot and switches the output function
to that shape.

| # | Command |
|---|---|
| 1 | `APPLy:SINusoid [<frequency>[,<amplitude>[,<offset>]]]` |
| 2 | `APPLy:SQUare [<frequency>[,<amplitude>[,<offset>]]]` |
| 3 | `APPLy:RAMP [<frequency>[,<amplitude>[,<offset>]]]` |
| 4 | `APPLy:PULSe [<frequency>[,<amplitude>[,<offset>]]]` |
| 5 | `APPLy:NOISe [<frequency\|DEFault>[,<amplitude>[,<offset>]]]` |
| 6 | `APPLy:DC [<frequency\|DEFault>[,<amplitude\|DEFault>[,<offset>]]]` |
| 7 | `APPLy:USER [<frequency>[,<amplitude>[,<offset>]]]` |
| 8 | `APPLy?` |
| 9–15 | Same as 1–7, with `:CH2` inserted before the args, targeting CH2 |
| 16 | `APPLy:CH2?` |

Per-command detail (CH1 forms; CH2 forms behave identically but target CH2):

- **`APPLy:SINusoid`** — generate a sine wave on CH1 with the given frequency, amplitude,
  DC offset. If you pass fewer than 3 params, they fill in order
  `<frequency>,<amplitude>,<offset>`. Default units: Hz, Vpp, VDC.
  Example: `APPL:SIN 1000,5.0,-1.5`
- **`APPLy:SQUare`** — generate a square wave. **Gotcha: this overwrites the current duty
  cycle setting and resets it to 50%** (see `FUNCtion:SQUare:DCYCle`).
  Example: `APPL:SQU 1000,5.0,-1.5`
- **`APPLy:RAMP`** — generate a ramp wave. **Gotcha: this overwrites the current symmetry
  setting and resets it to 50%** (see `FUNCtion:RAMP:SYMMetry`).
  Example: `APPL:RAMP 1000,5.0,-1.5`
- **`APPLy:PULSe`** — generate a pulse wave. Example: `APPL:PULS 1000,5.0,-1.5`
- **`APPLy:NOISe`** — generate Gaussian noise (5 MHz bandwidth). **Gotcha: the frequency
  parameter has no effect on the output but a value or `DEFault` must still be supplied —
  it's a required placeholder, not optional.** Example: `APPL:NOIS DEF,5.0,2.0`
- **`APPLy:DC`** — output a DC level set by `<offset>`. **Gotcha: same placeholder trap as
  NOISe — frequency *and* amplitude are ignored but a value/`DEFault` is still required for
  both.** Example: `APPL:DC DEF,DEF,-2.5`
- **`APPLy:USER`** — output the arbitrary wave previously selected via `FUNCtion:USER`,
  with the given frequency/amplitude/offset. Example: `APPL:USER 1000,5.0,-1.5`
- **`APPLy?`** — query CH1's current function and settings. Return: a quoted string —
  function, frequency, amplitude, offset, all in scientific notation. Example return:
  `CH1:"SIN,1.000000e+03,5.000000e+00,-1.500000e+00"`
- **`APPLy:CH2?`** — same as above for CH2. Example return:
  `CH2:"SIN,1.000000e+03,5.000000e+00,-1.500000e+00"`

### FUNCtion

Select output function, adjust square-wave duty cycle and ramp-wave symmetry, and choose
which arbitrary wave (built-in, user-defined, or volatile) is active.

| # | Command |
|---|---|
| 1 | `FUNCtion {SINusoid\|SQUare\|RAMP\|PULSe\|NOISe\|DC\|USER}` |
| 2 | `FUNCtion?` |
| 3 | `FUNCtion:USER {<name of arbitrary wave>\|VOLATILE}` |
| 4 | `FUNCtion:USER?` |
| 5 | `FUNCtion:SQUare:DCYCle {<percent>\|MINimum\|MAXimum}` |
| 6 | `FUNCtion:SQUare:DCYCle? [MINimum\|MAXimum]` |
| 7 | `FUNCtion:RAMP:SYMMetry {<percent>\|MINimum\|MAXimum}` |
| 8 | `FUNCtion:RAMP:SYMMetry? [MINimum\|MAXimum]` |
| 9–16 | Same as 1–8 with `:CH2` for CH2 |

- **`FUNCtion`** — select CH1's output function. Gotcha: sending `FUNC DC` then `FUNC USER`
  leaves the output on DC — `USER` alone doesn't switch away from DC; you need
  `FUNCtion:USER` explicitly. Example: `FUNC SIN`
- **`FUNCtion?`** — query CH1's function. Returns `CH1:SIN`, `CH1:SQU`, `CH1:RAMP`,
  `CH1:PULS`, `CH1:NOIS`, or `CH1:ARB` (the query **always** returns `ARB` after `FUNC DC`
  or `FUNC USER` — it never echoes back `DC`). Default: `CH1:SIN`.
- **`FUNCtion:USER`** — pick a built-in arbitrary wave, a nonvolatile user-defined wave
  (1 of 10 slots), or `VOLATILE` (the wave currently downloaded to volatile memory) for
  CH1. Gotcha: abbreviation is invalid for `VOLATILE` — it must be spelled out.
  Built-in wave names:
  - *Common*: NegRamp, AttALT, AmpALT, StairDown, StairUp, StairUD, Cpulse, PPulse,
    NPulse, Trapezia, RoundHalf, AbsSine, AbsSineHalf, SINE_TRA, SINE_VER
  - *Math*: Exp_Rise, Exp_Fall, Tan, Cot, Sqrt, X2, Sinc, Gauss, HaverSine, Lorentz,
    Dirichlet, GaussPulse, Airy
  - *Project*: Cardiac, Quake, Gamma, Voice, TV, Combin, BandLimited, Stepresponse,
    Butterworth, Chebyshev1, Chebyshev2
  - *Window function*: Boxcar, Barlett, triang, Blackman, Hamming, Hanning, Kaiser
  - *Others*: Roundpm, DC (to select DC, actually send `FUNC DC`, not `FUNC:USER DC`)

  Example: `FUNC:USER VOLATILE`
- **`FUNCtion:USER?`** — query the active arbitrary wave name on CH1. Returns the built-in
  name (e.g. `EXP_RISE`), `VOLATILE`, or a user-defined name. Default: `EXP_RISE`. Gotcha:
  invalid (meaningless) when DC is currently selected.
- **`FUNCtion:SQUare:DCYCle`** — set CH1 square-wave duty cycle. `MIN`/`MAX` bound the
  achievable duty cycle *at the currently selected frequency* — the usable range shrinks
  at high frequency. Example: `FUNC:SQU:DCYC 50`
- **`FUNCtion:SQUare:DCYCle?`** — query it. Return format: percent as a decimal, e.g.
  `50.000000`.
- **`FUNCtion:RAMP:SYMMetry`** — set CH1 ramp symmetry, 0–100%. Example:
  `FUNC:RAMP:SYMM 50`
- **`FUNCtion:RAMP:SYMMetry?`** — query it. Return: `50.000000`.
- CH2 forms (`FUNCtion:CH2`, `FUNCtion:CH2?`, `FUNCtion:USER:CH2`,
  `FUNCtion:USER:CH2?`, `FUNCtion:SQUare:DCYCle:CH2`(`?`),
  `FUNCtion:RAMP:SYMMetry:CH2`(`?`)) — identical behavior, targeting CH2. Example:
  `FUNC:CH2 SIN`, `FUNC:USER:CH2 SINC`, `FUNC:SQU:DCYC:CH2 50`.

### FREQuency

Set/query output frequency per channel, plus sweep start/stop/center/span (CH1-only —
sweep and modulation apply only to CH1).

| # | Command |
|---|---|
| 1 | `FREQuency {<frequency>\|MINimum\|MAXimum}` |
| 2 | `FREQuency? [MINimum\|MAXimum]` |
| 3 | `FREQuency:CH2 {<frequency>\|MINimum\|MAXimum}` |
| 4 | `FREQuency:CH2? [MINimum\|MAXimum]` |
| 5 | `FREQuency:STARt {<frequency>\|MINimum\|MAXimum}` |
| 6 | `FREQuency:STARt? [MINimum\|MAXimum]` |
| 7 | `FREQuency:STOP {<frequency>\|MINimum\|MAXimum}` |
| 8 | `FREQuency:STOP? [MINimum\|MAXimum]` |
| 9 | `FREQuency:CENTer {<frequency>\|MINimum\|MAXimum}` |
| 10 | `FREQuency:CENTer? [MINimum\|MAXimum]` |
| 11 | `FREQuency:SPAN {<frequency>\|MINimum\|MAXimum}` |
| 12 | `FREQuency:SPAN? [MINimum\|MAXimum]` |

- **`FREQuency`** — set CH1's output frequency, default unit Hz. `MIN`/`MAX` are bounded by
  the currently selected function (e.g. sine: 1 µHz–20 MHz). Example: `FREQ MIN`
- **`FREQuency?`** — return value in scientific notation, Hz, e.g. `1.000000e-06`.
- **`FREQuency:CH2`/`FREQuency:CH2?`** — same, for CH2. Query return is prefixed, e.g.
  `CH2:1.000000e-06`.
- **`FREQuency:STARt`/`:STOP`/`:CENTer`/`:SPAN`** — sweep boundary settings (CH1 only,
  used together in pairs: start+stop, or center+span). Examples: `FREQ:STAR MIN`,
  `FREQ:STOP MAX`, `FREQ:CENT 10000000`, `FREQ:SPAN MAX`.
- Queries return scientific notation in Hz, e.g. `FREQuency:STOP?` → `2.000000e+07`.

### VOLTage

Amplitude, high/low level, offset and voltage unit, per channel.

| # | Command |
|---|---|
| 1 | `VOLTage {<amplitude>\|MINimum\|MAXimum}` |
| 2 | `VOLTage?` |
| 3 | `VOLTage:HIGH {<voltage>\|MINimum\|MAXimum}` |
| 4 | `VOLTage:HIGH?` |
| 5 | `VOLTage:LOW {<voltage>\|MINimum\|MAXimum}` |
| 6 | `VOLTage:LOW?` |
| 7 | `VOLTage:OFFSet {<offset>\|MINimum\|MAXimum}` |
| 8 | `VOLTage:OFFSet?` |
| 9 | `VOLTage:UNIT {VPP\|VRMS\|DBM}` |
| 10 | `VOLTage:UNIT?` |
| 11–20 | Same as 1–10 with `:CH2` for CH2 |

- **`VOLTage`** — set CH1 amplitude, default unit Vpp. `MIN`/`MAX` bound depends on the
  active function. Example: `VOLT MIN`
- **`VOLTage?`** — return in scientific notation, e.g. `4.000000e-03`.
- **`VOLTage:HIGH`/`:LOW`** — set the absolute high/low output levels in V (an alternative
  to amplitude+offset). Examples: `VOLT:HIGH MAX`, `VOLT:LOW MIN`. Query returns
  scientific notation, e.g. `1.000000e+01` / `-1.000000e+01`.
- **`VOLTage:OFFSet`** — set DC offset in VDC; `MIN`/`MAX` depend on the current function
  and amplitude. Example: `VOLT:OFFS MIN`. Query returns e.g. `-9.998000e+00`.
- **`VOLTage:UNIT`** — `VPP`, `VRMS`, or `DBM`. **Gotcha: `DBM` is only usable when the
  output load is *not* set to high-impedance** (see `OUTPut:LOAD`). Example:
  `VOLT:UNIT VPP`. Query returns one of the three tokens.
- CH2 forms behave identically: `VOLT:CH2 MIN`, `VOLT:HIGH:CH2 MAX`, `VOLT:LOW:CH2 MIN`,
  `VOLT:OFFS:CH2 MIN`, `VOLT:UNIT:CH2 VPP`, and their query forms (CH2 query returns are
  prefixed, e.g. `CH2: 4.000000e-03`).

### OUTPut

Output enable, termination, polarity, sync signal, trigger-output behavior.

| # | Command |
|---|---|
| 1 | `OUTPut {OFF\|ON}` |
| 2 | `OUTPut?` |
| 3 | `OUTPut:LOAD {<ohm>\|INFinity\|MINimum\|MAXimum}` |
| 4 | `OUTPut:LOAD? [MINimum\|MAXimum]` |
| 5 | `OUTPut:POLarity {NORMal\|INVerted}` |
| 6 | `OUTPut:POLarity?` |
| 7 | `OUTPut:SYNC {OFF\|ON}` |
| 8 | `OUTPut:SYNC?` |
| 9 | `OUTPut:TRIGger:SLOPe {POSitive\|NEGative}` |
| 10 | `OUTPut:TRIGger:SLOPe?` |
| 11 | `OUTPut:TRIGger {OFF\|ON}` |
| 12 | `OUTPut:TRIGger?` |
| 13 | `OUTPut:CH2 {OFF\|ON}` |
| 14 | `OUTPut:CH2?` |
| 15 | `OUTPut:LOAD:CH2 {<ohm>\|INFinity\|MINimum\|MAXimum}` |
| 16 | `OUTPut:LOAD:CH2? [MINimum\|MAXimum]` |
| 17 | `OUTPut:POLarity:CH2 {NORMal\|INVerted}` |
| 18 | `OUTPut:POLarity:CH2?` |

- **`OUTPut`** — enable/disable the front-panel [Output] connector for CH1. Default `OFF`.
  Example: `OUTP ON`
- **`OUTPut:LOAD`** — set the assumed output termination (ohms), used only to correct the
  reported/generated amplitude and offset — it does not change the generator's physical
  output impedance. Default 50 Ω. `INFinity` selects "High Z". Example: `OUTP:LOAD 50`.
  Query returns the value in Ω, or `Infinity`.
- **`OUTPut:POLarity`** — `NORMal` or `INVerted` waveform polarity for CH1. Example:
  `OUTP:POL NORM`. Query returns `NORM`/`INV`.
- **`OUTPut:SYNC`** — enable/disable the rear-panel [Sync Out] connector. **Only CH1
  provides sync output** — there is no `OUTPut:SYNC:CH2`. Example: `OUTP:SYNC OFF`. Query
  returns `SYNC OFF`/`SYNC ON`.
- **`OUTPut:TRIGger:SLOPe`** — edge of the trigger-output pulse (used with Sweep/Burst)
  emitted from [Ext Trig/FSK/Burst] when `OUTPut:TRIGger` is enabled. Example:
  `OUTP:TRIG:SLOP POS`. Query returns `POSITIVE`/`NEGATIVE`.
- **`OUTPut:TRIGger`** — enable/disable the [Ext Trig/FSK/Burst] connector as a trigger
  output. Example: `OUTP:TRIG OFF`. Query returns `OFF`/`ON`.
- CH2 forms: `OUTPut:CH2` (default `OFF`), `OUTPut:LOAD:CH2`, `OUTPut:POLarity:CH2`, and
  their queries — identical behavior on CH2. Note there is **no** `OUTPut:SYNC:CH2` or
  `OUTPut:TRIGger:CH2` — those are CH1-only physical connectors shared by the instrument.

### PULSe

Pulse-wave timing: period, width, duty cycle, per channel.

| # | Command |
|---|---|
| 1 | `PULSe:PERiod {<seconds>\|MINimum\|MAXimum}` |
| 2 | `PULSe:PERiod? [MINimum\|MAXimum]` |
| 3 | `PULSe:WIDTh {<seconds>\|MINimum\|MAXimum}` |
| 4 | `PULSe:WIDTh? [MINimum\|MAXimum]` |
| 5 | `PULSe:DCYCle {<percent>\|MINimum\|MAXimum}` |
| 6 | `PULSe:DCYCle? [MINimum\|MAXimum]` |
| 7–12 | Same as 1–6 with `:CH2` for CH2 |

Pulse shape reference: rise/fall measured 10%–90%; width measured at 50% crossing;
duty cycle = width / period × 100.

- **`PULSe:PERiod`** — set CH1 pulse period, seconds. Example: `PULS:PER 0.01`. Query
  returns scientific notation seconds, e.g. `1.000000e-02`.
- **`PULSe:WIDTh`** — set CH1 pulse width, seconds. Example: `PULS:WIDT 0.005`. Query
  returns e.g. `5.000000e-03`.
- **`PULSe:DCYCle`** — set CH1 pulse duty cycle, percent. Example: `PULS:DCYC 50`. Query
  returns scientific notation percent, e.g. `5.000000e+01`.
- CH2 forms: `PULS:PER:CH2`, `PULS:WIDT:CH2`, `PULS:DCYC:CH2`, and their queries — same
  behavior on CH2.

### AM (CH1 only)

Amplitude modulation. No `:CH2` form — only CH1 can output a modulated waveform.

| # | Command |
|---|---|
| 1 | `AM:SOURce {INTernal\|EXTernal}` |
| 2 | `AM:SOURce?` |
| 3 | `AM:INTernal:FUNCtion {SINusoid\|SQUare\|RAMP\|NRAMp\|TRIangle\|NOISe\|USER}` |
| 4 | `AM:INTernal:FUNCtion?` |
| 5 | `AM:INTernal:FREQuency {<frequency>\|MINimum\|MAXimum}` |
| 6 | `AM:INTernal:FREQuency?` |
| 7 | `AM:DEPTh {<depth percent>\|MINimum\|MAXimum}` |
| 8 | `AM:DEPTh? [MINimum\|MAXimum]` |
| 9 | `AM:STATe {OFF\|ON}` |
| 10 | `AM:STATe?` |

- **`AM:SOURce`** — internal or external modulation source, default `INT`. Example:
  `AM:SOUR EXT`. Query returns `INT`/`EXT`.
- **`AM:INTernal:FUNCtion`** — shape of the internal modulating waveform, default `SIN`.
  Example: `AM:INT:FUNC SQU`. Query returns one of `SIN`, `SQU`, `RAMP`, `NRAM`, `TRI`,
  `NOIS`, `USER`.
- **`AM:INTernal:FREQuency`** — modulating frequency, Hz. Range: 2 mHz–20 kHz. Example:
  `AM:INT:FREQ 200`. Query returns scientific notation, e.g. `2.000000e+02`.
- **`AM:DEPTh`** — modulation depth, percent. **Range: 0%–120%** (can exceed 100%).
  Example: `AM:DEPT 70`. Query returns e.g. `7.000000e+01`.
- **`AM:STATe`** — enable/disable AM. Example: `AM:STAT OFF`. Query returns `OFF`/`ON`.

### FM (CH1 only)

Frequency modulation. Same shape as AM but with `FM:DEViation` instead of `AM:DEPTh`.

| # | Command |
|---|---|
| 1 | `FM:SOURce {INTernal\|EXTernal}` |
| 2 | `FM:SOURce?` |
| 3 | `FM:INTernal:FUNCtion {SINusoid\|SQUare\|RAMP\|NRAMp\|TRIangle\|NOISe\|USER}` |
| 4 | `FM:INTernal:FUNCtion?` |
| 5 | `FM:INTernal:FREQuency {<frequency>\|MINimum\|MAXimum}` |
| 6 | `FM:INTernal:FREQuency?` |
| 7 | `FM:DEViation {<frequency deviation>\|MINimum\|MAXimum}` |
| 8 | `FM:DEViation? [MINimum\|MAXimum]` |
| 9 | `FM:STATe {OFF\|ON}` |
| 10 | `FM:STATe?` |

Same semantics as AM's `SOURce`/`INTernal:FUNCtion`/`INTernal:FREQuency`/`STATe`
(2 mHz–20 kHz modulating frequency range). `FM:DEViation` sets frequency deviation in Hz,
e.g. `FM:DEV 100`; query returns scientific notation Hz, e.g. `1.000000e+02`.

### PM (CH1 only)

Phase modulation. Same shape as AM/FM but `PM:DEViation` is in degrees, not Hz.

| # | Command |
|---|---|
| 1 | `PM:SOURce {INTernal\|EXTernal}` |
| 2 | `PM:SOURce?` |
| 3 | `PM:INTernal:FUNCtion {SINusoid\|SQUare\|RAMP\|NRAMp\|TRIangle\|NOISe\|USER}` |
| 4 | `PM:INTernal:FUNCtion?` |
| 5 | `PM:INTernal:FREQuency {<frequency>\|MINimum\|MAXimum}` |
| 6 | `PM:INTernal:FREQuency?` |
| 7 | `PM:DEViation {<phase deviation>\|MINimum\|MAXimum}` |
| 8 | `PM:DEViation? [MINimum\|MAXimum]` |
| 9 | `PM:STATe {OFF\|ON}` |
| 10 | `PM:STATe?` |

`PM:DEViation` range is 0°–360°, e.g. `PM:DEV 180`; query returns scientific notation
degrees, e.g. `1.800000e+02`.

### FSKey (CH1 only)

Frequency-shift keying — output frequency "hops" between a carrier frequency (set via
`FREQuency`) and a hop frequency, at a rate set internally or driven by the external
[Ext Trig/FSK/Burst] connector.

| # | Command |
|---|---|
| 1 | `FSK:SOURce {INTernal\|EXTernal}` |
| 2 | `FSK:SOURce?` |
| 3 | `FSK:FREQuency {<frequency>\|MINimum\|MAXimum}` |
| 4 | `FSK:FREQuency?` |
| 5 | `FSK:INTernal:RATE {<rate>\|MINimum\|MAXimum}` |
| 6 | `FSK:INTernal:RATE?` |
| 7 | `FSK:STATe {OFF\|ON}` |
| 8 | `FSK:STATe?` |

- **`FSK:SOURce`** — default `INT`. Example: `FSK:SOUR EXT`.
- **`FSK:FREQuency`** — the hop frequency, Hz. Example: `FSK:FREQ 10`. Query returns
  scientific notation, e.g. `1.000000e+01`.
- **`FSK:INTernal:RATE`** — shift rate, Hz, range 2 mHz–50 kHz. Example:
  `FSK:INT:RATE 100`. Query returns e.g. `1.000000e+02`.
- **`FSK:STATe`** — enable/disable. Example: `FSK:STAT OFF`.

### SWEep (CH1 only)

Frequency sweep from `FREQuency:STARt` to `FREQuency:STOP` (or `CENTer`±`SPAN`) over
`SWEep:TIME`, on sine/square/ramp/arbitrary waves (**not** pulse, noise, or DC).

| # | Command |
|---|---|
| 1 | `SWEep:SPACing {LINear\|LOGarithmic}` |
| 2 | `SWEep:SPACing?` |
| 3 | `SWEep:TIME {<seconds>\|MINimum\|MAXimum}` |
| 4 | `SWEep:TIME?` |
| 5 | `SWEep:STATe {OFF\|ON}` |
| 6 | `SWEep:STATe?` |

- **`SWEep:SPACing`** — linear or logarithmic sweep, default `Linear`. Example:
  `SWE:SPAC LIN`. Query returns `LINEAR`/`LOG`.
- **`SWEep:TIME`** — total sweep duration, seconds. Default 1 s, range 1 ms–500 s. Example:
  `SWE:TIME 10`. Query returns scientific notation, e.g. `1.000000e+01`.
- **`SWEep:STATe`** — enable/disable sweep mode. Example: `SWE:STAT OFF`.

You can also trigger a single sweep manually or externally — see `TRIGger:SOURce`.

### TRIGger (CH1 only)

Trigger source/edge/delay for Sweep and Burst modes.

| # | Command |
|---|---|
| 1 | `TRIGger:SOURce {IMMediate\|EXTernal\|BUS}` |
| 2 | `TRIGger:SOURce?` |
| 3 | `TRIGger:SLOPe {POSitive\|NEGative}` |
| 4 | `TRIGger:SLOPe?` |
| 5 | `TRIGger:DELay {<second>\|MINimum\|MAXimum}` |
| 6 | `TRIGger:DELay?` |

- **`TRIGger:SOURce`** — `IMMediate` (internal/free-running), `EXTernal` (from [Ext
  Trig/FSK/Burst]), or `BUS` (manual/software trigger). Default `IMM`. Example:
  `TRIG:SOUR EXT`.
- **`TRIGger:SLOPe`** — rising (`POS`, default) or falling (`NEG`) edge of the external
  trigger signal. Gotcha: only meaningful when `OUTPut:TRIGger` is enabled. Example:
  `TRIG:SLOP POS`.
- **`TRIGger:DELay`** — trigger delay, seconds. **Applies to Burst mode only.** Example:
  `TRIG:DEL 0.000005`. Query returns scientific notation, e.g. `5.000000e-06`.

### BURSt (CH1 only)

Output a specified number of cycles (or a gated burst) of sine/square/ramp/pulse/
arbitrary wave.

| # | Command |
|---|---|
| 1 | `BURSt:MODE {TRIGgered\|GATed}` |
| 2 | `BURSt:MODE?` |
| 3 | `BURSt:NCYCles {<cycle>\|INFinity\|MINimum\|MAXimum}` |
| 4 | `BURSt:NCYCles?` |
| 5 | `BURSt:INTernal:PERiod {<second>\|MINimum\|MAXimum}` |
| 6 | `BURSt:INTernal:PERiod? [MINimum\|MAXimum]` |
| 7 | `BURSt:PHASe {<angle>\|MINimum\|MAXimum}` |
| 8 | `BURSt:PHASe? [MINimum\|MAXimum]` |
| 9 | `BURSt:STATe {OFF\|ON}` |
| 10 | `BURSt:STATe?` |
| 11 | `BURSt:GATE:POLarity {NORMal\|INVerted}` |
| 12 | `BURSt:GATE:POLarity?` |

- **`BURSt:MODE`** — `TRIGgered` (output N cycles per trigger event, default) or `GATed`
  (output follows the external gate signal level at [Ext Trig/FSK/Burst]). Example:
  `BURS:MODE GAT`.
- **`BURSt:NCYCles`** — cycle count, trigger mode only. Range 1–50,000, or `INFinity`.
  Example: `BURS:NCYC 100`. Query returns scientific notation count or the literal string
  `"Infinite"`.
- **`BURSt:INTernal:PERiod`** — burst repeat period in internal-trigger mode, seconds.
  Range 1 µs–500 s. Example: `BURS:INT:PER 10`.
- **`BURSt:PHASe`** — initial phase of the burst, degrees, range −180 to 180. Example:
  `BURS:PHAS 150`.
- **`BURSt:STATe`** — enable/disable burst mode. Example: `BURS:STAT OFF`.
- **`BURSt:GATE:POLarity`** — polarity of the external gate signal, default `NORMal`.
  Example: `BURS:GATE:POL INV`.

### DATA

Create/manage arbitrary waveforms (output via CH1's `FUNCtion:USER`). Up to 10
user-defined waveforms in nonvolatile memory plus 1 in volatile memory; each waveform
holds 1–524,288 points.

| # | Command |
|---|---|
| 1 | `DATA VOLATILE,<value>,<value>,...` |
| 2 | `DATA:DAC VOLATILE,<value>,<value>,...` |
| 3 | `DATA:COPY <destination arb name>[,VOLATILE]` |
| 4 | `DATA:DELete <arb name>` |
| 5 | `DATA:CATalog?` |
| 6 | `DATA:RENAME <destination arb name>,<new arb name>` |
| 7 | `DATA:NVOLatile:CATalog?` |
| 8 | `DATA:NVOLatile:FREE?` |
| 9 | `DATA:ATTRibute:POINts? <destination arb name>` |
| 10 | `DATA:LOAD [<destination arb name>]` |

- **`DATA`** — load floating-point samples in **[-1, 1]** into volatile memory. **Gotcha:
  silently overwrites whatever was previously in volatile memory — no error, no
  confirmation.** Example: `DATA VOLATILE,1,0.67,0.33,0,-0.33,-0.67,-1`
- **`DATA:DAC`** — load raw 14-bit DAC codes, **integers 0–16383** (0 = minimum amplitude,
  16383 = maximum amplitude) into volatile memory. Same silent-overwrite gotcha as `DATA`.
  Example: `DATA:DAC VOLATILE,8192,16383,8192,0`
- **`DATA:COPY`** — copy the volatile-memory waveform into a named nonvolatile slot.
  Gotcha: the destination name is max 12 chars, must start with a letter (A–Z/a–z),
  remaining chars limited to `0-9` and `_` — no spaces. The `VOLATILE` keyword itself has
  **no valid abbreviation** — must be spelled in full. Example: `DATA:COPY a1,VOLATILE`
- **`DATA:DELete`** — delete a named waveform from volatile or nonvolatile memory. Example:
  `DATA:DEL a1`
- **`DATA:CATalog?`** — list every waveform available for selection: the 5 built-in
  waveforms, `VOLATILE` (if populated), and all nonvolatile user-defined names. Example
  return: `"VOLATILE","EXP_RISE","EXP_FALL","NEG_RAMP","SINC","CARDIAC","A","B","C","D"`
- **`DATA:RENAME`** — rename a nonvolatile user-defined waveform. Example:
  `DATA:RENAME A,new`
- **`DATA:NVOLatile:CATalog?`** — list only the nonvolatile user-defined names (up to 10),
  quoted. Example return: `"A","B","C","D","E","F","G","H","I","J"`
- **`DATA:NVOLatile:FREE?`** — query free nonvolatile slots. Returns 0 (full) through 10.
- **`DATA:ATTRibute:POINts?`** — query point count of a named waveform, 0–524,288. Example
  return: `4096`
- **`DATA:LOAD`** — upload the named arbitrary waveform's data back to the controlling
  application (i.e., read the wave data out of the instrument).

### MEMory

10 nonvolatile instrument-state slots (STATE1–STATE10, numbered 1–10) plus location 0
which is volatile and auto-holds the power-down state.

| # | Command |
|---|---|
| 1 | `MEMory:STATe:NAME {0\|1\|...\|10}[,<name>]` |
| 2 | `MEMory:STATe:NAME? {0\|1\|...\|10}` |
| 3 | `MEMory:STATe:DELete {0\|1\|...\|10}` |
| 4 | `MEMory:STATe:RECall:AUTO {OFF\|ON}` |
| 5 | `MEMory:STATe:RECall:AUTO?` |
| 6 | `MEMory:STATe:VALid? {0\|1\|...\|10}` |
| 7 | `MEMory:NSTates?` |

- **`MEMory:STATe:NAME`** — assign a user-defined name to a state slot. Example:
  `MEM:STAT:NAME 1,A1`
- **`MEMory:STATe:NAME?`** — query the name of a slot; empty return if unnamed.
- **`MEMory:STATe:DELete`** — clear a slot's contents. Example: `MEM:STAT:DEL 1`
- **`MEMory:STATe:RECall:AUTO`** — whether the instrument auto-recalls the power-down
  state from slot 0 at power-on (`ON`) or performs a reset instead (`OFF`, **factory
  default**). Example: `MEM:STAT:REC:AUTO OFF`
- **`MEMory:STATe:VALid?`** — query whether a slot holds a valid stored state: `0` = no /
  deleted, `1` = yes.
- **`MEMory:NSTates?`** — always returns `11` (10 nonvolatile + slot 0).

### SYSTem

Error queue, firmware version, beeper, remote/local lock state, clock source, language.

| # | Command |
|---|---|
| 1 | `SYSTem:ERRor?` |
| 2 | `SYSTem:VERSion?` |
| 3 | `SYSTem:BEEPer:STATe {OFF\|ON}` |
| 4 | `SYSTem:BEEPer:STATe?` |
| 5 | `SYSTem:LOCal` |
| 6 | `SYSTem:RWLock` |
| 7 | `SYSTem:REMote` |
| 8 | `SYSTem:CLKSRC {EXT\|INT}` |
| 9 | `SYSTem:LANGuage {CHINESE\|ENGLISH}` |

- **`SYSTem:ERRor?`** — pop and clear the oldest queued error. Return format:
  `-118,"Invalid parameter"`. Gotcha: this is a **queue** — poll it in a loop after a
  batch of commands to drain all pending errors, since each call only returns one.
- **`SYSTem:VERSion?`** — firmware/SCPI revision string, e.g. `00.02.00.06.00.02.06`.
- **`SYSTem:BEEPer:STATe`** — enable/disable the error beep. Example:
  `SYST:BEEP:STAT OFF`. Query returns `0`/`1`.
- **`SYSTem:LOCal`** — return to local (front-panel) control, clear remote indicator,
  unlock the front panel.
- **`SYSTem:RWLock`** — enter remote state *with* front panel fully locked (including the
  Local button) — shows `R-LOCK`. **Gotcha: this locks out the physical Local button too,
  so you must send `SYSTem:LOCal` over the bus to get control back — there's no
  front-panel escape.**
- **`SYSTem:REMote`** — enter remote state, front panel locked *except* the Local button
  (shows `RMT`) — the safer of the two remote-lock commands for interactive debugging.
- **`SYSTem:CLKSRC`** — internal or external (via rear-panel [10 MHz In]) system clock
  reference. Default `INT`. Example: `SYST:CLKSRC EXT`
- **`SYSTem:LANGuage`** — front-panel display language. Example: `SYST:LANG CHINESE`

### PHASe

Initial phase per channel, and dual-channel phase alignment.

| # | Command |
|---|---|
| 1 | `PHASe {<angle>\|MINimum\|MAXimum}` |
| 2 | `PHASe? [MINimum\|MAXimum]` |
| 3 | `PHASe:CH2 {<angle>\|MINimum\|MAXimum}` |
| 4 | `PHASe:CH2? [MINimum\|MAXimum]` |
| 5 | `PHASe:ALIGN` |

- **`PHASe`**/**`PHASe:CH2`** — initial phase, degrees, range −180 to 180. Example:
  `PHAS 90`. Query returns e.g. `90.000`.
- **`PHASe:ALIGN`** — re-align the phase relationship of the two channels' outputs (no
  parameter — it's an event/action command, not a setting).

### DISPlay

Front-panel display control.

| # | Command |
|---|---|
| 1 | `DISPlay {OFF\|ON}` |
| 2 | `DISPlay:CONTRAST <value>` |
| 3 | `DISPlay:LUMINANCE <value>` |

- **`DISPlay`** — enable/disable the front-panel display. Example: `DISP OFF`
- **`DISPlay:CONTRAST`**/**`DISPlay:LUMINANCE`** — 0–31. Examples: `DISP:CONTRAST 25`,
  `DISP:LUMINANCE 25`

### COUPling

Channel coupling (CH2 tracks CH1 with a phase/frequency offset) and one-shot channel
parameter copy.

| # | Command |
|---|---|
| 1 | `COUPling {OFF\|ON}` |
| 2 | `COUPling?` |
| 3 | `COUPling:BASEdchannel{:CH1\|:CH2}` |
| 4 | `COUPling:BASEdchannel?` |
| 5 | `COUPling:PHASEDEViation <value>` |
| 6 | `COUPling:PHASEDEViation?` |
| 7 | `COUPling:FREQDEViation <value>` |
| 8 | `COUPling:FREQDEViation?` |
| 9 | `COUPling:CHANNCopy {1>2\|2>1}` |

- **`COUPling`** — enable/disable channel coupling. Example: `COUP OFF`
- **`COUPling:BASEdchannel`** — which channel is the reference the other tracks. Example:
  `COUP:BASE:CH1`. Query returns `CH1`/`CH2`.
- **`COUPling:PHASEDEViation`** — phase offset the coupled channel maintains, degrees,
  −180 to 180. Example: `COUP:PHASEDEV 10`. Query returns scientific notation, e.g.
  `1.000000e+01`.
- **`COUPling:FREQDEViation`** — frequency offset the coupled channel maintains, Hz, 0 to
  20 MHz. Example: `COUP:FREQDEV 100`.
- **`COUPling:CHANNCopy`** — one-shot copy of wave *parameters* (not shape) from one
  channel to the other. **Gotcha: only works while `COUPling` is `OFF`** — trying it while
  coupled is rejected. Example: `COUP:CHANNC 1>2`

### COUNter (CH1 input, no CH2 form)

Frequency counter function — measures an external signal rather than generating one.

| # | Command |
|---|---|
| 1 | `COUNter {OFF\|ON}` |
| 2 | `COUNter:COUPling {AC\|DC}` |
| 3 | `COUNter:COUPling?` |
| 4 | `COUNter:SENSitivity {LOW\|MEDIUM\|HIGH}` |
| 5 | `COUNter:SENSitivity?` |
| 6 | `COUNter:TLEVel {MIN\|MAX\|<value>}` |
| 7 | `COUNter:TLEVel?` |
| 8 | `COUNter:HFRS {ON\|OFF}` |
| 9 | `COUNter:HFRS?` |
| 10 | `COUNter:FREQuency?` |
| 11 | `COUNter:PERiod?` |
| 12 | `COUNter:DCYCle?` |
| 13 | `COUNter:POSWidth?` |
| 14 | `COUNter:NEGWidth?` |

- **`COUNter`** — enable/disable the counter. Example: `COUN ON`
- **`COUNter:COUPling`** — AC or DC input coupling. Example: `COUN:COUP AC`
- **`COUNter:SENSitivity`** — trigger sensitivity. Example: `COUN:SENS HIGH`
- **`COUNter:TLEVel`** — trigger level, encoded 0.0–99.9 across the −3 V to +3 V range in
  6 mV steps (i.e. `level_volts = -3 + (value/0.1) × 0.006`). Example: `COUNter:TLEVel 62`
  → level ≈ 0.72 V. Query returns the raw 0.0–99.9 value, e.g. `62.000000` — **not**
  volts; you must convert.
- **`COUNter:HFRS`** — high-frequency reject filter. Enable when measuring signals below
  1 kHz to filter noise; disable above 1 kHz. Example: `COUNter:HFRS ON`
- **`COUNter:FREQuency?`/`:PERiod?`/`:DCYCle?`/`:POSWidth?`/`:NEGWidth?`** — read-only
  measurement results. Return formats: frequency/period in decimal (e.g. `999.989319` Hz,
  `0.001000` s), duty cycle as a percentage string (e.g. `50.0%`), pulse widths in
  scientific-notation seconds (e.g. `5.00358e-04`).

## Worked end-to-end example: build and output a user-defined arbitrary waveform

This is the core workflow this instrument is built around — download a custom point
sequence, select it, and output it — adapted from the vendor manual's Application
Example 3 ("To Generate an User-defined Arbitrary Wave").

**Target:** a 10-second-period triangular ramp on CH1, swinging between −4 V and +4 V,
built from 4 points.

The instrument's arbitrary-waveform DAC is 14-bit: sample values run 0–16383, where 0
maps to the *low* level and 16383 maps to the *high* level you've configured via
`VOLTage:HIGH`/`VOLTage:LOW`. For this target: sample 0 (t=0s) = 0 V → DAC 8192 (midscale);
sample 1 (t=2.5s) = +4 V → DAC 16383; sample 2 (t=5s) = 0 V → DAC 8192; sample 3
(t=7.5s) = −4 V → DAC 0.

```
*IDN?                              ; confirm the link (expect RIGOL TECHNOLOGIES,DG1022,...)
FUNC USER                          ; select user-defined arbitrary wave on CH1
FREQ 100000                        ; 100 kHz base rate over 4 samples -> 10 s period
VOLT:UNIT VPP                      ; work in Vpp for level setup
VOLT:HIGH 4                        ; high level = +4 V
VOLT:LOW -4                        ; low level = -4 V
DATA:DAC VOLATILE,8192,16383,8192,0  ; download the 4 DAC-code samples to volatile memory
FUNC:USER VOLATILE                 ; point CH1's active waveform at what we just downloaded
OUTP ON                            ; enable CH1's front-panel [Output] connector
SYST:ERR?                          ; drain the error queue - expect "0,"No error""
```

To make it permanent across power cycles, follow up with:
```
DATA:COPY MYRAMP,VOLATILE          ; copy volatile -> nonvolatile slot named MYRAMP
DATA:CATalog?                      ; confirm MYRAMP now appears in the catalog
```

## Common gotchas

- **`APPLy:SQUare`/`APPLy:RAMP` silently reset duty cycle/symmetry to 50%.** If you've
  hand-tuned `FUNCtion:SQUare:DCYCle` or `FUNCtion:RAMP:SYMMetry`, calling the
  corresponding `APPLy` command afterward throws that setting away. Set frequency/
  amplitude/offset via the individual `FREQuency`/`VOLTage`/etc. commands instead if you
  need to preserve a custom duty cycle or symmetry.
- **`DATA`/`DATA:DAC` overwrite volatile memory with no error and no confirmation.**
  There's no "waveform already loaded" warning — the previous contents are just gone.
  Copy anything you want to keep to nonvolatile memory (`DATA:COPY`) before downloading a
  new one.
- **Case-insensitive, but abbreviations must match the documented capitalization
  exactly.** `FUNC:SQU:DCYC?` works; `FUN:SQ:DCYC?` does not — you can't abbreviate past
  the capitalized prefix shown for each keyword.
- **`FUNCtion?` always reports `ARB` after `FUNC DC` or `FUNC USER`.** It never echoes
  back `DC` even when DC is the true active output — check `FUNCtion:USER?` or `APPLy?`
  if you need to distinguish DC from a genuine arbitrary wave.
- **NOISe/DC `APPLy` commands still require a frequency/amplitude placeholder even though
  it's ignored.** `APPLy:NOISe 5.0,2.0` is invalid syntax — you must send
  `APPLy:NOISe DEFault,5.0,2.0` (or a numeric placeholder) even though the generator does
  nothing with that first value.
- **`SYSTem:RWLock` locks out the front-panel Local button entirely.** Use
  `SYSTem:REMote` instead during development/debugging unless you specifically need to
  prevent any front-panel interaction — `RWLock` requires a bus command
  (`SYSTem:LOCal`) to release.
- **Sweep, Burst, modulation (AM/FM/PM/FSK), Trigger, and the Counter are all CH1-only.**
  There is no `:CH2` form for any of these subsystems, unlike most of the rest of the
  command set which mirrors CH1/CH2.
- **`SYSTem:ERRor?` is a queue, not a single flag.** After sending a batch of setup
  commands, poll it in a loop (it returns `0,"No error"` when empty) rather than checking
  it once — an early error in the batch doesn't block later commands from also queuing
  their own errors.
- **`COUPling:CHANNCopy` fails silently/is rejected while `COUPling` is `ON`.** Disable
  coupling first.
- **`OUTPut:LOAD`/`OUTPut:LOAD:CH2` only correct displayed/generated amplitude — they do
  not change the physical output impedance**, which is a fixed 50 Ω source impedance on
  this instrument. Set it to match your actual load so the requested Vpp/offset land
  correctly at the load; it doesn't reconfigure the hardware driver.

## What's excluded and why

- **RS-232 was originally requested for this manual but is not present on this
  instrument.** All three source documents agree: the DG1022's only interfaces are a
  rear-panel **USB Device** port (remote control) and a front-panel **USB Host** port
  (flash storage / PA1011 amplifier connection, not remote control). The Programming
  Guide states flatly that "computers communicate with the generator... over USB
  interface" with no mention of a serial port anywhere in the document, and the User's
  Guide's own "Standard interfaces" line reads "USB Host & USB Device" — nothing else.
  There's no expansion slot or add-on module documented for this model that adds RS-232
  (unlike some other Rigol instrument families). If RS-232 (or GPIB) is a hard
  requirement, that points to a different Rigol model — e.g. a DG4000-series generator,
  which does offer LAN/GPIB/USB — rather than a firmware/config option on the DG1022.
- **GPIB** — same situation as RS-232: not documented as present on this model, not
  included here for the same reason.
- **DG1022A-specific commands** — none were found. Rigol's own Programming Guide (source
  1) explicitly covers both DG1022 and DG1022A as a single command set with DG1022 as the
  worked example, and no DG1022A-only command appears anywhere in the three source
  documents. Nothing was held back on this axis.
- **Front-panel USB Host port** — mentioned above for completeness but not documented as
  a command-reference interface, since it's a host port for external devices (flash
  drive, PA1011 amplifier), not a channel for a PC to send SCPI commands into the
  generator.
