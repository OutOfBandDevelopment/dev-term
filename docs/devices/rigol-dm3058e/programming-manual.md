# RIGOL DM3058E Remote Command Reference — USB (USB-TMC/VISA) Configuration

This reference lists the commands supported by a **RIGOL DM3058E** digital multimeter controlled over
**USB (USB-TMC/VISA)**. Source: *RIGOL Programming Guide, DM3058/DM3058E Digital Multimeter*, RIGOL
Technologies, Inc., Jan. 2015 (covers both the DM3058 and DM3058E in one document; no separate
part/document number is printed in the front matter).

Three things this configuration implies:

- **GPIB and LAN are DM3058-only.** The manual states this explicitly, twice: *"The GPIB and LAN
  interfaces are only supported by DM3058."* The entire `:LXI` command subsystem (LAN-based) and the
  GPIB/LAN `:UTILity:INTErface:*` commands are **excluded from this reference entirely** — the
  DM3058E has no LAN interface at all, so they're not a "use a different interface" caveat, they're
  hardware the DM3058E doesn't have.
- **RS-232 is also supported on this exact model**, but since you're using USB, the two RS-232
  configuration commands (`:UTILity:INTErface:RS232:BAUD`/`:PARIty`) have been left out of the main
  reference — see "What's Excluded and Why" below.
- **The instrument has three parallel, mutually exclusive command sets** — RIGOL's own (native,
  power-on default), Agilent-34401A-compatible, and Fluke-45-compatible — switched by a single
  `CMDSET` command. This is not a USB-vs-other-interface distinction; it applies identically over any
  interface. See "Command Set Selection" below — it's the single most important thing to understand
  before sending anything else in this document.

## Before you start

- Connect via the DM3058E's rear-panel USB device port. The instrument enumerates as a USB-TMC
  device; you'll need a VISA runtime (NI-VISA, Keysight IO Libraries, or RIGOL's own Ultra Sigma/
  RIGOL VISA) installed on the controlling PC to open a VISA resource string against it. No
  USB-specific SCPI commands exist — the manual documents no baud/address/mode settings for USB; once
  the VISA layer is connected, it "just works."
- **Interface compatibility table:**

  | Interface | DM3058 | DM3058E |
  |---|---|---|
  | USB | Yes | Yes |
  | RS-232 | Yes | Yes |
  | GPIB | Yes | **No** |
  | LAN / `:LXI` | Yes | **No** |

- **Command set matters more than interface.** Immediately after connecting, decide (and send) which
  of the three command sets you intend to use — see the next section. Don't assume the instrument is
  in the command set you expect; if it was last powered off in a non-default state and
  `:SYSTem:CONFigure:POWEron LAST` is active, it can power back on in whatever set (and even whatever
  measurement function) it was last left in.
- **A note for anyone using this manual to build or fix a dev-term `DevTerm.Devices.Scpi` profile**:
  the existing bundled `rigol-dm3058e.json` profile's commands (`MEAS:VOLT:DC?`, `CONF:VOLT:DC
  {Range}` with literal-value ranges like `AUTO|0.2|2|20|200|1000`) match this document's **Agilent-
  compatible command set**, not the RIGOL-native one — see "Command Set Selection" below. That means
  the profile implicitly depends on the instrument already being in `CMDSET AGILENT` mode, but it has
  no command that sends `CMDSET AGILENT` itself. On a factory-default instrument (RIGOL native mode
  at power-on), every command that profile sends will be rejected as invalid syntax until something —
  the profile itself, or the operator manually beforehand — sends `CMDSET AGILENT` first. This is
  flagged here as research findings; fixing the profile is separate follow-up work, not part of this
  document.

## Command syntax conventions

- **Colon `:`** begins a command / separates a keyword from a lower-level keyword, e.g.
  `:MEASure:VOLTage:DC?` (native set). The Agilent-compatible set generally omits the *leading* colon
  (`MEASure:VOLTage:DC?`) — this is a real, distinguishing difference between the two sets, not just a
  style choice; see below.
- **Query `?`** at the end of a command header forms the query form.
- **Braces `{ }`** with `|`-separated elements: exactly one element must be chosen, e.g. `{ON|OFF}`.
- **Angle brackets `< >`**: placeholder to be replaced with an actual value, e.g. `<range>`.
- **Square brackets `[ ]`**: optional/ignorable keyword; if omitted, the instrument uses a default,
  e.g. `[SENSe:]FUNCtion` (the `SENSe:` header itself is optional in the Agilent-compatible set),
  `CALCulate:LIMit:LOWer? [MINimum|MAXimum]`.
- **Abbreviation**: within a keyword, the capitalized portion is the minimum required abbreviation,
  the lowercase portion is optional — `:MEASure` can be sent as `:MEAS`. Applies to both command sets.
- **Parameter types**:
  - *Consecutive integer* — a whole number in range; sending a decimal where an integer is expected
    can raise an "unexpected exception."
  - *Consecutive real number* — an arbitrary value within range, default precision 6 digits after the
    decimal point.
  - *Discrete* — only specific cited values are legal (e.g. an index code like `{0|1|2|3|4}`, or a
    literal-value set like `{200mV|2V|20V|200V|1000V|AUTO}`).
  - *Boolean* — `ON`, `OFF`, `0`, or `1`.
  - *ASCII string* — a user-defined, quoted string.
- **No message terminator is documented anywhere in the manual's own examples.** dev-term's existing
  profile terminates with `\n`, the conventional SCPI terminator, and nothing in the source document
  contradicts that choice.
- The Agilent-compatible command set carries one extra, explicitly stated syntax warning (verbatim):
  *"the compatible commands must follow the syntax strictly... unwanted characters are not allowed in
  command trees; command trees and parameters should be separated by a space, unwanted characters are
  also not allowed to follow parameters, or else the instrument will beep to alarm for an execution
  error."* This is stricter than the RIGOL-native set and is a real, distinct gotcha — see Common
  Gotchas.

## 1. Command Set Selection (`CMDSET`)

| Command | Type | Description |
|---|---|---|
| `CMDSET` | Set | Switch active command set |
| `CMDSET?` | Query | Return active command set |

**`CMDSET {RIGOL\|AGILENT\|FLUKE}`**
Switches which of the three mutually exclusive command vocabularies the instrument currently parses.
- `RIGOL` — the instrument's own native command set (Chapter 3 below). **This is the power-on
  default.**
- `AGILENT` — an Agilent/HP 34401A-compatible command set (Chapter 4/§7–14 below). Commands from this
  set are simply invalid syntax while the instrument is still in `RIGOL` mode.
- `FLUKE` — a Fluke-45-compatible command set. Out of scope for this document (not used by anything
  in dev-term); mentioned only so you recognize the third option if `CMDSET?` ever returns it
  unexpectedly.

```
CMDSET AGILENT
CMDSET?          -> AGILENT
MEAS:VOLT:DC?    -> +1.234567E-01
```

Whether the instrument remembers this across a power cycle depends on `:SYSTem:CONFigure:POWEron`
(native set, §15) — `LAST` restores whatever was active when it last powered off, `DEF` always
restores the RIGOL-native factory default. **A script that wants Agilent-compatible mnemonics should
send `CMDSET AGILENT` once, itself, near the start of every session, rather than assuming the
instrument is already in that mode.**

## 2. IEEE 488 Common Commands

Shared identically across all three command sets — these are not RIGOL/Agilent/Fluke specific.

| Command | Type | Description |
|---|---|---|
| `*CLS` | Set | Clear status: resets Event Registers, clears the Error Queue |
| `*ESE` | Set/Query | Event Status Register enable mask (0–189) |
| `*ESR?` | Query | Current Event Status Register value |
| `*IDN?` | Query | Instrument identification string |
| `*OPC` | Set/Query | Operation-complete flag/query |
| `*PSC` | Set/Query | Power-on status clear behavior |
| `*RST` | Set | Reset the instrument to default state |
| `*SRE` | Set/Query | Status Byte Register enable mask (0–188) |
| `*STB?` | Query | Current Status Byte Register value |
| `*TRG` | Set | Software trigger while in "Wait-for-trigger" state |
| `*TST?` | Query | Self-test: `0` = pass, `1` = fail |
| `*WAI` | Set | Wait until all pending operations complete |

**`*IDN?`** — returns an identification string of at least 35 characters (manufacturer, model, serial
number, firmware — exact field layout not itemized in the source text beyond the length guarantee).
```
*IDN?
-> RIGOL TECHNOLOGIES,DM3058E,DM3xxxxxxxxxx,00.02.06
```

**`*RST`** — resets the instrument. Does not itself change the active `CMDSET`.

**`*PSC {0|1}`** — `0` = status registers keep their last state across a power cycle; `1` = registers
reset at power-on.

**`*OPC` / `*OPC?`** — `*OPC` sets bit 0 of ESR once the current operation completes; `*OPC?` blocks
and returns `"1"` once done. Prefer `*OPC?` over polling a measurement query to know when a
longer-running operation (e.g. a triggered `INITiate` sequence) has actually finished.

## 3. STATus Commands

Shared across all three command sets — the standard SCPI status-register model (Condition, Enable,
and Event registers feeding the Operation and Questionable Status queues, which roll up into
`*STB?`/`*ESR?`).

| Command | Type | Description |
|---|---|---|
| `STATus:OPERation:CONDition?` | Query | Operation register condition value |
| `STATus:OPERation:ENABle` | Set/Query | Operation register enable mask (0–1841) |
| `STATus:OPERation[:EVENt]?` | Query | Operation register event value |
| `STATus:PRESet` | Set | Resets Enable Registers of both status queues |
| `STATus:QUEStionable:CONDition?` | Query | Questionable-status condition value |
| `STATus:QUEStionable:ENABle` | Set/Query | Questionable-status enable mask (0–24375) |
| `STATus:QUEStionable[:EVENt]?` | Query | Questionable-status event value |

```
STATus:PRESet
STATus:OPERation:ENABle 256
STATus:OPERation:CONDition?
```

## 4. SYSTem Commands (shared)

Shared, general system commands — distinct from the RIGOL-native `:SYSTem:*` commands in §15, which
cover instrument configuration rather than these basics.

| Command | Type | Description |
|---|---|---|
| `SYSTem:BEEPer` | Set | Issue one immediate beep |
| `SYSTem:BEEPer:STATe` | Set/Query | Beeper enabled/disabled |
| `SYSTem:ERRor?` | Query | Dequeue one error from the error queue |
| `SYSTem:VERSion?` | Query | SCPI version string |

**`SYSTem:BEEPer`** — beeps once immediately; a no-op if the beeper has been disabled via
`SYSTem:BEEPer:STATe OFF` (re-enable first).

**`SYSTem:BEEPer:STATe {ON|OFF|1|0}`** — query returns `1`/`0`.

**`SYSTem:ERRor?`** — returns `<code>,"<description>"`, or `0,"No error"` once the queue is empty.
Call it in a loop after a command sequence to drain and inspect every pending error.
```
SYSTem:ERRor?
-> 0,"No error"
```

**`SYSTem:VERSion?`** — returns the supported SCPI version, e.g. `"1999.0"`.

---

The remaining sections cover the **Agilent-34401A-compatible command set** (§5–14) — the primary
reference for this document, since it matches the mnemonics already used by dev-term's bundled
`rigol-dm3058e.json` profile — followed by the **RIGOL-native command set** (§15) as a secondary
reference. **Remember: `CMDSET AGILENT` must be sent first**, or none of §5–14 will be recognized.

## 5. CONFigure Commands (Agilent-compatible)

Presets a measurement function, range, and resolution **without** triggering a measurement — the
family the existing profile's `confVoltDc` command belongs to.

| Command | Type | Description |
|---|---|---|
| `CONFigure?` | Query | Return the currently configured function/range/resolution |
| `CONFigure:VOLTage:DC` | Set | Configure DC voltage |
| `CONFigure:VOLTage:AC` | Set | Configure AC voltage |
| `CONFigure:CURRent:DC` | Set | Configure DC current |
| `CONFigure:CURRent:AC` | Set | Configure AC current |
| `CONFigure:RESistance` | Set | Configure 2-wire resistance |
| `CONFigure:FRESistance` | Set | Configure 4-wire resistance |
| `CONFigure:FREQuency` | Set | Configure frequency |
| `CONFigure:PERiod` | Set | Configure period |
| `CONFigure:CONTinuity` | Set | Configure continuity (no parameters) |
| `CONFigure:DIODe` | Set | Configure diode test (no parameters) |

**`CONFigure:VOLTage:DC {<range>|MIN|MAX|DEF},{<resolution>|MIN|MAX|DEF}`**
`<range>` discrete `{200mV|2V|20V|200V|1000V|AUTO}`, default `AUTO`; `MIN`=0.2 (200mV), `MAX`=1000.
`<resolution>` per the resolution/rate table below, default 1 ppm × range.
```
CONFigure:VOLTage:DC 20,DEF
CONFigure?    -> "VOLT:DC  2.000000E-01,2.000000E-07"
```

**`CONFigure:VOLTage:AC`** — same shape; range `{200mV|2V|20V|200V|750V|AUTO}`, default `AUTO`,
`MIN`=0.2 (200mV), `MAX`=750.

**`CONFigure:CURRent:DC`** — range `{200µA|2mA|20mA|200mA|2A|10A|AUTO}`, default `AUTO`,
`MIN`=0.0002 (200µA), `MAX`=10.

**`CONFigure:CURRent:AC`** — range `{20mA|200mA|2A|10A|AUTO}`, default `AUTO`, `MIN`=0.02 (20mA),
`MAX`=10.

**`CONFigure:RESistance` / `:FRESistance`** — range `{200Ω|2kΩ|20kΩ|200kΩ|2MΩ|10MΩ|100MΩ|AUTO}`,
default `AUTO`, `MIN`=200, `MAX`=100000000. Both 2-wire and 4-wire share the same range table.

**`CONFigure:FREQuency {<range>|MIN|MAX|DEF},{<resolution>|MIN|MAX|DEF}`** — unlike every other
`CONFigure` command above, `<range>` here is a **consecutive real number** (not a discrete list),
20 Hz–1 MHz; `MIN`=20, `MAX`=1000000.

**`CONFigure:PERiod`** — `<range>` consecutive real, 1 µs–50 ms; `MIN`=0.000001, `MAX`=0.05. Default
is a fixed 50 ms — the only `CONFigure` command whose default is *not* auto-ranging.

**`CONFigure:CONTinuity` / `:DIODe`** — no parameters. Continuity's range is fixed at 2 kΩ.

**`CONFigure?`** — returns a quoted string: `"<function>  <range>,<resolution>"`.

## 6. MEASure Commands (Agilent-compatible)

Same `{<range>|MIN|MAX|DEF},{<resolution>|MIN|MAX|DEF}` parameter shape as `CONFigure` above — the
difference is that `MEASure` **presets range/resolution and immediately executes the measurement**,
sending the reading straight to the output buffer, in one call.

| Command | Type | Description |
|---|---|---|
| `MEASure:VOLTage:DC?` | Query | Configure + measure DC voltage |
| `MEASure:VOLTage:AC?` | Query | Configure + measure AC voltage |
| `MEASure:CURRent:DC?` | Query | Configure + measure DC current |
| `MEASure:CURRent:AC?` | Query | Configure + measure AC current |
| `MEASure:RESistance?` | Query | Configure + measure 2-wire resistance |
| `MEASure:FRESistance?` | Query | Configure + measure 4-wire resistance |
| `MEASure:FREQuency?` | Query | Configure + measure frequency |
| `MEASure:PERiod?` | Query | Configure + measure period |
| `MEASure:CONTinuity?` | Query | Measure continuity (no parameters, fixed 2 kΩ range) |
| `MEASure:DIODe?` | Query | Measure diode test (no parameters) |

Each `MEASure:<function>?` command's `{<range>|MIN|MAX|DEF},{<resolution>|MIN|MAX|DEF}` parameters
match exactly the same-named `CONFigure:<function>` command's range/resolution rules in §5.
```
MEASure:VOLTage:DC? 20,DEF
-> +1.234567E-01
```

## 7. READ? / INITiate / FETCh? Commands (Agilent-compatible)

Three different ways to get a reading, differing in whether/how internal memory is involved.

| Command | Type | Description |
|---|---|---|
| `INITiate` | Set | Idle → Wait-for-trigger; stores up to 512 readings in internal memory on trigger |
| `FETCh?` | Query | Transfer readings already in internal memory to the output buffer |
| `READ?` | Query | Idle → Wait-for-trigger, measure, send reading straight to the output buffer |

**`INITiate`** — no parameters, no query form. Moves the trigger state machine from Idle to
Wait-for-trigger; when triggered, measures and stores results in internal memory (up to 512 readings)
rather than sending them out immediately.

**`FETCh?`** — pulls whatever's already in internal memory (from a prior `INITiate`) out to the
output buffer. Does **not** trigger a new measurement — if nothing has been stored yet, this returns
stale or no data.

**`READ?`** — the "just get me a number" command: triggers a measurement and sends the result
directly to the output buffer, skipping the internal-memory stage entirely.
```
INITiate
*OPC?
FETCh?        -> +1.234567E-01
```

## 8. SENSe Commands (Agilent-compatible)

The literal-value range/resolution-setting family that `CONFigure`/`MEASure` are built on top of. The
`SENSe:` header itself is optional (`[SENSe:]`) throughout this section.

| Command | Type | Description |
|---|---|---|
| `[SENSe:]FUNCtion` | Set/Query | Active measurement function (quoted string, no default) |
| `[SENSe:]VOLTage:DC:RANGe` | Set/Query | DC voltage range |
| `[SENSe:]VOLTage:AC:RANGe` | Set/Query | AC voltage range |
| `[SENSe:]CURRent:DC:RANGe` | Set/Query | DC current range |
| `[SENSe:]CURRent:AC:RANGe` | Set/Query | AC current range |
| `[SENSe:]RESistance:RANGe` | Set/Query | 2-wire resistance range (affects 4-wire too) |
| `[SENSe:]FRESistance:RANGe` | Set/Query | 4-wire resistance range (affects 2-wire too) |
| `[SENSe:]FREQuency:VOLTage:RANGe` | Set/Query | Input voltage range for frequency measurement |
| `[SENSe:]PERiod:VOLTage:RANGe` | Set/Query | Input voltage range for period measurement |
| `[SENSe:]<function>:RANGe:AUTO` | Set/Query | Auto-ranging toggle for a given function |
| `[SENSe:]<function>:RESolution` | Set/Query | Measurement resolution for a given function |
| `[SENSe:]VOLTage:DC:NPLC` | Set/Query | Integration time (power-line cycles) for DC voltage |
| `[SENSe:]CURRent:DC:NPLC` | Set/Query | Integration time for DC current |
| `[SENSe:]RESistance:NPLC` | Set/Query | Integration time for 2-wire resistance |
| `[SENSe:]FRESistance:NPLC` | Set/Query | Integration time for 4-wire resistance |
| `[SENSe:]FREQuency:APERture` | Set/Query | Gate time for frequency measurement |
| `[SENSe:]PERiod:APERture` | Set/Query | Gate time for period measurement |
| `[SENSe:]DETector:BANDwidth` | Set/Query | Documented no-op |
| `[SENSe:]ZERO:AUTO` | Set/Query | Documented no-op (query always returns `"0"`) |

**`[SENSe:]FUNCtion "<function>"`** — **must be set explicitly; there is no default.** Valid values:
`VOLTage:DC`, `VOLTage:AC`, `CURRent:AC`, `CURRent:DC`, `FREQuency`, `PERiod`, `RESistance` (2-wire),
`FRESistance` (4-wire), `CONTinuity`, `DIODe`.
```
SENSe:FUNCtion "VOLTage:DC"
```

**`[SENSe:]VOLTage:DC:RANGe {<range>|MIN|MAX}`** — discrete `{200mV|2V|20V|200V|1000V}`, default 20V.
"If a 200mV range is required, enter 0.2" — values are entered in base units (volts), not scaled
shorthand.

**`[SENSe:]VOLTage:AC:RANGe`** — `{200mV|2V|20V|200V|750V}`, default 20V.

**`[SENSe:]CURRent:DC:RANGe`** — `{200µA|2mA|20mA|200mA|2A|10A}`, default 200mA.

**`[SENSe:]CURRent:AC:RANGe`** — `{20mA|200mA|2A|10A}`, default 200mA.

**`[SENSe:]RESistance:RANGe` / `:FRESistance:RANGe`** — `{200Ω|2kΩ|20kΩ|200kΩ|2MΩ|10MΩ|100MΩ}`,
default 200kΩ. "If a 2kΩ range is required, enter 2000" — values are entered in base ohms, not
kΩ-scaled shorthand. **Setting one affects both the 2-wire and 4-wire range simultaneously** — a real
device quirk, not independent settings.

**`[SENSe:]FREQuency:VOLTage:RANGe` / `[SENSe:]PERiod:VOLTage:RANGe`** — both reuse the AC-voltage
range table `{200mV|2V|20V|200V|750V}`, default 20V. This is the *input signal's voltage* range, not
a frequency or time range — easy to misread at a glance.

**`[SENSe:]<function>:RANGe:AUTO {ON|OFF}`** — available for `VOLTage:DC`, `VOLTage:AC`, `CURRent:DC`,
`CURRent:AC`, `RESistance`, `FRESistance`, `FREQuency:VOLTage`, `PERiod:VOLTage`.

**`[SENSe:]<function>:RESolution {<resolution>|MIN|MAX}`** — available for `VOLTage:DC`,
`VOLTage:AC`, `CURRent:DC`, `CURRent:AC`, `RESistance`, `FRESistance`. Default 1 ppm × range (3 ppm
for the two resistance functions).

**Resolution/rate table** (applies to the `<resolution>` parameter in §5–6 and to `:RESolution`
above):

| Resolution | Rate | NPLC |
|---|---|---|
| 100 ppm × range | Fast | 0.02 |
| 10 ppm × range | Medium | 0.2 |
| 3 ppm × range | Medium | 1 |
| 1 ppm × range | Slow | 10 |
| 0.3 ppm × range | Slow | 100 |

**`[SENSe:]VOLTage:DC:NPLC {0.02|0.2|1|10|100|MIN|MAX}`** — integration time in power-line cycles.
`MIN`=0.02, `MAX`=100, default 1. Same shape for `CURRent:DC:NPLC`, `RESistance:NPLC`,
`FRESistance:NPLC`.

**`[SENSe:]FREQuency:APERture {0.01|0.1|1|MIN|MAX}`** — gate time in seconds. `MIN`=10ms, `MAX`=1s,
default 100ms. Same shape for `PERiod:APERture`.

**`[SENSe:]DETector:BANDwidth`** and **`[SENSe:]ZERO:AUTO`** — both accepted but **documented as
having no practical effect** ("only responded without practical operation"). `ZERO:AUTO?` always
returns `"0"` regardless of what was set. Include these in a script only if you specifically want to
document intent — don't rely on them to change behavior.

## 9. CALCulate Commands (Agilent-compatible)

Math operations layered on top of whatever the active measurement function is.

| Command | Type | Description |
|---|---|---|
| `CALCulate:STATe` | Set/Query | Enable/disable the whole CALCulate subsystem |
| `CALCulate:FUNCtion` | Set/Query | Which math operation is active |
| `CALCulate:LIMit:LOWer` | Set/Query | Lower pass/fail limit |
| `CALCulate:LIMit:UPPer` | Set/Query | Upper pass/fail limit |
| `CALCulate:DB:REFerence` | Set/Query | dB reference value |
| `CALCulate:DBM:REFerence` | Set/Query | dBm reference resistance |
| `CALCulate:NULL:OFFSet` | Set/Query | Null (relative) offset |
| `CALCulate:AVERage:AVERage?` | Query | Running average |
| `CALCulate:AVERage:COUNt?` | Query | Sample count for statistics |
| `CALCulate:AVERage:MAXimum?` | Query | Running maximum |
| `CALCulate:AVERage:MINimum?` | Query | Running minimum |

**`CALCulate:STATe {OFF|ON}`** — must be `ON` before any `CALCulate:FUNCtion`-specific behavior
applies; this is a top-level gate on top of the function-specific ones below.

**`CALCulate:FUNCtion {NULL|DB|DBM|AVERage|LIMit}`**, default `NULL`. Maps to the RIGOL-native math
ops as: `NULL`↔`REL`, `LIMIT`↔`PF`; `DB`/`DBM`/`AVERAGE` map 1:1.

**`CALCulate:LIMit:LOWer {<value>|MINimum|MAXimum}`** / **`:UPPer`** — default 0 each; valid only when
`CALCulate:FUNCtion LIMIT` and `CALCulate:STATe ON`; range ±120% of the active function's max range.

**`CALCulate:DB:REFerence`** — −120..+120 dB, default 0; requires `CALCulate:FUNCtion DB` + `STATe
ON`.

**`CALCulate:DBM:REFerence`** — 2..8000 Ω, default 600 Ω; requires `CALCulate:FUNCtion DBM` + `STATe
ON`.

**`CALCulate:NULL:OFFSet`** — ±120% of max range; requires `CALCulate:FUNCtion NULL` + `STATe ON`.

**`CALCulate:AVERage:*?`** — statistics queries, valid only once `CALCulate:FUNCtion AVERage` + `STATe
ON`; readable at any time thereafter.
```
CALCulate:STATe ON
CALCulate:FUNCtion AVERage
CALCulate:AVERage:AVERage?    -> +1.234567E-01
CALCulate:AVERage:COUNt?      -> +25
```

## 10. TRIGger Commands (Agilent-compatible)

Distinct from the RIGOL-native `:TRIGger:*` commands in §17 — different mnemonics, different
parameter shapes.

| Command | Type | Description |
|---|---|---|
| `TRIGger:COUNt` | Set/Query | Number of triggers per `INITiate` |
| `TRIGger:DELay` | Set/Query | Delay before each measurement |
| `TRIGger:DELay:AUTO` | Set/Query | Automatic delay on/off |
| `TRIGger:SOURce` | Set/Query | Trigger source |

**`TRIGger:COUNt {<value>|MIN|MAX|INFinite}`** — 1–2000; the manual states parameters must be set
explicitly (no default documented).

**`TRIGger:DELay {<seconds>|MIN|MAX}`** — 0–3600 s; also explicitly "must be set."

**`TRIGger:DELay:AUTO {ON|OFF}`**.

**`TRIGger:SOURce {IMMediate|EXTernal|BUS}`** — `IMMediate` fires immediately (this is what
`MEASure`/`READ?` implicitly assume); `EXTernal` waits for the rear-panel external trigger input;
`BUS` waits for a software trigger via `*TRG`. **Gotcha**: after selecting a non-immediate source, the
instrument must actually be put into "waiting trigger" mode (e.g. via `INITiate`) or the source
selection itself is refused. To get back to immediate mode, run `CONFigure`/`MEASure?` again rather
than trying to set `TRIGger:SOURce IMMediate` directly mid-sequence. The manual specifically
recommends pairing `SENSe:<function>:RANGe:AUTO OFF` with an explicit `SENSe:<function>:RANGe` (or
`CONFigure`/`MEASure?`) whenever using `EXTernal`/`BUS` sources, rather than relying on auto-ranging.

## 11. SAMPle Commands (Agilent-compatible)

| Command | Type | Description |
|---|---|---|
| `SAMPle:COUNt` | Set/Query | Number of samples taken per trigger |

**`SAMPle:COUNt {<value>|MIN|MAX}`** — 1–2000. No default is documented for this one specifically.

## 12. DISPlay Commands (Agilent-compatible)

| Command | Type | Description |
|---|---|---|
| `DISPlay` | Set/Query | Enable/disable the display |
| `DISPlay:TEXT` | Set/Query | Custom text shown on the display |
| `DISPlay:TEXT:CLEar` | Set | Clear custom display text |

**`DISPlay {OFF|ON}`** — turns the whole front-panel display off/on (measurements still happen with
the display off).

**`DISPlay:TEXT "<quoted string>"`** — shows arbitrary text on the display, useful for annotating what
an automated test is currently doing. `DISPlay:TEXT:CLEar` removes it.

## 13. DATA / INPut / ROUTe Commands (Agilent-compatible)

Three small, mostly-inert groups — included for completeness, but the manual itself documents each
as non-functional or fixed-output on this instrument.

| Command | Type | Description |
|---|---|---|
| `DATA:FEED` | Set | Documented as accepted but non-functional |
| `DATA:FEED?` | Query | Always returns `"CALC"` |
| `DATA:POINts?` | Query | Number of readings in internal memory |
| `INPut:IMPedance:AUTO` | Set/Query | Documented no-op; query always returns `"0"` |
| `ROUTe:TERMinals?` | Query | Always returns `"FRON"` |

**`DATA:FEED RDG_STORE,{"CALCulate"|""}`** — the manual's own note describes this as "restricted by
working principle": the command is accepted syntactically but performs no real operation, and
`DATA:FEED?` always reports back `"CALC"` regardless of what was set.

**`DATA:POINts?`** — the one command in this group that does return real, useful information: the
count of readings currently held in internal memory (populated by `INITiate`).

**`INPut:IMPedance:AUTO`** — accepted but inert; the query always returns `"0"` no matter what was
last set.

**`ROUTe:TERMinals?`** — always returns `"FRON"` (front-panel terminals); the instrument has no
rear-terminal option to route to, so this is a fixed, informational-only query.

## 14. RIGOL-Native Command Set (Secondary Reference)

Everything below uses the **RIGOL-native command set** (`CMDSET RIGOL`, the power-on default) rather
than the Agilent-compatible one in §5–13 above. It's presented as a secondary reference because it's
what the instrument speaks out of the box, and it exposes a few things the Agilent-compatible set
doesn't cleanly offer — measurement-rate control, capacitance, and math statistics — but its range
parameters are **index-coded integers** (e.g. `{0|1|2|3|4}`), not literal values, which is a real,
easy-to-miss difference from §5–13. Case-insensitive; every command below requires a **mandatory
leading colon** (`:MEASure:...`), unlike the Agilent-compatible set's headers.

### `:FUNCtion` / `:FUNCtion2` — measurement function selection

| Command | Type | Description |
|---|---|---|
| `:FUNCtion?` | Query | Active main-display function |
| `:FUNCtion:VOLTage:DC` etc. | Set | Select main-display function (one command per function) |
| `:FUNCtion2?` | Query | Active vice-display (dual-display) function |
| `:FUNCtion2:VALUe1?` / `:VALUe2?` | Query | Main-display / vice-display measured values |
| `:FUNCtion2:VOLTage:DC` etc. | Set | Select vice-display function |
| `:FUNCtion2:ON?` | Query | Current vice-display function/state |
| `:FUNCtion2:CLEar` | Set | Disable vice-display (dual-display off) |

**`:FUNCtion?`** returns one of `DCV, ACV, DCI, ACI, RESISTANCE, CAPACITANCE, CONTINUITY,
FRESISTANCE, DIODE, FREQUENCY, PERIOD`. Select a function with the matching bare set command:
`:FUNCtion:VOLTage:DC`, `:FUNCtion:VOLTage:AC`, `:FUNCtion:CURRent:DC`, `:FUNCtion:CURRent:AC`,
`:FUNCtion:RESistance` (2-wire), `:FUNCtion:FRESistance` (4-wire), `:FUNCtion:FREQuency`,
`:FUNCtion:PERiod`, `:FUNCtion:CONTinuity`, `:FUNCtion:DIODe`, `:FUNCtion:CAPacitance` — none take a
parameter; the function selection *is* the command.

**Gotcha**: selecting a vice-display function via `:FUNCtion2:*` restricts which main-display
functions remain legal afterward — e.g. after `:FUNCtion2:VOLTage:DC`, the main display can only be
DCV/DCI/ACV/ACI; after `:FUNCtion2:FREQuency`, the main display can only be ACV/FREQUENCY/PERIOD.
This is a genuine device interaction, not just documentation noise.

### `:MEASure` — native measure/range

Ranges here are discrete **index codes**, not literal values — this is the defining contrast with the
Agilent-compatible `MEASure`/`CONFigure` in §5–6.

| Command | Type | Description |
|---|---|---|
| `:MEASure?` | Query | Whether new data has been acquired under the current trigger setting |
| `:MEASure` | Set | Select Auto (`AUTO`) vs Manual (`MANU`) measurement type |
| `:MEASure:VOLTage:DC?` | Query | DC voltage reading |
| `:MEASure:VOLTage:DC` | Set/Query | DC voltage range (index `0`–`4`, default `2` = 20V) |
| `:MEASure:VOLTage:DC:RANGe?` | Query | Current DC voltage range index |
| `:MEASure:VOLTage:DC:IMPEdance` | Set/Query | Input impedance `{10M\|10G}` |
| `:MEASure:VOLTage:DC:FILTer[:STATe]` | Set/Query | AC filter under DC voltage |
| `:MEASure:VOLTage:AC?` / `:VOLTage:AC` / `:RANGe?` | — | AC voltage, same shape (index `0`–`4`) |
| `:MEASure:CURRent:DC?` / `:CURRent:DC` / `:RANGe?` | — | DC current (index `0`–`5`) |
| `:MEASure:CURRent:AC?` / `:CURRent:AC` / `:RANGe?` | — | AC current (index `0`–`3`) |
| `:MEASure:RESistance?` / `:RESistance` / `:RANGe?` | — | 2-wire resistance (index `0`–`6`) |
| `:MEASure:FRESistance?` / `:FRESistance` / `:RANGe?` | — | 4-wire resistance (same index table) |
| `:MEASure:FREQuency?` / `:FREQuency` / `:RANGe?` | — | Frequency (reuses AC-voltage index table) |
| `:MEASure:PERiod?` / `:PERiod` / `:RANGe?` | — | Period (reuses AC-voltage index table) |
| `:MEASure:CONTinuity?` / `:CONTinuity` | — | Continuity threshold, **real ohms, not index-coded** |
| `:MEASure:DIODe?` | Query | Diode test reading |
| `:MEASure:CAPacitance?` / `:CAPacitance` / `:RANGe?` | — | Capacitance (index `0`–`5`) |

**`:MEASure:VOLTage:DC {<range>|MIN|MAX|DEF}`** — index `{0|1|2|3|4}`, default `2`:
`0`=200mV (100nV res), `1`=2V (1µV), `2`=20V (10µV), `3`=200V (100µV), `4`=1000V (1mV); `MIN`=0,
`MAX`=4, `DEF`=2 (20V). Setting a range switches measurement type to Manual automatically.
`:MEASure:VOLTage:DC:RANGe?` (index `0..4`) requires DC voltage to have been used at least once
first. `:MEASure:VOLTage:DC:IMPEdance {10M|10G}` — `10G` (>10 GΩ) is only legal when range is 200mV
or 2V.

**`:MEASure:VOLTage:AC {<range>|MIN|MAX|DEF}`** — index `{0|1|2|3|4}`, default `2`: `0`=200mV,
`1`=2V, `2`=20V, `3`=200V, `4`=750V.

**`:MEASure:CURRent:DC {<range>|MIN|MAX|DEF}`** — index `{0..5}`. `0`=200µA (1nA), `1`=2mA (10nA),
`2`=20mA (100nA), `3`=200mA (1µA), `4`=2A (10µA), `5`=10A (100µA); `MIN`=0, `MAX`=5. The manual's own
default-value table shows a tension here — the parameter's "Default" column says `0`, but the row
the `DEF` *keyword* actually targets is 200mA (index `3`); both are preserved here as documented
rather than "corrected," since the source text itself has this apparent inconsistency.

**`:MEASure:CURRent:AC {<range>|MIN|MAX|DEF}`** — index `{0|1|2|3}`, default `1`: `0`=20mA, `1`=200mA,
`2`=2A, `3`=10A. (The manual's prose says the range query can return "0,1,2,3 or 4," but its own table
only lists 4 rows, 0–3 — likely a manual typo; the 4-row table is treated as authoritative here.)

**`:MEASure:RESistance {<range>|MIN|MAX|DEF}`** — index `{0..6}`, default `3`: `0`=200Ω, `1`=2kΩ,
`2`=20kΩ, `3`=200kΩ, `4`=1MΩ, `5`=10MΩ, `6`=100MΩ. `:MEASure:FRESistance` shares the same table.

**`:MEASure:FREQuency {<range>|MIN|MAX|DEF}`** / **`:MEASure:PERiod`** — index `{0..4}`, default `2`;
the range is actually the *input voltage* range (reusing the AC-voltage index table above), not a
frequency/period range. Usable measurement ranges: 20 Hz–1 MHz for frequency, 1 µs–50 ms for period.

**`:MEASure:CONTinuity {<range>|MIN|MAX|DEF}`** — a genuine **consecutive integer** 1–2000 Ω, default
10 Ω (the resistance threshold for the continuity beep) — not index-coded like everything else in this
section.

**`:MEASure:DIODe?`** — beep condition documented as 1V ≤ Vmeasured ≤ 2.4V by default.

**`:MEASure:CAPacitance {<range>|MIN|MAX|DEF}`** — index `{0..5}`, default `2`: `0`=2nF, `1`=20nF,
`2`=200nF, `3`=2µF, `4`=200µF, `5`=10000µF.

### `:RATE` — measurement speed

| Command | Type | Description |
|---|---|---|
| `:RATE:VOLTage:DC` etc. | Set/Query | `{F\|M\|S}` per function |
| `:RATE:SENSor` | Set/Query | `{M\|S}` — no Fast option |

**`:RATE:VOLTage:DC` / `:VOLTage:AC` / `:CURRent:DC` / `:CURRent:AC` / `:RESistance` /
`:FRESistance` {F|M|S}`** — each valid only when its function is the currently active one. `F` (Fast)
= 123 rdg/s (50 Hz refresh), `M` (Medium) = 20 rdg/s (20 Hz), `S` (Slow) = 2.5 rdg/s (2.5 Hz).
```
:FUNCtion:VOLTage:DC
:RATE:VOLTage:DC F
```

### `:CALCulate` — native math/statistics

| Command | Type | Description |
|---|---|---|
| `:CALCulate:FUNCtion` | Set/Query | Active math op: `{NONE\|REL\|DB\|DBM\|MIN\|MAX\|AVERAGE\|TOTAL\|PF}` |
| `:CALCulate:STATistic:MIN?` / `:MAX?` / `:AVERage?` / `:COUNt?` | Query | Statistics, need the matching op enabled |
| `:CALCulate:STATistic:STATe` | Set/Query | Statistics on/off |
| `:CALCulate:REL:OFFSet` / `:STATe` | Set/Query | Relative offset value/enable |
| `:CALCulate:DB?` / `:DB:REFErence` / `:DB:STATe` | — | dB result/reference/enable |
| `:CALCulate:DBM?` / `:DBM:REFErence` / `:DBM:STATe` | — | dBm result/reference/enable |
| `:CALCulate:PF?` / `:PF:LOWEr` / `:PF:UPPEr` / `:PF:STATe` | — | Pass/fail result/limits/enable |

**`:CALCulate:FUNCtion {NONE|REL|DB|DBM|MIN|MAX|AVERAGE|TOTAL|PF}`**, default `NONE`; a query can
return a combination like `REL+PF`; `TOTAL` turns on MIN+MAX+AVERAGE together. Most sub-features
below are only meaningful once the matching op is selected here — e.g. `:CALCulate:DB?` only returns
something sensible once `DB` is active.

**`:CALCulate:REL:OFFSet {<range>|MIN|MAX|DEF|CURR}`** — default 0; valid range/units depend on the
active measurement function: DC V ±1200 V, AC V ±900 V, DC/AC I ±12 A, Resistance ±1.2×10⁸ Ω,
Capacitance ±1.2×10⁻² F, Frequency ±1.2×10⁶ Hz.

**`:CALCulate:DB:REFErence {<range>|MIN|MAX|DEF}`** — −120..+120 dBm, default 0.

**`:CALCulate:DBM:REFErence`** — 2–8000 Ω, default 600 Ω.

**`:CALCulate:PF?`** — returns `PASS`, `HI`, or `LO`. **`:PF:LOWEr` / `:PF:UPPEr`** — default 0/1;
range depends on active function per: DC V ±1200 V, AC V 0–900 V, DC I ±12 A, AC I 0–12 A, Resistance
0–1.2×10⁸ Ω, Capacitance 0–1.2×10⁻² F, Period 1.0×10⁻⁶–100 s, Frequency 0–1.2×10⁶ Hz.

### `:SYSTem` — native instrument configuration

| Command | Type | Description |
|---|---|---|
| `:SYSTem:CONFigure:POWESwitch` | Set/Query | Require manual power-switch press after mains on |
| `:SYSTem:CONFigure:POWEron` | Set/Query | Power-on state: `{LAST\|DEF}` |
| `:SYSTem:CONFigure:DEFault` | Set | Reset system configuration now |
| `:SYSTem:LANGuage` | Set/Query | UI language `{CHINESE\|ENGLISH}` |
| `:SYSTem:FORMat:DECImal` | Set/Query | Decimal separator `{COMMA\|DOT}` |
| `:SYSTem:FORMat:SEPArate` | Set/Query | Data delimiter `{ON\|NONE\|SPACE}` |
| `:SYSTem:DISPlay:BRIGht` | Set/Query | Screen brightness `0`–`32`, default 22 |
| `:SYSTem:DISPlay:CONTrast` | Set/Query | Screen contrast `0`–`32`, default 19 |
| `:SYSTem:DISPlay:INVErt` | Set | Invert screen display (no query) |

**`:SYSTem:CONFigure:POWEron {LAST|DEF}`** — the setting that decides whether the instrument (and
critically, its `CMDSET`) comes back up in whatever state it was last in, or always resets to RIGOL
defaults. Relevant to every script in this document per §1's `CMDSET` discussion.

**`:SYSTem:FORMat:DECImal {COMMA|DOT}`** — the manual specifically warns this "causes easily the
format changes of data delimiter, please use it modestly" — i.e. changing it can have side effects on
how numeric replies are formatted; don't toggle it casually mid-script.

### `:TRIGger` — native trigger control

Distinct from the Agilent-compatible `TRIGger:*` in §10.

| Command | Type | Description |
|---|---|---|
| `:TRIGger:SOURce` | Set/Query | `{AUTO\|SINGLE\|EXT}` |
| `:TRIGger:AUTO:INTErval` | Set/Query | Auto-trigger interval (ms), range depends on active rate |
| `:TRIGger:AUTO:HOLD` | Set/Query | Auto-hold on/off |
| `:TRIGger:AUTO:HOLD:SENSitivity` | Set/Query | Hold sensitivity, index `{0..3}` |
| `:TRIGger:SINGle` | Set/Query | Sample count for single-trigger mode, `1`–`2000`, default 1 |
| `:TRIGger:SINGle:TRIGgered` | Set | Fire one manual single-trigger |
| `:TRIGger:EXT` | Set/Query | External trigger edge/level `{RISE\|FALL\|HIGH\|LOW}`, default `RISE` |
| `:TRIGger:VMComplete:POLAr` | Set/Query | Output polarity `{POS\|NEG}`, default `POS` |
| `:TRIGger:VMComplete:PULSewidth` | Set/Query | Pulse width (ms), range depends on active rate |

**`:TRIGger:AUTO:INTErval`** — legal range depends on the active `:RATE`: Fast 8–2000ms (default 8),
Medium 50–2000ms (default 50), Slow 400–2000ms (default 400; the overall power-on default, since Slow
is the default rate).

### RS-232 configuration (out of scope for this USB manual — see below)

**`:UTILity:INTErface:RS232:BAUD {1200|2400|4800|9600|19200|38400|57600|115200}`** and
**`:UTILity:INTErface:RS232:PARIty {NONE|ODD|EVEN}`** (query returns `NONE8BITS`, `ODD7BITS`, or
`EVEN7BITS`) exist and are relevant to the DM3058E specifically (RS-232 is supported on this model),
but are excluded from the main reference above per this document's USB scope — see "What's Excluded
and Why."

## Worked Example: Full Measurement Script

A typical automated sequence using the Agilent-compatible command set — select `CMDSET AGILENT`,
configure a function/range, take a reading, then check for errors:

```
*CLS                        ! clear status
CMDSET AGILENT              ! switch into the Agilent-compatible command set (not the default!)
*IDN?                       ! confirm you're talking to the right instrument
SYSTem:ERRor?                -> 0,"No error"

SENSe:FUNCtion "VOLTage:DC"
SENSe:VOLTage:DC:RANGe 20
SENSe:VOLTage:DC:NPLC 10
SENSe:VOLTage:DC:RANGe:AUTO OFF

READ?                        -> +1.234567E-01

SYSTem:ERRor?                -> 0,"No error"
```

Equivalent one-shot form using `MEASure` instead of the `SENSe`/`READ?` sequence above:
```
CMDSET AGILENT
MEASure:VOLTage:DC? 20,DEF   -> +1.234567E-01
```

## Common Gotchas

- **The instrument boots into RIGOL-native mode, not Agilent-compatible mode**, unless
  `:SYSTem:CONFigure:POWEron LAST` is active *and* it was last powered off in Agilent mode. Any script
  using §5–13's mnemonics should send `CMDSET AGILENT` itself, every session, rather than assuming.
  This is the single most common way a script that "used to work" against a factory-reset or
  power-cycled instrument suddenly gets nothing but syntax errors back.
- **The Agilent-compatible command set enforces stricter syntax than the RIGOL-native set** — extra
  whitespace, stray characters in a command tree, or a character following a parameter will make the
  instrument beep an execution error rather than silently tolerating it. If a command that looks
  correct is failing, check for exactly this before anything else.
- **RIGOL-native ranges are index codes (`0`,`1`,`2`,...); Agilent-compatible ranges are literal
  values (`200mV`,`2V`,`20V`,...)** — sending an index code to an Agilent-compatible `CONFigure`/
  `MEASure`/`SENSe` command (or vice versa) is a parameter-value error, not a syntax error, so it may
  fail less obviously than a typo would.
- **`CALCulate:STATe ON` (Agilent-compatible) is a separate, top-level gate on top of
  `CALCulate:FUNCtion`** — both must be set, in either order, before any function-specific math
  command (`DB:REFerence`, `LIMit:LOWer`, etc.) does anything meaningful.
- **Several Agilent-compatible commands are accepted but functionally inert on real hardware**:
  `INPut:IMPedance:AUTO`, `[SENSe:]DETector:BANDwidth`, `[SENSe:]ZERO:AUTO`, `DATA:FEED`/`DATA:FEED?`,
  `ROUTe:TERMinals?` (always `"FRON"`). They won't error, but they also won't do what their names
  suggest — don't build logic that depends on them actually changing instrument behavior.
- **`TRIGger:SOURce {EXTernal|BUS}` (Agilent-compatible) requires the instrument to actually be in
  "waiting trigger" state, or the source selection itself is refused.** Pair a non-immediate trigger
  source with manual ranging (`SENSe:<function>:RANGe:AUTO OFF` + an explicit range) rather than
  relying on auto-ranging, per the manual's own recommendation.
- **`:FUNCtion2:*` (RIGOL-native dual-display) restricts which main-display function remains legal**
  — setting a vice-display function can silently narrow what you're allowed to select as the main
  display afterward. If a subsequent `:FUNCtion:*` call unexpectedly fails, check whether a
  `:FUNCtion2:*` call earlier in the same session set up an incompatible pairing.
- **`SYSTem:ERRor?` (shared) dequeues one error per call** — call it in a loop until it returns
  `0,"No error"` to fully drain the queue; a single call after a multi-command sequence may leave
  earlier errors hidden behind later ones.

## What's Excluded and Why

- **GPIB and LAN interfaces, and the entire `:LXI` command subsystem** — excluded outright. The
  manual states twice, verbatim, that *"The GPIB and LAN interfaces are only supported by DM3058"* —
  the DM3058E has no LAN interface and no GPIB port at all, so `:UTILity:INTErface:GPIB:ADDRess`,
  `:UTILity:INTErface:LAN:*`, and every `:LXI:*` command are not a configuration choice, they're
  commands this exact model cannot execute regardless of interface. **If you were instead documenting
  a DM3058** (not the E variant), these would all belong back in the reference, plus a GPIB address
  command and the full LAN/mDNS/LXI configuration commands.
- **RS-232 configuration commands** (`:UTILity:INTErface:RS232:BAUD`/`:PARIty`) — present on the
  DM3058E, but out of scope here because this document is written for USB. They're listed at the end
  of §14 for reference. **If you switch this instrument to RS-232 instead of USB**, add those two
  commands back into your working set and set matching baud/parity on both ends before anything else
  will respond — same idea as any RS-232 instrument.
- **Fluke-45-compatible command set** (`CMDSET FLUKE`) — the manual documents a full third command
  vocabulary compatible with Fluke 45 multimeters. Excluded entirely since nothing in dev-term uses
  it and it serves the same purpose as the Agilent-compatible set (compatibility with a different
  vendor's existing scripts), just for a different vendor.
- **Chapter 6's eight worked "Application Examples" and the manual's Appendix of incompatible
  Agilent/Fluke commands** were reviewed but not transcribed verbatim here — their content is already
  folded into this document's own Worked Example and Common Gotchas sections rather than repeated as
  a separate, redundant example set.
