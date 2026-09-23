# HP 34401A Programming Manual — Chapter 4: Remote Interface Reference

Transcription of Chapter 4, "Remote Interface Reference" (pages 104-169 of
the original manual), rendered from `Hp-34401-CH4_RemoteProgramming.pdf`
(not in the repo — see the user's own manual copy). Primary source
material — transcribed as printed, not summarized. Figures/diagrams are
noted with placeholders, not redrawn.

This chapter is the SCPI command reference for the HP/Agilent/Keysight
34401A digital multimeter. Section headers, tables, command syntax,
NOTE/Caution callouts, and page breaks are preserved as printed.

<!-- page 103 (chapter divider, unnumbered) -->

# 4

## Remote Interface Reference

<!-- page 104 -->

## Remote Interface Reference

- Command Summary, *starting on page 105*
- Simplified Programming Overview, *starting on page 112*
- The MEASure? and CONFigure Commands, *starting on page 117*
- Measurement Configuration Commands, *starting on page 121*
- Math Operation Commands, *starting on page 124*
- Triggering, *starting on page 127*
- Triggering Commands, *starting on page 130*
- System-Related Commands, *starting on page 132*
- The SCPI Status Model, *starting on page 134*
- Status Reporting Commands, *starting on page 144*
- Calibration Commands, *on page 146*
- RS-232 Interface Configuration, *starting on page 148*
- RS-232 Interface Commands, *on page 153*
- An Introduction to the SCPI Language, *starting on page 154*
- Output Data Formats, *on page 159*
- Using Device Clear to Halt Measurements, *on page 160*
- TALK ONLY for Printers, *on page 160*
- To Set the HP-IB Address, *on page 161*
- To Select the Remote Interface, *on page 162*
- To Set the Baud Rate, *on page 163*
- To Set the Parity, *on page 164*
- To Select the Programming Language, *on page 165*
- Alternate Programming Language Compatibility, *starting on page 166*
- SCPI Compliance Information, *on page 168*
- IEEE-488 Compliance Information, *on page 169*

**[SCPI icon]** *If you are a first-time user of the SCPI language, you may want to refer to
these sections to become familiar with the language before attempting to
program the multimeter.*

<!-- page 105 -->

## Command Summary

This section summarizes the SCPI (*Standard Commands for Programmable
Instruments*) commands available to program the multimeter. Refer to the
later sections in this chapter for more complete details on each command.

> *Throughout this manual, the following conventions are used for SCPI
> command syntax. Square brackets ( `[ ]` ) indicate optional keywords or
> parameters. Braces ( `{ }` ) enclose parameters within a command string.
> Triangle brackets ( `< >` ) indicate that you must substitute a value for
> the enclosed parameter.*

**The MEASure? and CONFigure Commands**

*(see page 117 for more information)*

```
MEASure
  :VOLTage:DC? {<range>|MIN|MAX|DEF},{<resolution>|MIN|MAX|DEF}
  :VOLTage:DC:RATio? {<range>|MIN|MAX|DEF},{<resolution>|MIN|MAX|DEF}
  :VOLTage:AC? {<range>|MIN|MAX|DEF},{<resolution>|MIN|MAX|DEF}
  :CURRent:DC? {<range>|MIN|MAX|DEF},{<resolution>|MIN|MAX|DEF}
  :CURRent:AC? {<range>|MIN|MAX|DEF},{<resolution>|MIN|MAX|DEF}
  :RESistance? {<range>|MIN|MAX|DEF},{<resolution>|MIN|MAX|DEF}
  :FRESistance? {<range>|MIN|MAX|DEF},{<resolution>|MIN|MAX|DEF}
  :FREQuency? {<range>|MIN|MAX|DEF},{<resolution>|MIN|MAX|DEF}
  :PERiod? {<range>|MIN|MAX|DEF},{<resolution>|MIN|MAX|DEF}
  :CONTinuity?
  :DIODe?

CONFigure
  :VOLTage:DC {<range>|MIN|MAX|DEF},{<resolution>|MIN|MAX|DEF}
  :VOLTage:DC:RATio {<range>|MIN|MAX|DEF},{<resolution>|MIN|MAX|DEF}
  :VOLTage:AC {<range>|MIN|MAX|DEF},{<resolution>|MIN|MAX|DEF}
  :CURRent:DC {<range>|MIN|MAX|DEF},{<resolution>|MIN|MAX|DEF}
  :CURRent:AC {<range>|MIN|MAX|DEF},{<resolution>|MIN|MAX|DEF}
  :RESistance {<range>|MIN|MAX|DEF},{<resolution>|MIN|MAX|DEF}
  :FRESistance {<range>|MIN|MAX|DEF},{<resolution>|MIN|MAX|DEF}
  :FREQuency {<range>|MIN|MAX|DEF},{<resolution>|MIN|MAX|DEF}
  :PERiod {<range>|MIN|MAX|DEF},{<resolution>|MIN|MAX|DEF}
  :CONTinuity
  :DIODe

CONFigure?
```

<!-- page 106 -->

**Measurement Configuration Commands**

*(see page 121 for more information)*

```
[SENSe:]
  FUNCtion "VOLTage:DC"
  FUNCtion "VOLTage:DC:RATio"
  FUNCtion "VOLTage:AC"
  FUNCtion "CURRent:DC"
  FUNCtion "CURRent:AC"
  FUNCtion "RESistance"        (2-wire ohms)
  FUNCtion "FRESistance"       (4-wire ohms)
  FUNCtion "FREQuency"
  FUNCtion "PERiod"
  FUNCtion "CONTinuity"
  FUNCtion "DIODe"
  FUNCtion?

[SENSe:]
  VOLTage:DC:RANGe {<range>|MINimum|MAXimum}
  VOLTage:DC:RANGe? [MINimum|MAXimum]
  VOLTage:AC:RANGe {<range>|MINimum|MAXimum}
  VOLTage:AC:RANGe? [MINimum|MAXimum]
  CURRent:DC:RANGe {<range>|MINimum|MAXimum}
  CURRent:DC:RANGe? [MINimum|MAXimum]
  CURRent:AC:RANGe {<range>|MINimum|MAXimum}
  CURRent:AC:RANGe? [MINimum|MAXimum]
  RESistance:RANGe {<range>|MINimum|MAXimum}
  RESistance:RANGe? [MINimum|MAXimum]
  FRESistance:RANGe {<range>|MINimum|MAXimum}
  FRESistance:RANGe? [MINimum|MAXimum]
  FREQuency:VOLTage:RANGe {<range>|MINimum|MAXimum}
  FREQuency:VOLTage:RANGe? [MINimum|MAXimum]
  PERiod:VOLTage:RANGe {<range>|MINimum|MAXimum}
  PERiod:VOLTage:RANGe? [MINimum|MAXimum]

[SENSe:]
  VOLTage:DC:RANGe:AUTO {OFF|ON}
  VOLTage:DC:RANGe:AUTO?
  VOLTage:AC:RANGe:AUTO {OFF|ON}
  VOLTage:AC:RANGe:AUTO?
  CURRent:DC:RANGe:AUTO {OFF|ON}
  CURRent:DC:RANGe:AUTO?
  CURRent:AC:RANGe:AUTO {OFF|ON}
  CURRent:AC:RANGe:AUTO?
  RESistance:RANGe:AUTO {OFF|ON}
  RESistance:RANGe:AUTO?
  FRESistance:RANGe:AUTO {OFF|ON}
  FRESistance:RANGe:AUTO?
  FREQuency:VOLTage:RANGe:AUTO {OFF|ON}
  FREQuency:VOLTage:RANGe:AUTO?
  PERiod:VOLTage:RANGe:AUTO {OFF|ON}
  PERiod:VOLTage:RANGe:AUTO?
```

*Default parameters are shown in **bold**.*

<!-- page 107 -->

**Measurement Configuration Commands** *(continued)*

```
[SENSe:]
  VOLTage:DC:RESolution {<resolution>|MINimum|MAXimum}
  VOLTage:DC:RESolution? [MINimum|MAXimum]
  VOLTage:AC:RESolution {<resolution>|MINimum|MAXimum}
  VOLTage:AC:RESolution? [MINimum|MAXimum]
  CURRent:DC:RESolution {<resolution>|MINimum|MAXimum}
  CURRent:DC:RESolution? [MINimum|MAXimum]
  CURRent:AC:RESolution {<resolution>|MINimum|MAXimum}
  CURRent:AC:RESolution? [MINimum|MAXimum]
  RESistance:RESolution {<resolution>|MINimum|MAXimum}
  RESistance:RESolution? [MINimum|MAXimum]
  FRESistance:RESolution {<resolution>|MINimum|MAXimum}
  FRESistance:RESolution? [MINimum|MAXimum]

[SENSe:]
  VOLTage:DC:NPLCycles {0.02|0.2|1|10|100|MINimum|MAXimum}
  VOLTage:DC:NPLCycles? [MINimum|MAXimum]
  CURRent:DC:NPLCycles {0.02|0.2|1|10|100|MINimum|MAXimum}
  CURRent:DC:NPLCycles? [MINimum|MAXimum]
  RESistance:NPLCycles {0.02|0.2|1|10|100|MINimum|MAXimum}
  RESistance:NPLCycles? [MINimum|MAXimum]
  FRESistance:NPLCycles {0.02|0.2|1|10|100|MINimum|MAXimum}
  FRESistance:NPLCycles? [MINimum|MAXimum]

[SENSe:]
  FREQuency:APERture {0.01|0.1|1|MINimum|MAXimum}
  FREQuency:APERture? [MINimum|MAXimum]
  PERiod:APERture {0.01|0.1|1|MINimum|MAXimum}
  PERiod:APERture? [MINimum|MAXimum]

[SENSe:]
  DETector:BANDwidth {3|20|200|MINimum|MAXimum}
  DETector:BANDwidth? [MINimum|MAXimum]

[SENSe:]
  ZERO:AUTO {OFF|ONCE|ON}
  ZERO:AUTO?

INPut
  :IMPedance:AUTO {OFF|ON}
  :IMPedance:AUTO?

ROUTe:TERMinals?
```

*Default parameters are shown in **bold**.*

<!-- page 108 -->

**Math Operation Commands**

*(see page 124 for more information)*

```
CALCulate
  :FUNCtion {NULL|DB|DBM|AVERage|LIMit}
  :FUNCtion?
  :STATe {OFF|ON}
  :STATe?

CALCulate
  :AVERage:MINimum?
  :AVERage:MAXimum?
  :AVERage:AVERage?
  :AVERage:COUNt?

CALCulate
  :NULL:OFFSet {<value>|MINimum|MAXimum}
  :NULL:OFFSet? [MINimum|MAXimum]

CALCulate
  :DB:REFerence {<value>|MINimum|MAXimum}
  :DB:REFerence? [MINimum|MAXimum]

CALCulate
  :DBM:REFerence {<value>|MINimum|MAXimum}
  :DBM:REFerence? [MINimum|MAXimum]

CALCulate
  :LIMit:LOWer {<value>|MINimum|MAXimum}
  :LIMit:LOWer? [MINimum|MAXimum]
  :LIMit:UPPer {<value>|MINimum|MAXimum}
  :LIMit:UPPer? [MINimum|MAXimum]

DATA:FEED RDG_STORE, {"CALCulate"|""}
DATA:FEED?
```

<!-- page 109 -->

**Triggering Commands**

*(see page 127 for more information)*

```
INITiate

READ?

TRIGger
  :SOURce {BUS|IMMediate|EXTernal}
  :SOURce?

TRIGger
  :DELay {<seconds>|MINimum|MAXimum}
  :DELay? [MINimum|MAXimum]

TRIGger
  :DELay:AUTO {OFF|ON}
  :DELay:AUTO?

SAMPle
  :COUNt {<value>|MINimum|MAXimum}
  :COUNt? [MINimum|MAXimum]

TRIGger
  :COUNt {<value>|MINimum|MAXimum|INFinite}
  :COUNt? [MINimum|MAXimum]
```

**System-Related Commands**

*(see page 132 for more information)*

| | |
|---|---|
| `FETCh?` | `SYSTem:ERRor?` |
| `READ?` | `SYSTem:VERSion?` |
| `DISPlay {OFF\|ON}` | `DATA:POINts?` |
| `DISPlay?` | `*RST` |
| `DISPlay:TEXT <quoted string>` | `*TST?` |
| `DISPlay:TEXT?` | `*IDN?` |
| `DISPlay:TEXT:CLEar` | `L1` |
| `SYSTem:BEEPer` | `L2` |
| `SYSTem:BEEPer:STATe {OFF\|ON}` | `L3` |
| `SYSTem:BEEPer:STATe?` | |

*Default parameters are shown in **bold**.*

<!-- page 110 -->

**Status Reporting Commands**

*(see page 144 for more information)*

```
SYSTem:ERRor?

STATus
  :QUEStionable:ENABle <enable value>
  :QUEStionable:ENABle?
  :QUEStionable:EVENt?

STATus:PRESet

*CLS

*ESE <enable value>
*ESE?

*ESR?

*OPC

*OPC?

*PSC {0|1}
*PSC?

*SRE <enable value>
*SRE?

*STB?
```

**[Figure — "Questionable Data" / "Standard Event" / "Status Byte" / "Output
Buffer" / "Binary Weights" block diagram, thumbnail-sized on this Command
Summary page.]** This is the same SCPI Status System diagram reproduced
in full detail on page 135 — see that page for the complete bit-level
layout.

**Calibration Commands**

*(see page 146 for more information)*

```
CALibration?

CALibration:COUNt?

CALibration
  :SECure:CODE <new code>
  :SECure:STATe {OFF|ON},<code>
  :SECure:STATe?

CALibration
  :STRing <quoted string>
  :STRing?

CALibration
  :VALue <value>
  :VALue?
```

*Default parameters are shown in **bold**.*

<!-- page 111 -->

```
SYSTem:REMote
SYSTem:RWLock
```

**IEEE-488.2 Common Commands**

*(see page 169 for more information)*

```
*CLS
*ESE <enable value>
*ESE?
*ESR?
*IDN?
*OPC
*OPC?
*PSC {0|1}
*PSC?
*RST
*SRE <enable value>
*SRE?
*STB?
*TRG
*TST?
```

**RS-232 Interface Commands**

*(see page 148 for more information)*

```
SYSTem:LOCal
SYSTem:REMote
SYSTem:RWLock
```

*Default parameters are shown in **bold**.*

<!-- page 112 -->

## Simplified Programming Overview

**[SCPI icon]** *First-time SCPI users, see page 154.*

You can program the multimeter to take measurements from the remote
interface using the following simple seven-step sequence.

1. Place the multimeter in a known state (often the *reset* state).
2. Change the multimeter's settings to achieve the desired configuration.
3. Set-up the triggering conditions.
4. Initiate or arm the multimeter for a measurement.
5. Trigger the multimeter to make a measurement.
6. Retrieve the readings from the output buffer or internal memory.
7. Read the measured data into your bus controller.

The MEASure? and CONFigure commands provide the most straightforward
method to program the multimeter for measurements. You can select the
measurement function, range, and resolution all in one command. The
multimeter automatically *presets* other measurement parameters (ac
filter, autozero, trigger count, etc.) to default values as shown below.

**MEASure? and CONFigure Preset States**

| Command | MEASure? and CONFigure Setting |
|---|---|
| AC Filter (DET:BAND) | 20 Hz (medium filter) |
| Autozero (ZERO:AUTO) | OFF if resolution setting results in NPLC < 1; ON if resolution setting results in NPLC ≥ 1 |
| Input Resistance (INP:IMP:AUTO) | OFF (fixed at 10 MΩ for all dc voltage ranges) |
| Samples per Trigger (SAMP:COUN) | 1 sample |
| Trigger Count (TRIG:COUN) | 1 trigger |
| Trigger Delay (TRIG:DEL) | Automatic delay |
| Trigger Source (TRIG:SOUR) | Immediate |
| Math Function (CALCulate subsystem) | OFF |

<!-- page 113 -->

**Using the MEASure? Command**

The easiest way to program the multimeter for measurements is by using
the `MEASure?` command. However, this command does not offer much
flexibility. When you execute the command, the multimeter *presets* the
best settings for the requested configuration and immediately performs
the measurement. You cannot change any settings (other than function,
range, and resolution) before the measurement is taken. The results are
sent to the output buffer.

*Sending the `MEASure?` command is the same as sending a `CONFigure`
command followed immediately by a `READ?` command.*

**Using the CONFigure Command**

For a little more programming flexibility, use the `CONFigure` command.
When you execute the command, the multimeter *presets* the best settings
for the requested configuration (like the `MEASure?` command). However,
the measurement *is not* automatically started and you can change
measurement parameters before making measurements. This allows you to
"incrementally" change the multimeter's configuration from the *preset*
conditions. The multimeter offers a variety of low-level commands in the
`INPut`, `SENSe`, `CALCulate`, and `TRIGger` subsystems. (You can use the
`SENSe:FUNCtion` command to change the measurement function without
using `MEASure?` or `CONFigure`.)

*Use the `INITiate` or `READ?` command to initiate the measurement.*

<!-- page 114 -->

**Using the *range* and *resolution* Parameters**

With the `MEASure?` and `CONFigure` commands, you can select the
measurement function, range, and resolution all in one command. Use the
*range* parameter to specify the expected value of the input signal. The
multimeter then selects the correct measurement range.

For frequency and period measurements, the multimeter uses one "range"
for all inputs between 3 Hz and 300 kHz. The range parameter is required
only to specify the resolution. Therefore, it is not necessary to send a
new command for each new frequency to be measured.

Use the *resolution* parameter to specify the desired resolution for the
measurement. Specify the resolution in the same units as the measurement
function, *not in number of digits*. For example, for dc volts, specify
the resolution in volts. For frequency, specify the resolution in hertz.

*You must specify a range to use the resolution parameter.*

**Using the READ? Command**

The `READ?` command changes the state of the trigger system from the
"idle" state to the "wait-for-trigger" state. Measurements will begin
when the specified trigger conditions are satisfied following the
receipt of the `READ?` command. Readings are sent *immediately* to the
output buffer. You *must* enter the reading data into your bus
controller or the multimeter will stop making measurements when the
output buffer fills. Readings *are not* stored in the multimeter's
internal memory when using the `READ?` command.

*Sending the `READ?` command is like sending the `INITiate` command
followed immediately by the `FETCh?` command, except readings are not
buffered internally.*

<!-- page 115 -->

**Caution** — *If you send two query commands without reading the response
from the first, and then attempt to read the second response, you may
receive some data from the first response followed by the complete
second response. To avoid this, do not send a query command without
reading the response. When you cannot avoid this situation, send a
device clear before sending the second query command.*

**Using the INITiate and FETCh? Commands**

The `INITiate` and `FETCh?` commands provide the lowest level of control
(with the most flexibility) of measurement triggering and reading
retrieval. Use the `INITiate` command after you have configured the
multimeter for the measurement. This changes the state of the triggering
system from the "idle" state to the "wait-for-trigger" state.
Measurements will begin when the specified trigger conditions are
satisfied after the `INITiate` command is received. The readings *are*
placed in the multimeter's internal memory (up to 512 readings can be
stored). Readings *are stored* in memory until you are able to retrieve
them.

Use the `FETCh?` command to transfer the readings from the multimeter's
internal memory to the multimeter's output buffer where you can read
them into your bus controller.

**MEASure? Example**

The following program segment shows how to use the `MEASure?` command
to make a measurement. This example configures the multimeter for dc
voltage measurements, automatically places the multimeter in the
"wait-for-trigger" state, internally triggers the multimeter to take one
reading, and then sends the reading to the output buffer.

```
MEAS:VOLT:DC? 10,0.003
bus enter statement
```

This is the simplest way to take a reading. However, you do not have any
flexibility with `MEASure?` to set the trigger count, sample count,
trigger delay, etc. All measurement parameters except function, range,
and resolution are preset for you automatically (*see the table on page 112*).

<!-- page 116 -->

**CONFigure Example**

The following program segment shows how to use the `READ?` command with
`CONFigure` to make an externally-triggered measurement. The program
configures the multimeter for dc voltage measurements. `CONFigure` does
not place the multimeter in the "wait-for-trigger" state. The `READ?`
command places the multimeter in the "wait-for-trigger" state, takes a
reading when the *Ext Trig* terminal is pulsed, and sends the reading to
the output buffer.

```
CONF:VOLT:DC 10, 0.003
TRIG:SOUR EXT
READ?
bus enter statement
```

**CONFigure Example**

The following program segment is similar to the program above but it
uses `INITiate` to place the multimeter in the "wait-for-trigger" state.
The `INITiate` command places the multimeter in the "wait-for-trigger"
state, takes a reading when the *Ext Trig* terminal is pulsed, and sends
the reading to the multimeter's internal memory. The `FETCh?` command
transfers the reading from internal memory to the output buffer.

```
CONF:VOLT:DC 10, 0.003
TRIG:SOUR EXT
INIT
FETC?
bus enter statement
```

Storing readings in memory using the `INITiate` command is faster than
sending readings to the output buffer using the `READ?` command. The
multimeter can store up to 512 readings in internal memory. If you
configure the multimeter to take more than 512 readings (using the
sample count and trigger count), and then send `INITiate`, a memory
error is generated.

After you execute an `INITiate` command, no further commands are
accepted until the measurement sequence is completed. However, if you
select `TRIGger:SOURce BUS`, the multimeter will accept the `*TRG`
command (bus trigger) or an IEEE-488 *Group Execute Trigger* message.

<!-- page 117 -->

## The MEASure? and CONFigure Commands

*See also "Measurement Configuration," starting on page 51 in chapter 3.*

- For the *range* parameter, MIN selects the lowest range for the
  selected function; MAX selects the highest range; DEF selects
  autoranging.
- For the *resolution* parameter, specify the resolution in the same
  units as the measurement function, *not in number of digits*. MIN
  selects the smallest value accepted, which gives the best resolution;
  MAX selects the largest value accepted, which gives the least
  resolution; DEF selects the default resolution which is 5½ digits
  slow (10 PLC).

> *Note: You must specify a **range** to use the **resolution** parameter.*

**`MEASure:VOLTage:DC? {<range>|MIN|MAX|DEF},{<resolution>|MIN|MAX|DEF}`**
Preset and make a dc voltage measurement with the specified range and
resolution. The reading is sent to the output buffer.

**`MEASure:VOLTage:DC:RATio? {<range>|MIN|MAX|DEF},{<resolution>|MIN|MAX|DEF}`**
Preset and make a dc:dc ratio measurement with the specified range and
resolution. The reading is sent to the output buffer. For ratio
measurements, the specified range applies to the signal connected to
the **Input** terminals. Autoranging is automatically selected for
reference voltage measurements on the **Sense** terminals.

**`MEASure:VOLTage:AC? {<range>|MIN|MAX|DEF},{<resolution>|MIN|MAX|DEF}`**
Preset and make an ac voltage measurement with the specified range and
resolution. The reading is sent to the output buffer. For ac
measurements, resolution is actually fixed at 6½ digits. The
*resolution* parameter only affects the front-panel display.

**`MEASure:CURRent:DC? {<range>|MIN|MAX|DEF},{<resolution>|MIN|MAX|DEF}`**
Preset and make a dc current measurement with the specified range and
resolution. The reading is sent to the output buffer.

<!-- page 118 -->

**`MEASure:CURRent:AC? {<range>|MIN|MAX|DEF},{<resolution>|MIN|MAX|DEF}`**
Preset and make an ac current measurement with the specified range and
resolution. The reading is sent to the output buffer. For ac
measurements, resolution is actually fixed at 6½ digits. The
*resolution* parameter only affects the front-panel display.

**`MEASure:RESistance? {<range>|MIN|MAX|DEF},{<resolution>|MIN|MAX|DEF}`**
Preset and make a 2-wire ohms measurement with the specified range and
resolution. The reading is sent to the output buffer.

**`MEASure:FRESistance? {<range>|MIN|MAX|DEF},{<resolution>|MIN|MAX|DEF}`**
Preset and make a 4-wire ohms measurement with the specified range and
resolution. The reading is sent to the output buffer.

**`MEASure:FREQuency? {<range>|MIN|MAX|DEF},{<resolution>|MIN|MAX|DEF}`**
Preset and make a frequency measurement with the specified range and
resolution. The reading is sent to the output buffer. For frequency
measurements, the multimeter uses one "range" for all inputs between
3 Hz and 300 kHz. With no input signal applied, frequency measurements
return "0".

**`MEASure:PERiod? {<range>|MIN|MAX|DEF},{<resolution>|MIN|MAX|DEF}`**
Preset and make a period measurement with the specified range and
resolution. The reading is sent to the output buffer. For period
measurements, the multimeter uses one "range" for all inputs between
0.33 seconds and 3.3 µsec. With no input signal applied, period
measurements return "0".

**`MEASure:CONTinuity?`**
Preset and make a continuity measurement. The reading is sent to the
output buffer. The range and resolution are fixed for continuity tests
(1 kΩ range and 5½ digits).

**`MEASure:DIODe?`**
Preset and make a diode measurement. The reading is sent to the output
buffer. The range and resolution are fixed for diode tests (1 Vdc range
with 1 mA current source output and 5½ digits).

<!-- page 119 -->

**`CONFigure:VOLTage:DC {<range>|MIN|MAX|DEF},{<resolution>|MIN|MAX|DEF}`**
Preset and configure the multimeter for dc voltage measurements with the
specified range and resolution. This command *does not* initiate the
measurement.

**`CONFigure:VOLTage:DC:RATio {<range>|MIN|MAX|DEF},{<resolution>|MIN|MAX|DEF}`**
Preset and configure the multimeter for dc:dc ratio measurements with
the specified range and resolution. This command does not initiate the
measurement. For ratio measurements, the specified range applies to the
signal connected to the **Input** terminals. Autoranging is
automatically selected for reference voltage measurements on the
**Sense** terminals.

**`CONFigure:VOLTage:AC {<range>|MIN|MAX|DEF},{<resolution>|MIN|MAX|DEF}`**
Preset and configure the multimeter for ac voltage measurements with the
specified range and resolution. This command does not initiate the
measurement. For ac measurements, resolution is actually fixed at
6½ digits. The *resolution* parameter only affects the front-panel
display.

**`CONFigure:CURRent:DC {<range>|MIN|MAX|DEF},{<resolution>|MIN|MAX|DEF}`**
Preset and configure the multimeter for dc current measurements with the
specified range and resolution. This command does not initiate the
measurement.

**`CONFigure:CURRent:AC {<range>|MIN|MAX|DEF},{<resolution>|MIN|MAX|DEF}`**
Preset and configure the multimeter for ac current measurements with the
specified range and resolution. This command does not initiate the
measurement. For ac measurements, resolution is actually fixed at
6½ digits. The *resolution* parameter only affects the front-panel
display.

**`CONFigure:RESistance {<range>|MIN|MAX|DEF},{<resolution>|MIN|MAX|DEF}`**
Preset and configure the multimeter for 2-wire ohms measurements with
the specified range and resolution. This command does not initiate the
measurement.

**`CONFigure:FRESistance {<range>|MIN|MAX|DEF},{<resolution>|MIN|MAX|DEF}`**
Preset and configure the multimeter for 4-wire ohms measurements with
the specified range and resolution. This command does not initiate the
measurement.

<!-- page 120 -->

**`CONFigure:FREQuency {<range>|MIN|MAX|DEF},{<resolution>|MIN|MAX|DEF}`**
Preset and configure a frequency measurement with the specified range
and resolution. This command *does not* initiate the measurement. For
frequency measurements, the multimeter uses one "range" for all inputs
between 3 Hz and 300 kHz. With no input signal applied, frequency
measurements return "0".

**`CONFigure:PERiod {<range>|MIN|MAX|DEF},{<resolution>|MIN|MAX|DEF}`**
Preset and configure a period measurement with the specified range and
resolution. This command *does not* initiate the measurement. For
period measurements, the multimeter uses one "range" for all inputs
between 0.33 seconds and 3.3 µsec. With no input signal applied, period
measurements return "0".

**`CONFigure:CONTinuity`**
Preset and configure the multimeter for continuity measurements. This
command *does not* initiate the measurement. The range and resolution
are fixed for continuity tests (1 kΩ range and 5½ digits).

**`CONFigure:DIODe`**
Preset and configure the multimeter for diode measurements. This
command *does not* initiate the measurement. The range and resolution
are fixed for diode tests (1 Vdc range with 1 mA current source output
and 5½ digits).

**`CONFigure?`**
Query the multimeter's present configuration and return a quoted
string.

<!-- page 121 -->

## Measurement Configuration Commands

*See also "Measurement Configuration," starting on page 51 in chapter 3.*

**`FUNCtion "<function>"`**
Select a measurement function. The function must be enclosed in quotes
in the command string (`FUNC "VOLT:DC"`). Specify one of the following
strings.

| | |
|---|---|
| `VOLTage:DC` | `FRESistance` *(4-wire ohms)* |
| `VOLTage:DC:RATio` | `FREQuency` |
| `VOLTage:AC` | `PERiod` |
| `CURRent:DC` | `CONTinuity` |
| `CURRent:AC` | `DIODe` |
| `RESistance` *(2-wire ohms)* | |

**`FUNCtion?`**
Query the measurement function and return a quoted string.

**`<function>:RANGe {<range>|MINimum|MAXimum}`**
Select the range for the selected function. For frequency and period
measurements, ranging applies to the signal's input voltage, *not* its
frequency (use `FREQuency:VOLTage` or `PERiod:VOLTage`). MIN selects the
lowest range for the selected function. MAX selects the highest range.
*[Stored in volatile memory]*

**`<function>:RANGe? [MINimum|MAXimum]`**
Query the range for the selected function.

**`<function>:RANGe:AUTO {OFF|ON}`**
Disable or enable autoranging for the selected function. For frequency
and period, use `FREQuency:VOLTage` or `PERiod:VOLTage`. Autorange
thresholds: Down range at <10% of range; Up range at >120% of range.
*[Stored in volatile memory]*

**`<function>:RANGe:AUTO?`**
Query the autorange setting. Returns "0" (OFF) or "1" (ON).

<!-- page 122 -->

**`<function>:RESolution {<resolution>|MINimum|MAXimum}`**
Select the resolution for the specified function (not valid for
frequency, period, or ratio). Specify the resolution in the same units
as the measurement function, *not in number of digits*. MIN selects the
smallest value accepted, which gives the most resolution. MAX selects
the largest value accepted which gives the least resolution.
*[Stored in volatile memory]*

**`<function>:RESolution? [MINimum|MAXimum]`**
Query the resolution for the selected function. For frequency or period
measurements, the multimeter returns a resolution setting based upon a
3 Hz input frequency.

**`<function>:NPLCycles {0.02|0.2|1|10|100|MINimum|MAXimum}`**
Select the integration time in number of power line cycles for the
present function (the default is 10 PLC). This command is valid only
for dc volts, ratio, dc current, 2-wire ohms, and 4-wire ohms. MIN =
0.02. MAX = 100. *[Stored in volatile memory]*

**`<function>:NPLCycles? [MINimum|MAXimum]`**
Query the integration time for the selected function.

**`FREQuency:APERture {0.01|0.1|1|MINimum|MAXimum}`**
Select the aperture time (or gate time) for frequency measurements (the
default is 0.1 seconds). Specify 10 ms (4½ digits), **100 ms** (default;
5½ digits), or 1 second (6½ digits). MIN = 0.01 seconds. MAX = 1 second.
*[Stored in volatile memory]*

**`FREQuency:APERture? [MINimum|MAXimum]`**
Query the aperture time for frequency measurements.

**`PERiod:APERture {0.01|0.1|1|MINimum|MAXimum}`**
Select the aperture time (or gate time) for period measurements (the
default is 0.1 seconds). Specify 10 ms (4½ digits), **100 ms** (default;
5½ digits), or 1 second (6½ digits). MIN = 0.01 seconds. MAX = 1 second.
*[Stored in volatile memory]*

**`PERiod:APERture? [MINimum|MAXimum]`**
Query the aperture time for period measurements.

<!-- page 123 -->

**`[SENSe:]DETector:BANDwidth {3|20|200|MINimum|MAXimum}`**
Specify the lowest frequency expected in the input signal. The
multimeter selects the slow, medium (default), or fast ac filter based
on the frequency you specify. MIN = 3 Hz. MAX = 200 Hz.
*[Stored in volatile memory]*

**`[SENSe:]DETector:BANDwidth? [MINimum|MAXimum]`**
Query the ac filter. Returns "3", "20", or "200".

**`[SENSe:]ZERO:AUTO {OFF|ONCE|ON}`**
Disable or enable (default) the autozero mode. The OFF and ONCE
parameters have a similar effect. Autozero OFF *does not* issue a new
zero measurement until the next time the multimeter goes to the
"wait-for-trigger" state. Autozero ONCE issues an immediate zero
measurement. *[Stored in volatile memory]*

**`[SENSe:]ZERO:AUTO?`**
Query the autozero mode. Returns "0" (OFF or ONCE) or "1" (ON).

**`INPut:IMPedance:AUTO {OFF|ON}`**
Disable or enable the automatic input resistance mode for dc voltage
measurements. With AUTO OFF (default), the input resistance is fixed at
10 MΩ for all ranges. With AUTO ON, the resistance is set to >10 GΩ for
the 100 mV, 1 V, and 10 V ranges. *[Stored in volatile memory]*

**`INPut:IMPedance:AUTO?`**
Query the input resistance mode. Returns "0" (OFF) or "1" (ON).

**`ROUTe:TERMinals?`**
Query the multimeter to determine if the front or rear input terminals
are selected. Returns "FRON" or "REAR".

<!-- page 124 -->

## Math Operation Commands

*See also "Math Operations," starting on page 63 in chapter 3.*

There are five math operations available, only one of which can be
enabled at a time. Each math operation performs a mathematical
operation on each reading or stores data on a series of readings. The
selected math operation remains in effect until you disable it, change
functions, turn off the power, or perform a remote interface reset. The
math operations use one or more internal registers. You can preset the
values in some of the registers, while others hold the results of the
math operation.

The following table shows the math/measurement function combinations
allowed. Each "X" indicates an allowable combination. If you choose a
math operation that is not allowed with the present measurement
function, math is turned off. If you select a valid math operation and
then change to one that is invalid, a "Settings conflict" error is
generated over the remote interface. *For null and dB measurements, you
must turn on the math operation before writing to their math registers.*

| | DC V | AC V | DC I | AC I | Ω 2W | Ω 4W | Freq | Per | Cont | Diode | Ratio |
|---|---|---|---|---|---|---|---|---|---|---|---|
| **Null** | X | X | X | X | X | X | X | X | | | X |
| **Min-Max** | X | X | X | X | X | X | X | X | | | X |
| **dB** | X | X | | | | | | | | | |
| **dBm** | X | X | | | | | | | | | |
| **Limit** | X | X | X | X | X | X | X | X | | | X |

**`CALCulate:FUNCtion {NULL|DB|DBM|AVERage|LIMit}`**
Select the math function. Only one function can be enabled at a time.
The default function is null. *[Stored in volatile memory]*

**`CALCulate:FUNCtion?`**
Query the present math function. Returns NULL, DB, DBM, AVER, or LIM.

**`CALCulate:STATe {OFF|ON}`**
Disable or enable the selected math function. *[Stored in volatile memory]*

**`CALCulate:STATe?`**
Query the state of the math function. Returns "0" (OFF) or "1" (ON).

<!-- page 125 -->

**`CALCulate:AVERage:MINimum?`**
Read the minimum value found during a min-max operation. The multimeter
clears the value when min-max is turned on, when power has been off, or
after a remote interface reset. *[Stored in volatile memory]*

**`CALCulate:AVERage:MAXimum?`**
Read the maximum value found during a min-max operation. The multimeter
clears the value when min-max is turned on, when power has been off, or
after a remote interface reset. *[Stored in volatile memory]*

**`CALCulate:AVERage:AVERage?`**
Read the average of all readings taken since min-max was enabled. The
multimeter clears the value when min-max is turned on, when power has
been off, or after a remote interface reset. *[Stored in volatile memory]*

**`CALCulate:AVERage:COUNt?`**
Read the number of readings taken since min-max was enabled. The
multimeter clears the value when min-max is turned on, when power has
been off, or after a remote interface reset. *[Stored in volatile memory]*

**`CALCulate:NULL:OFFSet {<value>|MINimum|MAXimum}`**
Store a null value in the multimeter's Null Register. *You must turn on
the math operation before writing to the math register.* You can set
the null value to any number between 0 and ±120% of the highest range,
for the present function. MIN = –120% of the highest range. MAX = 120%
of the highest range. *[Stored in volatile memory]*

**`CALCulate:NULL:OFFSet? [MINimum|MAXimum]`**
Query the null value.

**`CALCulate:DB:REFerence {<value>|MINimum|MAXimum}`**
Store a relative value in the dB Relative Register. *You must turn on
the math operation before writing to the math register.* You can set
the relative value to any number between 0 dBm and ±200 dBm.
MIN = –200.00 dBm. MAX = 200.00 dBm. *[Stored in volatile memory]*

**`CALCulate:DB:REFerence? [MINimum|MAXimum]`**
Query the dB relative value.

<!-- page 126 -->

**`CALCulate:DBM:REFerence {<value>|MINimum|MAXimum}`**
Select the dBm reference value. Choose from: *50, 75, 93, 110, 124, 125,
135, 150, 250, 300, 500, **600**, 800, 900, 1000, 1200, or 8000 ohms.*
MIN = 50 Ω. MAX = 8000 Ω. *[Stored in non-volatile memory]*

**`CALCulate:DBM:REFerence? [MINimum|MAXimum]`**
Query the dBm reference resistance.

**`CALCulate:LIMit:LOWer {<value>|MINimum|MAXimum}`**
Set the lower limit for limit testing. You can set the value to any
number between 0 and ±120% of the highest range, for the present
function. MIN = –120% of the highest range. MAX = 120% of the highest
range. *[Stored in volatile memory]*

**`CALCulate:LIMit:LOWer? [MINimum|MAXimum]`**
Query the lower limit.

**`CALCulate:LIMit:UPPer {<value>|MINimum|MAXimum}`**
Set the lower limit for limit testing. You can set the value to any
number between 0 and ±120% of the highest range, for the present
function. MIN = –120% of the highest range. MAX = 120% of the highest
range. *[Stored in volatile memory]*

**`CALCulate:LIMit:UPPer? [MINimum|MAXimum]`**
Query the upper limit.

**`DATA:FEED RDG_STORE, {"CALCulate"|""}`**
Selects whether readings taken using the `INITiate` command are stored
in the multimeter's internal memory (default) or not stored at all. In
the default state (`DATA:FEED RDG_STORE, "CALC"`), up to 512 readings
are stored in memory when `INITiate` is executed. The `MEASure?` and
`CONFigure` commands automatically select `"CALC"`. With memory disabled
(`DATA:FEED RDG_STORE, ""`), readings taken using `INITiate` are not
stored. This may be useful with the min-max operation since it allows
you to determine an average of the readings without storing the
individual values. An error will be generated if you attempt to
transfer readings to the output buffer using the `FETCh?` command.

**`DATA:FEED?`**
Query the reading memory state. Returns `"CALC"` or `""`.

<!-- page 127 -->

## Triggering

**[SCPI icon]** *First-time SCPI users, see page 154.*

*See also "Triggering," starting on page 71 in chapter 3.*

The multimeter's triggering system allows you to generate triggers
either manually or automatically, take multiple readings per trigger,
and insert a delay before each reading. Normally, the multimeter will
take one reading each time it receives a trigger, but you can specify
multiple readings (up to 50,000) per trigger.

Triggering the multimeter from the remote interface is a multi-step
process that offers triggering flexibility.

- First, you must configure the multimeter for the measurement by
  selecting the function, range, resolution, etc.
- Then, you must specify the source from which the multimeter will
  accept the trigger. The multimeter will accept a software (bus)
  trigger from the remote interface, a hardware trigger from the
  rear-panel *Ext Trig* (external trigger) terminal, or an immediate
  internal trigger.
- Then, you must make sure that the multimeter is ready to accept a
  trigger from the specified trigger source (this is called the
  *wait-for-trigger* state).

*The diagram on the next page shows the multimeter's triggering system.*

<!-- page 128 -->

**[Figure — "HP 34401A Triggering System" state diagram.]** A flowchart
with four states/boxes connected top to bottom by arrows, redrawn here
as an equivalent state description:

- **Idle State** (circle) → entered at power-on and after each
  measurement sequence completes. Advances to **Wait-for-Trigger State**
  when one of the "Initiate Triggering" commands is received:
  `MEASure?`, `READ?`, or `INITiate`.
- **Wait-for-Trigger State** (circle) → advances to **Delay** once a
  trigger arrives from the selected "Trigger Source": `TRIGger:SOURce
  IMMediate`, `TRIGger:SOURce EXTernal`, `TRIGger:SOURce BUS`, or the
  front-panel "Single" key.
- **Delay** (rounded box) → a pause controlled by `TRIGger:DELay`, then
  advances to **Measurement Sample**.
- **Measurement Sample** (rounded box) → takes one reading (the front-panel
  "Sample (`*`)" annunciator lights during this box). From here:
  - If Sample Count ≠ 1, control loops back to **Delay** (another
    sample is taken without waiting for a new trigger).
  - If Trigger Count ≠ 1, control loops back to **Wait-for-Trigger
    State** (another trigger is required).
  - Otherwise, control returns to **Idle State**.

<!-- page 129 -->

**The Wait-for-Trigger State**

After you have configured the multimeter and selected a trigger source,
you must place the multimeter in the *wait-for-trigger* state. A
trigger will not be accepted until the multimeter is in this state. If
a trigger signal is present, and if multimeter is in the
"wait-for-trigger" state, the measurement sequence begins and readings
are taken.

*The "wait-for-trigger" state is a term used primarily for remote
interface operation. From the front panel, the multimeter is always in
the "wait-for-trigger" state and will accept triggers at any time,
unless a measurement is already in progress.*

You can place the multimeter in "wait-for-trigger" state by executing
any of the following commands from the remote interface.

```
MEASure?
READ?
INITiate
```

> *The multimeter requires approximately 20 ms of set-up time after you
> send a command to change to the "wait-for-trigger" state. Any external
> triggers that occur during this set-up time are ignored.*

<!-- page 130 -->

## Triggering Commands

*See also "Triggering," starting on page 71 in chapter 3.*

**`INITiate`**
Change the state of the triggering system from the "idle" state to the
"wait-for-trigger" state. Measurements will begin when the specified
trigger conditions are satisfied after the `INITiate` command is
received. The readings are placed in the multimeter's internal memory
(up to 512 readings can be stored). Readings *are stored* in memory
until you are able to retrieve them. Use the `FETCh?` command to
retrieve reading results.

> *A new command is available starting with firmware Revision 2 which
> allows you to take readings using `INITiate` without storing them in
> internal memory. This command may be useful with the min-max operation
> since it allows you to determine the average of a series of readings
> without storing the individual values.*
>
> ```
> DATA:FEED RDG_STORE, ""            do not store readings
> DATA:FEED RDG_STORE, "CALCulate"   store readings (default)
> ```
>
> *See page 126 for more information on using the `DATA:FEED` command.*

**`READ?`**
Change the state of the trigger system from the "idle" state to the
"wait-for-trigger" state. Measurements will begin when the specified
trigger conditions are satisfied following the receipt of the `READ?`
command. Readings are sent immediately to the output buffer.

**`TRIGger:SOURce {BUS|IMMediate|EXTernal}`**
Select the source from which the multimeter will accept a trigger. The
multimeter will accept a software (bus) trigger, an immediate internal
trigger (this is the default source), or a hardware trigger from the
rear-panel *Ext Trig* (external trigger) terminal. *[Stored in volatile
memory]*

**`TRIGger:SOURce?`**
Query the present trigger source. Returns "BUS", "IMM", or "EXT".

<!-- page 131 -->

**`TRIGger:DELay {<seconds>|MINimum|MAXimum}`**
Insert a trigger delay between the trigger signal and each sample that
follows. If you do not specify a trigger delay, the multimeter
automatically selects a delay for you. Select from 0 to 3600 seconds.
MIN = 0 seconds. MAX = 3600 seconds. *[Stored in volatile memory]*

**`TRIGger:DELay? [MINimum|MAXimum]`**
Query the trigger delay.

**`TRIGger:DELay:AUTO {OFF|ON}`**
Disable or enable an automatic trigger delay. The delay is determined
by function, range, integration time, and ac filter setting. Selecting
a specific trigger delay value automatically turns off the automatic
trigger delay. *[Stored in volatile memory]*

**`TRIGger:DELay:AUTO?`**
Query the automatic trigger delay setting. Returns "0" (OFF) or "1" (ON).

**`SAMPle:COUNt {<value>|MINimum|MAXimum}`**
Set the number of readings (samples) the multimeter takes per trigger.
Select from 1 to 50,000 readings per trigger. MIN = 1. MAX = 50,000.
*[Stored in volatile memory]*

**`SAMPle:COUNt? [MINimum|MAXimum]`**
Query the sample count.

**`TRIGger:COUNt {<value>|MINimum|MAXimum|INFinite}`**
Set the number of triggers the multimeter will accept before returning
to the "idle" state. Select from 1 to 50,000 triggers. The `INFinite`
parameter instructs the multimeter to continuously accept triggers (you
must send a device clear to return to the "idle" state). Trigger count
is ignored while in local operation. MIN = 1. MAX = 50,000.
*[Stored in volatile memory]*

**`TRIGger:COUNt? [MINimum|MAXimum]`**
Query the trigger count. If you specify an infinite trigger count, the
query command returns "9.90000000E+37".

<!-- page 132 -->

## System-Related Commands

*See also "System-Related Operations," starting on page 84 in chapter 3.*

**`FETCh?`**
Transfer readings stored in the multimeter's internal memory by the
`INITiate` command to the multimeter's output buffer where you can read
them into your bus controller.

**`READ?`**
Change the state of the trigger system from the "idle" state to the
"wait-for-trigger" state. Measurements will begin when the specified
trigger conditions are satisfied following the receipt of the `READ?`
command. Readings are sent immediately to the output buffer.

**`DISPlay {OFF|ON}`**
Turn the front-panel display off or on. *[Stored in volatile memory]*

**`DISPlay?`**
Query the front-panel display setting. Returns "0" (OFF) or "1" (ON).

**`DISPlay:TEXT <quoted string>`**
Display a message on the front panel. The multimeter will display up to
12 characters in a message; any additional characters are truncated.
*[Stored in volatile memory]*

**`DISPlay:TEXT?`**
Query the message sent to the front panel and return a quoted string.

**`DISPlay:TEXT:CLEar`**
Clear the message displayed on the front panel.

<!-- page 133 -->

**`SYSTem:BEEPer`**
Issue a single beep immediately.

**`SYSTem:BEEPer:STATe {OFF|ON}`**
Disable or enable the front-panel beeper. *[Stored in non-volatile memory]*

When you disable the beeper, the multimeter *will not* emit a tone when:
1. a new minimum or maximum is found in a min–max test.
2. a stable reading is captured in reading hold.
3. a limit is exceeded in a limit test.
4. a forward-biased diode is measured in the diode test function.

**`SYSTem:BEEPer:STATe?`**
Query the state of the front-panel beeper. Returns "0" (OFF) or "1" (ON).

**`SYSTem:ERRor?`**
Query the multimeter's error queue. Up to 20 errors can be stored in the
queue. Errors are retrieved in first-in-first out (FIFO) order. Each
error string may contain up to 80 characters.

**`SYSTem:VERSion?`**
Query the multimeter to determine the present SCPI version.

**`DATA:POINts?`**
Query the number of readings stored in the multimeter's internal
memory.

**`*RST`**
Reset the multimeter to its power-on configuration.

**`*TST?`**
Perform a complete self-test of the multimeter. Returns "0" if the
self-test is successful, or "1" if it test fails.

**`*IDN?`**
Read the multimeter's identification string (be sure to dimension a
string variable with at least 35 characters).

<!-- page 134 -->

## The SCPI Status Model

All SCPI instruments implement status registers in the same way. The
status system records various instrument conditions in three register
groups: the Status Byte register, the Standard Event register, and the
Questionable Data register. The status byte register records
high-level summary information reported in the other register groups.
The diagram on the next page illustrates the SCPI status system.

> *Chapter 6, "Application Programs," contains an example program
> showing the use of the status registers. You may find it useful to
> refer to the program after reading the following section in this
> chapter.*

**What is an *Event* Register?**

The standard event and questionable data registers have *event
registers*. An event register is a read-only register that reports
defined conditions within the multimeter. Bits in the event registers
*are* latched. Once an event bit is set, subsequent state changes are
ignored. Bits in an event register are automatically cleared by a query
of that register (such as `*ESR?` or `STAT:QUES:EVEN?`) or by sending
the `*CLS` (clear status) command. A reset (`*RST`) or device clear will
not clear bits in event registers. Querying an event register returns a
decimal value which corresponds to the binary-weighted sum of all bits
set in the register.

**What is an *Enable* Register?**

An *enable register* defines which bits in the corresponding event
register are logically ORed together to form a single summary bit.
Enable registers are both readable and writable. Querying an enable
register *will not* clear it. The `*CLS` (clear status) command does not
clear enable registers but it does clear the bits in the event
registers. The `STATus:PRESet` command *will* clear the questionable
data enable register. To enable bits in an enable register, you must
write a decimal value which corresponds to the binary-weighted sum of
the bits you wish to enable in the register.

<!-- page 135 -->

**[Figure — "SCPI Status System" block diagram.]** Full layout:

- **Questionable Data** block (left): a 16-bit Event Register (bits
  0-15) feeding a parallel Enable Register, both summed through an OR
  gate into bit 3 ("Questionable Data") of the Status Byte Summary
  Register. Event register bit labels: 0 Voltage Overload, 1 Current
  Overload, 2-8 Not Used, 9 Ohms Overload, 10 Not Used, 11 Limit Test
  Fail LO, 12 Limit Test Fail HI, 13-15 Not Used. Read via
  `STAT:QUES:EVEN?`; enable register set/read via
  `STAT:QUES:ENAB <value>` / `STAT:QUES:ENAB?`.
- **Standard Event** block (bottom left): an 8-bit Event Register
  (bits 0-7) feeding a parallel Enable Register, summed through an OR
  gate into bit 5 ("Standard Event") of the Status Byte Summary
  Register. Event register bit labels: 0 Operation Complete, 1 Not
  Used, 2 Query Error, 3 Device Error, 4 Execution Error, 5 Command
  Error, 6 Not Used, 7 Power On. Read via `*ESR?`; enable register
  set/read via `*ESE <value>` / `*ESE?`.
- **Status Byte** block (right): an 8-bit Summary Register (bits 0-7)
  feeding a parallel Enable Register, summed through an OR gate to
  produce the low-level IEEE-488 SRQ line. Summary register bit
  labels: 0-2 Not Used, 3 Questionable Data, 4 Message Available, 5
  Standard Event, 6 Request Service, 7 Not Used. Read via Serial Poll
  (SPOLL) or `*STB?`; enable register set/read via `*SRE <value>` /
  `*SRE?`. Note bit 6 ("Request Service") has no corresponding enable
  bit (crossed-out cell in the diagram) — it feeds the SRQ line
  directly.
- **Output Buffer** box (bottom right): a small FIFO icon, feeding the
  "Message Available" summary bit.
- **Binary Weights** reference table (top right): 2⁰=1, 2¹=2, 2²=4,
  2³=8, 2⁴=16, 2⁵=32, 2⁶=64, 2⁷=128, 2⁸=256, 2⁹=512, 2¹⁰=1024,
  2¹¹=2048, 2¹²=4096, 2¹³=8192, 2¹⁴=16384, 2¹⁵=32768.

<!-- page 136 -->

**The Status Byte**

The status byte *summary register* reports conditions from other status
registers. Query data that is waiting in the multimeter's output buffer
is immediately reported through the "message available" bit (bit 4).
Bits in the summary registers are *not* latched. Clearing an event
register will clear the corresponding bits in the status byte summary
register. Reading all messages in the output buffer, including any
pending queries, will clear the message available bit.

**Bit Definitions – Status Byte Register**

| Bit | Decimal Value | Definition |
|---|---|---|
| 0 Not Used | 1 | Always set to 0. |
| 1 Not Used | 2 | Always set to 0. |
| 2 Not Used | 4 | Always set to 0. |
| 3 Questionable Data | 8 | One or more bits are set in the Questionable Data register (bits must be "enabled" in enable register). |
| 4 Message Available | 16 | Data is available in the multimeter's output buffer. |
| 5 Standard Event | 32 | One or more bits are set in the Standard Event register (bits must be "enabled" in enable register). |
| 6 Request Service | 64 | The multimeter is requesting service (serial poll). |
| 7 Not Used | 128 | Always set to 0. |

The status byte *summary register* is cleared when:

- You execute a `*CLS` (clear status) command.
- Querying the standard event and questionable data registers will
  clear only the respective bits in the summary register.

The status byte *enable register* (request service) is cleared when:

- You turn on the power and you have previously configured the
  multimeter using the `*PSC 1` command.
- You execute a `*SRE 0` command.

The status byte enable register *will not* be cleared at power-on if
you have previously configured the multimeter using `*PSC 0`.

<!-- page 137 -->

**Using Service Request (SRQ) and Serial POLL**

You must configure your bus controller to respond to the IEEE-488
service request (SRQ) interrupt to use this capability. Use the status
byte enable register (SRE) to select which summary bits will set the
low-level IEEE-488 SRQ signal. When the status byte "request service"
bit (bit 6) is set, an IEEE-488 SRQ interrupt message is automatically
sent to the bus controller. The bus controller may then poll the
instruments on the bus to identify which one requested service (the one
with bit 6 set in its status byte). The request service bit is only
cleared by reading the status byte using an IEEE-488 serial poll or by
reading the event register whose summary bit is causing the service
request.

To read the status byte summary register, send the IEEE-488 serial poll
message. Querying the summary register will return a decimal value
which corresponds to the binary-weighted sum of the bits set in the
register. Serial poll will automatically clear the "request service"
bit in the status byte summary register. No other bits are affected.
Performing a serial poll will not affect instrument throughput.

**Caution** — *The IEEE-488.2 standard does not ensure synchronization
between your bus controller program and the instrument. Use the `*OPC?`
command to guarantee that commands previously sent to the instrument
have completed. Executing a serial poll before a `*RST`, `*CLS`, or
other commands have completed can cause previous conditions to be
reported.*

<!-- page 138 -->

**Using \*STB? to Read the Status Byte**

The `*STB?` (status byte query) command is similar to a serial poll
except it is processed like any other instrument command. The `*STB?`
command returns the same result as an IEEE-488 serial poll except that
the "request service" bit (bit 6) *is not* cleared if a serial poll has
occurred. The `*STB?` command is not handled automatically by the
IEEE-488 bus interface hardware and the command will be executed *only*
after previous commands have completed. Polling is not possible using
the `*STB?` command. Using the `*STB?` command does not clear the status
byte summary register.

**To Interrupt Your Bus Controller Using SRQ**

- Send a bus device clear message.
- Clear the event registers with the `*CLS` (clear status) command.
- Set the `*ESE` (standard event register) and `*SRE` (status byte
  register) enable masks.
- Send the `*OPC?` (operation complete query) command and enter the
  result to assure synchronization.
- Enable your bus controller's IEEE-488 SRQ interrupt.

**To Determine When a Command Sequence is Completed**

- Send a device clear message to clear the multimeter's output buffer.
- Clear the event registers with the `*CLS` (clear status) command.
- Enable "operation complete" using the `*ESE 1` command (standard
  event register).
- Send the `*OPC?` (operation complete query) command and enter the
  result to assure synchronization.
- Send your programming command string, and place the `*OPC`
  (operation complete) command as the last command.
- Use a serial poll to check to see when bit 5 (standard event) is set
  in the status byte summary register. You could also configure the
  multimeter for an SRQ interrupt by sending `*SRE 32` (status byte
  enable register, bit 5).

<!-- page 139 -->

**How to Use the Message Available Bit (MAV)**

You can use the status byte "message available" bit (bit 4) to
determine when data becomes available to read into your bus controller.
The multimeter sets bit 4 when the first reading trigger occurs (which
can be `TRIGger:SOURce:IMMediate`). The multimeter subsequently clears
bit 4 *only* after all messages have been read from the output buffer.

The message available (MAV) bit can only indicate when the *first*
reading is available following a `READ?` command. This can be helpful if
you do not know when a trigger event such as BUS or EXTernal will occur.

The MAV bit is set only after *all* specified measurements have
completed when using the `INITiate` command followed by `FETCh?`.
Readings are placed in the multimeter's internal memory when using
`INITiate`. Sending the `FETCh?` command transfers readings (stored in
internal memory by the `INITiate` command) to the multimeter's output
buffer. Therefore, the MAV bit can only be set after *all* measurements
have been completed.

**Using \*OPC to Signal When Data is in the Output Buffer**

Generally, it is best to use the "operation complete" bit (bit 0) in
the standard event register to signal when a command sequence is
completed. This bit is set in the register after an `*OPC` command has
been executed. If you send `*OPC` after a command which loads a message
in the multimeter's output buffer (either reading data or query data),
you can use the operation complete bit to determine when the message is
available. However, if too many messages are generated before the
`*OPC` command executes (sequentially), the output buffer will fill and
the multimeter will stop taking readings.

<!-- page 140 -->

**The Standard Event Register**

The *standard event* register reports the following types of instrument
events: power-on detected, command syntax errors, command execution
errors, self-test or calibration errors, query errors, or when an
`*OPC` command is executed. Any or all of these conditions can be
reported in the standard event summary bit through the enable register.
You must write a decimal value using the `*ESE` (event status enable)
command to set the enable register mask.

> *An error condition (standard event register bits 2, 3, 4, or 5) will
> always record one or more errors in the multimeter's error queue,
> except for the following case. Read the error queue using
> `SYSTem:ERRor?`.*
>
> *A reading overload condition is always reported in both the standard
> event register (bit 3) and the questionable data event register (bits
> 0, 1, or 9). However, no error message is recorded in the multimeter's
> error queue.*

**Bit Definitions – Standard Event Register**

| Bit | Decimal Value | Definition |
|---|---|---|
| 0 Operation Complete | 1 | All commands prior to and including an `*OPC` command have been executed. |
| 1 Not Used | 2 | Always set to 0. |
| 2 Query Error | 4 | The multimeter tried to read the output buffer but it was empty. Or, a new command line was received before a previous query has been read. Or, both the input and output buffers are full. |
| 3 Device Error | 8 | A self-test, calibration, or reading overload error occurred (see error numbers 501 through 748 in chapter 5). |
| 4 Execution Error | 16 | An execution error occurred (see error numbers -211 through -230 in chapter 5). |
| 5 Command Error | 32 | A command syntax error occurred (see error numbers -101 through -158 in chapter 5). |
| 6 Not Used | 64 | Always set to 0. |
| 7 Power On | 128 | Power has been turned off and on since the last time the event register was read or cleared. |

<!-- page 141 -->

The standard *event register* is cleared when:

- You send a `*CLS` (clear status) command.
- You query the event register using the `*ESR?` (event status
  register) command.

The standard event *enable register* is cleared when:

- You turn on the power and you have previously configured the
  multimeter using the `*PSC 1` command.
- You execute a `*ESE 0` command.

The standard event enable register *will not* be cleared at power-on if
you have previously configured the multimeter using `*PSC 0`.

<!-- page 142 -->

**The Questionable Data Register**

The *questionable data* register provides information about the
quality of the multimeter's measurement results. Overload conditions
and high/low limit test results are reported. Any or all of these
conditions can be reported in the questionable data summary bit through
the enable register. You must write a decimal value using the
`STATus:QUEStionable:ENABle` command to set the enable register mask.

> *Note: A reading overload condition is always reported in both the
> standard event register (bit 3) and the questionable data event
> register (bits 0, 1, or 9). However, no error message is recorded in
> the multimeter's error queue.*

**Bit Definitions – Questionable Data Register**

| Bit | Decimal Value | Definition |
|---|---|---|
| 0 Voltage Overload | 1 | Range overload on dc volts, ac volts, frequency, period, diode, or ratio function. |
| 1 Current Overload | 2 | Range overload on dc or ac current function. |
| 2 Not Used | 4 | Always set to 0. |
| 3 Not Used | 8 | Always set to 0. |
| 4 Not Used | 16 | Always set to 0. |
| 5 Not Used | 32 | Always set to 0. |
| 6 Not Used | 64 | Always set to 0. |
| 7 Not Used | 128 | Always set to 0. |
| 8 Not Used | 256 | Always set to 0. |
| 9 Ohms Overload | 512 | Range overload on 2-wire or 4-wire ohms. |
| 10 Not Used | 1024 | Always set to 0. |
| 11 Limit Fail LO | 2048 | Reading is less than lower limit in limit test. |
| 12 Limit Fail HI | 4096 | Reading exceeds upper limit in limit test. |
| 13 Not Used | 8192 | Always set to 0. |
| 14 Not Used | 16384 | Always set to 0. |
| 15 Not Used | 32768 | Always set to 0. |

<!-- page 143 -->

The questionable data *event register* is cleared when:

- You execute a `*CLS` (clear status) command.
- You query the event register using `STATus:QUEStionable:EVENt?`.

The questionable data *enable register* is cleared when:

- You turn on the power (`*PSC` does not apply).
- You execute the `STATus:PRESet` command.
- You execute the `STATus:QUEStionable:ENABle 0` command.

<!-- page 144 -->

## Status Reporting Commands

**`SYSTem:ERRor?`**
Query the multimeter's error queue. Up to 20 errors can be stored in
the queue. Errors are retrieved in first-in-first out (FIFO) order.
Each error string may contain up to 80 characters.

**`STATus:QUEStionable:ENABle <enable value>`**
Enable bits in the Questionable Data enable register. The selected bits
are then reported to the Status Byte.

**`STATus:QUEStionable:ENABle?`**
Query the Questionable Data enable register. The multimeter returns a
binary-weighted decimal representing the bits set in the enable
register.

**`STATus:QUEStionable:EVENt?`**
Query the Questionable Data event register. The multimeter returns a
decimal value which corresponds to the binary-weighted sum of all bits
set in the register.

**`STATus:PRESet`**
Clear all bits in the Questionable Data enable register.

**`*CLS`**
Clear the Status Byte summary register and all event registers.

**`*ESE <enable value>`**
Enable bits in the Standard Event enable register. The selected bits
are then reported to the Status Byte.

**`*ESE?`**
Query the Standard Event enable register. The multimeter returns a
decimal value which corresponds to the binary-weighted sum of all bits
set in the register.

<!-- page 145 -->

**`*ESR?`**
Query the Standard event register. The multimeter returns a decimal
value which corresponds to the binary-weighted sum of all bits set in
the register.

**`*OPC`**
Sets the "operation complete" bit (bit 0) in the Standard Event
register after the command is executed.

**`*OPC?`**
Returns "1" to the output buffer after the command is executed.

**`*PSC {0|1}`**
Power-on status clear. Clear the Status Byte and Standard Event
register enable masks when power is turned on (`*PSC 1`). When `*PSC 0`
is in effect, the Status Byte and Standard Event register enable masks
*are not* cleared when power is turned on. *[Stored in non-volatile
memory]*

**`*PSC?`**
Query the power-on status clear setting. Returns "0" (`*PSC 0`) or
"1" (`*PSC 1`).

**`*SRE <enable value>`**
Enable bits in the Status Byte enable register.

**`*SRE?`**
Query the Status Byte enable register. The multimeter returns a decimal
value which corresponds to the binary-weighted sum of all bits set in
the register.

**`*STB?`**
Query the Status Byte summary register. The `*STB?` command is similar
to a serial poll but it is processed like any other instrument
command. The `*STB?` command returns the same result as a serial poll
but the "request service" bit (bit 6) *is not* cleared if a serial poll
has occurred.

<!-- page 146 -->

## Calibration Commands

*See "Calibration Overview" starting on page 95 for an overview of the
calibration features of the multimeter. For a more detailed discussion
of the calibration procedures, see chapter 4 in the Service Guide.*

**`CALibration?`**
Perform a calibration using the specified calibration value
(`CALibration:VALue` command). Before you can calibrate the multimeter,
you must unsecure it by entering the correct security code.

**`CALibration:COUNt?`**
Query the multimeter to determine the number of times it has been
calibrated. Your multimeter was calibrated before it left the factory.
When you receive your multimeter, read the count to determine its
initial value. *[Stored in non-volatile memory]*

- The calibration count increments up to a maximum of 32,767 after
  which it wraps-around to 0. Since the value increments by one for
  each calibration point, a complete calibration will increase the
  value by many counts.

**`CALibration:SECure:CODE <new code>`**
Enter a new security code. To change the security code, you must first
unsecure the multimeter using the old security code, and then enter a
new code. The calibration code may contain up to 12 characters.
*[Stored in non-volatile memory]*

**`CALibration:SECure:STATe {OFF|ON},<code>`**
Unsecure or secure the multimeter for calibration. The calibration code
may contain up to 12 characters. *[Stored in non-volatile memory]*

**`CALibration:SECure:STATe?`**
Query the secured state of the multimeter. Returns "0" (OFF) or "1" (ON).

<!-- page 147 -->

**`CALibration:STRing <quoted string>`**
Record calibration information about your multimeter. For example, you
can store such information as the last calibration date, the next
calibration due date, the instrument serial number, or even the name
and phone number of the person to contact for a new calibration.
*[Stored in non-volatile memory]*

- You can record information in the calibration message only from the
  remote interface. However, you can read the message from either the
  front-panel menu or the remote interface.
- The calibration message may contain up to 40 characters. However,
  the multimeter can display only 12 characters of the message on the
  front panel (additional characters are truncated).

**`CALibration:STRing?`**
Query the calibration message and return a quoted string.

**`CALibration:VALue <value>`**
Specify the value of the known calibration signal used by the
calibration procedure.

**`CALibration:VALue?`**
Query the present calibration value.

<!-- page 148 -->

## RS-232 Interface Configuration

*See also "Remote Interface Configuration," on page 91 in chapter 3.*

You connect the multimeter to the RS-232 interface using the 9-pin
(DB-9) serial connector on the rear panel. The multimeter is configured
as a DTE (*Data Terminal Equipment*) device. For all communications
over the RS-232 interface, the multimeter uses two handshake lines: DTR
(*Data Terminal Ready*) on pin 4 and DSR (*Data Set Ready*) on pin 6.

The following sections contain information to help you use the
multimeter over the RS-232 interface. The programming commands for
RS-232 are listed on page 153.

**RS-232 Configuration Overview**

Configure the RS-232 interface using the parameters shown below. Use
the front-panel I/O MENU to select the baud rate, parity, and number of
data bits (*see also pages 163 and 164 for more information*).

- Baud Rate: 300, 600, 1200, 2400, 4800, or **9600 baud** *(factory
  setting)*
- Parity and Data Bits: **None / 8 data bits** *(factory setting)*;
  Even / 7 data bits, or Odd / 7 data bits
- Number of Start Bits: **1 bit** *(fixed)*
- Number of Stop Bits: **2 bits** *(fixed)*

**Caution** — *Do not use the RS-232 interface if you have configured the
multimeter to output pass/fail signals on pins 1 and 9. Internal
components on the RS-232 interface circuitry may be damaged.*

<!-- page 149 -->

**RS-232 Data Frame Format**

A character *frame* consists of all the transmitted bits that make up a
single character. The frame is defined as the characters from the
*start bit* to the last *stop bit*, inclusively. Within the frame, you
can select the baud rate, number of data bits, and parity type. The
multimeter uses the following frame formats for seven and eight data
bits.

**[Figure — RS-232 data frame format diagram.]** Two frame layouts shown
side by side:

- `PARITY = EVEN,ODD` — `Start Bit` | `7 Data Bits` | `Parity Bit` |
  `Stop Bit` | `Stop Bit`
- `PARITY = NONE` — `Start Bit` | `8 Data Bits` | `Stop Bit` | `Stop Bit`

**Connection to a Computer or Terminal**

To connect the multimeter to a computer or terminal, you must have the
proper interface cable. Most computers and terminals are DTE (*Data
Terminal Equipment*) devices. Since the multimeter is also a DTE
device, you must use a DTE-to-DTE interface cable. These cables are
also called *null-modem*, *modem-eliminator*, or *crossover* cables.

The interface cable must also have the proper connector on each end and
the internal wiring must be correct. Connectors typically have 9 pins
(DB-9 connector) or 25 pins (DB-25 connector) with a "male" or "female"
pin configuration. A male connector has pins inside the connector shell
and a female connector has holes inside the connector shell.

If you cannot find the correct cable for your configuration, you may
have to use a *wiring adapter*. If you are using a DTE-to-DTE cable,
make sure the adapter is a "straight-through" type. Typical adapters
include gender changers, null-modem adapters, and DB-9 to DB-25
adapters.

Refer to the cable and adapter diagrams on the following page to
connect the multimeter to most computers or terminals. If your
configuration is different than those described, order the *HP 34399A
Adapter Kit*. This kit contains adapters for connection to other
computers, terminals, and modems. Instructions and pin diagrams are
included with the adapter kit.

<!-- page 150 -->

**[Figure — DB-9 Serial Connection cable pin diagram.]** *If your
computer or terminal has a 9-pin serial port with a male connector, use
the null-modem cable included with the HP 34398A Cable Kit. This cable
has a 9-pin female connector on each end.* The diagram shows the
instrument's DB-9 male connector wired through an `F1047-80002` cable
(DB-9 female to DB-9 female) with the following crossover pairing to
the PC's DB-9 male connector:

| Instrument pin | Signal | ↔ | Signal | PC pin |
|---|---|---|---|---|
| 1 | DCD | — straight — | DCD | 1 |
| 2 | RX | ⤬ crossed with pin 3 | TX | 3 |
| 3 | TX | ⤬ crossed with pin 2 | RX | 2 |
| 4 | DTR | ⤬ crossed with pin 6 | DSR | 6 |
| 5 | GND | — straight — | GND | 5 |
| 6 | DSR | ⤬ crossed with pin 4 | DTR | 4 |
| 7 | RTS | ⤬ crossed with pin 8 | CTS | 8 |
| 8 | CTS | ⤬ crossed with pin 7 | RTS | 7 |
| 9 | RI | — straight — | RI | 9 |

**[Figure — DB-25 Serial Connection cable + adapter pin diagram.]** *If
your computer or terminal has a 25-pin serial port with a male
connector, use the null-modem cable and 25-pin adapter included with
the HP 34398A Cable Kit.* The instrument's DB-9 male connector goes
through the same `F1047-80002` cable (crossed as above) into a DB-9
male connector, which feeds a `5181-6641` adapter (DB-9 female to DB-25
female) that maps to the PC/printer's DB-25 male connector as follows:
adapter pin 1 (DCD) ↔ DB-25 pin 8, pin 2 (RX) ↔ DB-25 pin 3, pin 3 (TX)
↔ DB-25 pin 2, pin 4 (DTR) ↔ DB-25 pin 20, pin 5 (GND) ↔ DB-25 pin 7,
pin 6 (DSR) ↔ DB-25 pin 6, pin 7 (RTS) ↔ DB-25 pin 4, pin 8 (CTS) ↔
DB-25 pin 5, resulting in the PC/printer seeing TX/RX/RTS/CTS/DSR/GND/
DCD/DTR on its standard DB-25 pin assignments.

<!-- page 151 -->

**DTR / DSR Handshake Protocol**

The multimeter is configured as a DTE (*Data Terminal Equipment*)
device and uses the DTR (*Data Terminal Ready*) and DSR (*Data Set
Ready*) lines of the RS-232 interface to handshake. The multimeter uses
the DTR line to send a hold-off signal. The DTR line must be TRUE
before the multimeter will accept data from the interface. When the
multimeter sets the DTR line FALSE, the data must cease within 10
characters.

To disable the DTR/DSR handshake, *do not* connect the DTR line and tie
the DSR line to logic TRUE. If you disable the DTR/DSR handshake, also
select a slower baud rate (300, 600, or 1200 baud) to ensure that the
data is transmitted correctly.

*The multimeter sets the DTR line FALSE in the following cases:*

1. When the multimeter's input buffer is full (when approximately 100
   characters have been received), it sets the DTR line FALSE (pin 4 on
   the RS-232 connector). When enough characters have been removed to
   make space in the input buffer, the multimeter sets the DTR line
   TRUE, unless the second case (*see below*) prevents this.
2. When the multimeter wants to "talk" over the interface (which means
   that it has processed a query) and has received a *\<new line\>*
   message terminator, it will set the DTR line FALSE. This implies
   that once a query has been sent to the multimeter, the controller
   should read the response before attempting to send more data. It
   also means that a *\<new line\>* must terminate the command string.
   After the response has been output, the multimeter sets the DTR
   line TRUE again, unless the first case (*see above*) prevents this.

The multimeter monitors the DSR line to determine when the controller
is ready to accept data over the interface. The multimeter monitors the
DSR line (pin 6 on the RS-232 connector) before each character is
sent. The output is suspended if the DSR line is FALSE. When the DSR
line goes TRUE, transmission will resume.

<!-- page 152 -->

The multimeter holds the DTR line FALSE while output is suspended. A
form of interface *deadlock* exists until the controller asserts the
DSR line TRUE to allow the multimeter to complete the transmission. You
can break the interface deadlock by sending the *\<Ctrl-C\>* character,
which clears the operation in progress and discards pending output
(this is equivalent to the IEEE-488 device clear action). *For the
\<Ctrl-C\> character to be recognized reliably by the multimeter while
it holds DTR FALSE, the controller must first set DSR FALSE.*

In addition, you may have difficulty sending the *\<Ctrl-C\>* character
if you are interrupting a query operation, in which case the multimeter
hold the DTR line FALSE. This may prevent the controller from sending
anything unless you first reprogram the interface to ignore DTR.

**RS-232 Troubleshooting**

Here are a few things to check if you are having problems communicating
over the RS-232 interface. If you need additional help, refer to the
documentation that came with your computer.

- Verify that the multimeter and your computer are configured for the
  same baud rate, parity, and number of data bits. Make sure that your
  computer is set up for *1 start bit* and *2 stop bits* (these values
  are fixed on the multimeter).
- Make sure to execute the `SYSTem:REMote` command to place the
  multimeter in the REMOTE mode.
- Verify that you have connected the correct interface cable and
  adapters. Even if the cable has the proper connectors for your
  system, the internal wiring may not be correct. The *HP 34398A Cable
  Kit* can be used to connect the multimeter to most computers or
  terminals.
- Verify that you have connected the interface cable to the correct
  serial port on your computer (COM1, COM2, etc).

<!-- page 153 -->

## RS-232 Interface Commands

*Use the front-panel I/O MENU to select the baud rate, parity, and
number of data bits (see pages 163 and 164 for more information).*

**`SYSTem:LOCal`**
Place the multimeter in the *local* mode for RS-232 operation. All keys
on the front panel are fully functional.

**`SYSTem:REMote`**
Place the multimeter in the *remote* mode for RS-232 operation. All
keys on the front panel, except the LOCAL key, are disabled.

> *It is very important that you send the `SYSTem:REMote` command to
> place the multimeter in the remote mode. Sending or receiving data
> over the RS-232 interface when not configured for remote operation
> can cause unpredictable results.*

**`SYSTem:RWLock`**
Place the multimeter in the *remote* mode for RS-232 operation. This
command is the same as the `SYSTem:REMote` command except that *all
keys* on the front panel are disabled, including the LOCAL key.

**`Ctrl-C`**
Clear the operation in progress over the RS-232 interface and discard
any pending output data. *This is equivalent to the IEEE-488 device
clear action over the HP-IB interface.*

<!-- page 154 -->

## An Introduction to the SCPI Language

SCPI (*Standard Commands for Programmable Instruments*) is an
ASCII-based instrument command language designed for test and
measurement instruments. *Refer to "Simplified Programming Overview,"
starting on page 112, for an introduction to the basic techniques used
to program the multimeter over the remote interface.*

SCPI commands are based on a hierarchical structure, also known as a
*tree system*. In this system, associated commands are grouped together
under a common node or root, thus forming *subsystems*. A portion of
the SENSE subsystem is shown below to illustrate the tree system.

```
SENSe:
  VOLTage:
    DC:RANGe {<range>|MINimum|MAXimum}
  VOLTage:
    DC:RANGe? [MINimum|MAXimum]

  FREQuency:
    VOLTage:RANGe {<range>|MINimum|MAXimum}
  FREQuency:
    VOLTage:RANGe? [MINimum|MAXimum]

  DETector:
    BANDwidth {3|20|200|MINimum|MAXimum}
  DETector:
    BANDwidth? [MINimum|MAXimum]

  ZERO:
    AUTO {OFF|ONCE|ON}
  ZERO:
    AUTO?
```

`SENSe` is the root keyword of the command, `VOLTage` and `FREQuency`
are second-level keywords, and `DC` and `VOLTage` are third-level
keywords. A *colon* ( `:` ) separates a command keyword from a
lower-level keyword.

<!-- page 155 -->

**Command Format Used in This Manual**

The format used to show commands in this manual is shown below:

```
VOLTage:DC:RANGe {<range>|MINimum|MAXimum}
```

The command syntax shows most commands (and some parameters) as a
mixture of upper- and lower-case letters. The upper-case letters
indicate the abbreviated spelling for the command. For shorter program
lines, send the abbreviated form. For better program readability, send
the long form.

For example, in the above syntax statement, `VOLT` and `VOLTAGE` are
both acceptable forms. You can use upper- or lower-case letters.
Therefore, `VOLTAGE`, `volt`, and `Volt` are all acceptable. Other
forms, such as `VOL` and `VOLTAG`, will generate an error.

*Braces* ( `{ }` ) enclose the parameter choices for a given command
string. The braces are not sent with the command string.

A *vertical bar* ( `|` ) separates multiple parameter choices for a
given command string.

*Triangle brackets* ( `< >` ) indicate that you must specify a value for
the enclosed parameter. For example, the above syntax statement shows
the range parameter enclosed in triangle brackets. The brackets are not
sent with the command string. You must specify a value for the
parameter (such as `"VOLT:DC:RANG 10"`).

Some parameters are enclosed in *square brackets* ( `[ ]` ). The
brackets indicate that the parameter is optional and can be omitted.
The brackets are not sent with the command string. If you do not
specify a value for an optional parameter, the multimeter chooses a
default value.

<!-- page 156 -->

**Command Separators**

A *colon* ( `:` ) is used to separate a command keyword from a
lower-level keyword. You must insert a *blank space* to separate a
parameter from a command keyword. If a command requires more than one
parameter, you must separate adjacent parameters using a *comma* as
shown below:

```
"CONF:VOLT:DC 10, 0.003"
```

A *semicolon* ( `;` ) is used to separate commands within the *same*
subsystem, and can also minimize typing. For example, sending the
following command string:

```
"TRIG:DELAY 1; COUNT 10"
```

... is the same as sending the following two commands:

```
"TRIG:DELAY 1"
"TRIG:COUNT 10"
```

Use a colon *and* a semicolon to link commands from *different*
subsystems. For example, in the following command string, an error is
generated if you do not use both the colon *and* semicolon:

```
"SAMP:COUN 10;:TRIG:SOUR EXT"
```

**Using the *MIN* and *MAX* Parameters**

You can substitute `MINimum` or `MAXimum` in place of a parameter for
many commands. For example, consider the following command:

```
VOLTage:DC:RANGe {<range>|MINimum|MAXimum}
```

Instead of selecting a specific voltage range, you can substitute MIN
to set the range to its minimum value or MAX to set the range to its
maximum value.

<!-- page 157 -->

**Querying Parameter Settings**

You can query the current value of most parameters by adding a
*question mark* ( `?` ) to the command. For example, the following
command sets the sample count to 10 readings:

```
"SAMP:COUN 10"
```

You can query the sample count by executing:

```
"SAMP:COUN?"
```

You can also query the minimum or maximum count allowed as follows:

```
"SAMP:COUN? MIN"
"SAMP:COUN? MAX"
```

**Caution** — *If you send two query commands without reading the
response from the first, and then attempt to read the second response,
you may receive some data from the first response followed by the
complete second response. To avoid this, do not send a query command
without reading the response. When you cannot avoid this situation,
send a device clear before sending the second query command.*

**SCPI Command Terminators**

A command string sent to the multimeter *must* terminate with a
*\<new line\>* character. The IEEE-488 *EOI* (end-or-identify) message
is interpreted as a *\<new line\>* character and can be used to
terminate a command string in place of a *\<new line\>* character. A
*\<carriage return\>* followed by a *\<new line\>* is also accepted.
Command string termination will *always* reset the current SCPI
command path to the root level.

<!-- page 158 -->

**IEEE-488.2 Common Commands**

The IEEE-488.2 standard defines a set of *common commands* that perform
functions like reset, self-test, and status operations. Common commands
always begin with an asterisk ( `*` ), are four to five characters in
length, and may include one or more parameters. The command keyword is
separated from the first parameter by a *blank space*. Use a
*semicolon* ( `;` ) to separate multiple commands as shown below:

```
"*RST; *CLS; *ESE 32; *OPC?"
```

**SCPI Parameter Types**

The SCPI language defines several different data formats to be used in
program messages and response messages.

*Numeric Parameters* Commands that require numeric parameters will
accept all commonly used decimal representations of numbers including
optional signs, decimal points, and scientific notation. Special values
for numeric parameters like `MINimum`, `MAXimum`, and `DEFault` are also
accepted. You can also send engineering unit suffixes with numeric
parameters (e.g., M, K, or u). If only specific numeric values are
accepted, the multimeter will automatically round the input numeric
parameters. The following command uses a numeric parameter:

```
VOLTage:DC:RANGe {<range>|MINimum|MAXimum}
```

*Discrete Parameters* Discrete parameters are used to program settings
that have a limited number of values (like `BUS`, `IMMediate`,
`EXTernal`). They have a short form and a long form just like command
keywords. You can mix upper- and lower-case letters. Query responses
will *always* return the short form in all upper-case letters. The
following command uses discrete parameters:

```
TRIGger:SOURce {BUS|IMMediate|EXTernal}
```

<!-- page 159 -->

*Boolean Parameters* Boolean parameters represent a single binary
condition that is either true or false. For a false condition, the
multimeter will accept "OFF" or "0". For a true condition, the
multimeter will accept "ON" or "1". When you query a boolean setting,
the instrument will *always* return "0" or "1". The following command
uses a boolean parameter:

```
INPut:IMPedance:AUTO {OFF|ON}
```

*String Parameters* String parameters can contain virtually any set of
ASCII characters. A string *must* begin and end with matching quotes;
either with a single quote or with a double quote. You can include the
quote delimiter as part of the string by typing it twice without any
characters in between. The following command uses a string parameter:

```
DISPlay:TEXT <quoted string>
```

## Output Data Formats

Output data will be in one of formats shown in the table below.

| Type of Output Data | Output Data Format |
|---|---|
| Non-reading queries | < 80 ASCII character string |
| Single reading (IEEE-488) | `SD.DDDDDDDDESDD<nl>` |
| Multiple readings (IEEE-488) | `SD.DDDDDDDDESDD,...,...,<nl>` |
| Single reading (RS-232) | `SD.DDDDDDDDESDD<cr><nl>` |
| Multiple readings (RS-232) | `SD.DDDDDDDDESDD,...,...,<cr><nl>` |

| Symbol | Meaning |
|---|---|
| S | Negative sign or positive sign |
| D | Numeric digits |
| E | Exponent |
| `<nl>` | newline character |
| `<cr>` | carriage return character |

<!-- page 160 -->

## Using Device Clear to Halt Measurements

Device clear is an IEEE-488 low-level bus message which can be used to
halt measurements in progress. Different programming languages and
IEEE-488 interface cards provide access to this capability through
their own unique commands. The status registers, the error queue, and
all configuration states are left unchanged when a device clear message
is received. Device clear performs the following actions.

- All measurements in progress are aborted.
- The multimeter returns to the trigger "idle state."
- The multimeter's input and output buffers are cleared.
- The multimeter is prepared to accept a new command string.

For RS-232 operation, sending the *\<Ctrl-C\>* character will perform
the equivalent operations of the IEEE-488 device clear message. The
multimeter's DTR (data terminal ready) handshake line will be true
following a device clear message. *See "DTR/DSR Handshake Protocol," on
page 151 for further details.*

## TALK ONLY for Printers

You can set the address to "31" which is the *talk only* mode. In this
mode, the multimeter can output readings directly to a printer without
being addressed by a bus controller (over either HP-IB or RS-232). For
proper operation, make sure your printer is configured in the *listen
always* mode. Address 31 is not a valid address if you are operating
the multimeter from the HP-IB interface with a bus controller.

If you select the RS-232 interface and then set the HP-IB address to
the talk only address (31), the multimeter will *send* readings over
the RS-232 interface when in the local mode.

<!-- page 161 -->

## To Set the HP-IB Address

Each device on the HP-IB (IEEE-488) interface must have a unique
address. You can set the multimeter's address to any value between 0
and 31. The address is set to "**22**" when the multimeter is shipped
from the factory. The address is displayed on the front panel when you
turn on the multimeter. *See also "HP-IB Address," on page 91.*

**[Front-panel key sequence, "To Set the HP-IB Address"]**

| Step | Key(s) | Display |
|---|---|---|
| 1. Turn on the front-panel menu. | `Shift` + `<` *(On/Off)* | `A: MEAS MENU` |
| 2. Move across to the I/O MENU choice on this level. | `<` `<` | `E: I/O MENU` |
| 3. Move down a level to the HP-IB ADDR command. | `∨` | `1: HP-IB ADDR` |
| 4. Move down to the "parameter" level to set the address. Use the left/right and down/up arrow keys to change the address. | `∨` | `∧22 ADDR` |
| 5. Save the change and turn off the menu. The address is stored in *non-volatile* memory, and does not change when power has been off or after a remote interface reset. | `Auto/Man` *(ENTER)* | |

<!-- page 162 -->

## To Select the Remote Interface

The multimeter is shipped with both an HP-IB (IEEE-488) interface and
an RS-232 interface. Only one interface can be enabled at a time. The
HP-IB interface is selected when the multimeter is shipped from the
factory. *See also "Remote Interface Selection," on page 92.*

**[Front-panel key sequence, "To Select the Remote Interface"]**

| Step | Key(s) | Display |
|---|---|---|
| 1. Turn on the front-panel menu. | `Shift` + `<` *(On/Off)* | `A: MEAS MENU` |
| 2. Move across to the I/O MENU choice on this level. | `<` `<` | `E: I/O MENU` |
| 3. Move down a level and then across to the INTERFACE command. | `∨` `>` | `2: INTERFACE` |
| 4. Move down to the "parameter" level to select the interface. Use the left/right arrow keys to see the interface choices. Choose from the following: HP-IB / 488 or RS-232. | `∨` | `HP-IB / 488` |
| 5. Save the change and turn off the menu. The interface selection is stored in *non-volatile* memory, and does not change when power has been off or after a remote interface reset. | `Auto/Man` *(ENTER)* | |

<!-- page 163 -->

## To Set the Baud Rate

You can select one of six baud rates for RS-232 operation. The rate is
set to **9600 baud** when the multimeter is shipped from the factory.
*See also "Baud Rate Selection," on page 93.*

**[Front-panel key sequence, "To Set the Baud Rate"]**

| Step | Key(s) | Display |
|---|---|---|
| 1. Turn on the front-panel menu. | `Shift` + `<` *(On/Off)* | `A: MEAS MENU` |
| 2. Move across to the I/O MENU choice on this level. | `<` `<` | `E: I/O MENU` |
| 3. Move down a level and then across to the BAUD RATE command. | `∨` `>` `>` | `3: BAUD RATE` |
| 4. Move down to the "parameter" level to select the baud rate. Use the left/right arrow keys to see the baud rate choices. Choose from one of the following: 300, 600, 1200, 2400, 4800, or **9600** baud. | `∨` | `9600 BAUD` |
| 5. Save the change and exit the menu. The baud rate selection is stored in *non-volatile* memory, and does not change when power has been off or after a remote interface reset. | `Auto/Man` *(ENTER)* | |

<!-- page 164 -->

## To Set the Parity

You can select the parity for RS-232 operation. The multimeter is
configured for even parity with 7 data bits when shipped from the
factory. *See also "Parity Selection," on page 93.*

> The front-panel procedure below shows an "EVEN: 7 BITS" example
> reading — as printed, this appears to be the worked example shown in
> the procedure rather than the multimeter's factory default; page 148's
> own "RS-232 Configuration Overview" states the factory setting is
> **None / 8 data bits**. Both statements are transcribed here exactly
> as printed in the source manual; the discrepancy is in the original
> document, not introduced by this transcription.

**[Front-panel key sequence, "To Set the Parity"]**

| Step | Key(s) | Display |
|---|---|---|
| 1. Turn on the front-panel menu. | `Shift` + `<` *(On/Off)* | `A: MEAS MENU` |
| 2. Move across to the I/O MENU choice on this level. | `<` `<` | `E: I/O MENU` |
| 3. Move down a level and then across to the PARITY command. | `∨` `<` `<` | `4: PARITY` |
| 4. Move down to the "parameter" level to select the parity. Use the left/right arrow keys to see the parity choices. Choose from one of the following: None (8 data bits), **Even** (7 data bits), or Odd (7 data bits). When you set parity, you are indirectly setting the number of data bits. | `∨` | `EVEN: 7 BITS` |
| 5. Save the change and turn off the menu. The parity selection is stored in *non-volatile* memory, and does not change when power has been off or after a remote interface reset. | `Auto/Man` *(ENTER)* | |

<!-- page 165 -->

## To Select the Programming Language

You can select one of three languages to program the multimeter from
the selected remote interface. The language is **SCPI** when the
multimeter is shipped from the factory. *See also "Programming Language
Selection," on page 94.*

**[Front-panel key sequence, "To Select the Programming Language"]**

| Step | Key(s) | Display |
|---|---|---|
| 1. Turn on the front-panel menu. | `Shift` + `<` *(On/Off)* | `A: MEAS MENU` |
| 2. Move across to the I/O MENU choice on this level. | `<` `<` | `E: I/O MENU` |
| 3. Move down a level and then across to the LANGUAGE command. | `∨` `<` | `5: LANGUAGE` |
| 4. Move down to the "parameter" level to select the language. Choose from one of the following: **SCPI**, HP 3478A, or Fluke 8840A. | `∨` | `SCPI` |
| 5. Save the change and turn off the menu. The language selection is stored in *non-volatile* memory, and does not change when power has been off or after a remote interface reset. | `Auto/Man` *(ENTER)* | |

<!-- page 166 -->

## Alternate Programming Language Compatibility

You can configure the HP 34401A to accept and execute the commands of
either the HP 3478A multimeter or the Fluke 8840A/8842A multimeter.
Remote operation will only allow you to access the functionality of the
multimeter language selected. You can take advantage of the full
functionality of the HP 34401A only through the SCPI programming
language. For more information on selecting the alternate languages
from the front panel menu, see "To Select the Programming Language," on
the previous page. From the remote interface, use the following
commands to select the alternate languages:

```
L1   select SCPI language
L2   select HP 3478A language
L3   select Fluke 8840A language
```

Virtually all of the commands available for the other two multimeters
are implemented in the HP 34401A, with the exception of the self-test
and calibration commands. You must always calibrate the HP 34401A
using the SCPI language setting. The calibration commands from the
other two multimeters will not be executed.

> *Be aware that measurement timing may be different in the alternate
> language compatibility modes.*

**HP 3478A Language Setting**

All HP 3478A commands are accepted and executed by the HP 34401A with
equivalent operations, with the exception of the commands shown below.
Refer to your HP 3478A *Operating Manual* for further remote interface
programming information.

| HP 3478A Command | Description | HP 34401A Action |
|---|---|---|
| `C` | Perform a calibration. | Command is accepted but is ignored. |
| Device Clear | Perform a self-test and reset. | Self-test is not executed. |

<!-- page 167 -->

**Fluke 8840A/8842A Language Setting**

All Fluke 8840A or 8842A commands are accepted and executed by the
HP 34401A with equivalent operations, with the exception of the
commands shown below. Refer to your Fluke 8840A or 8842A *Instruction
Manual* for further remote interface programming information.

| Fluke 8840A Command | Description | HP 34401A Action |
|---|---|---|
| `G2` | GET calibration input prompt. | Generates Error 51 in 8840A/8842A. |
| `G4` | GET calibration status. | Returns "1000". |
| `G8` | Return identification string. | Returns "HEWLETT-PACKARD, 34401A,0,X-X-X" |
| `P2` | PUT variable calibration value. | Generates Error 51 in 8840A/8842A. |
| `P3` | PUT user-defined message. | Generates Error 51 in 8840A/8842A. |
| `Z0` | Perform self-test. | Self-test is not executed and no errors are recorded in the status byte. |
| `C0` | Store input as calibration value. | Generates Error 51 in 8840A/8842A. |
| `C1` | Begin A/D calibration. | Generates Error 51 in 8840A/8842A. |
| `C2` | Begin high-frequency AC calibration. | Generates Error 51 in 8840A/8842A. |
| `C3` | Enter ERASE mode. | Generates Error 51 in 8840A/8842A. |

<!-- page 168 -->

## SCPI Compliance Information

The following commands are device-specific to the HP 34401A. They are
not included in the 1991.0 version of the SCPI standard. However, these
commands are designed with the SCPI format in mind and they follow all
of the syntax rules of the standard.

> *Many of the required SCPI commands are accepted by the multimeter but
> are not described in this manual for simplicity or clarity. Most of
> these non-documented commands duplicate the functionality of a
> command already described in this chapter.*

```
CALCulate                                        MEASure
  :AVERage:MINimum?                                :CONTinuity?
  :AVERage:MAXimum?                                :DIODe?
  :AVERage:AVERage?
  :AVERage:COUNt?                                SAMPle
  :DB:REFerence {<value>|MINimum|MAXimum}          :COUNt {<value>|MINimum|MAXimum}
  :DB:REFerence? [MINimum|MAXimum]                 :COUNt? [MINimum|MAXimum]
  :DBM:REFerence {<value>|MINimum|MAXimum}
  :DBM:REFerence? [MINimum|MAXimum]              [SENSe:]
  :FUNCtion {NULL|DB|DBM|AVERage|LIMit}            FUNCtion "CONTinuity"
  :FUNCtion?                                       FUNCtion "DIODe"
  :LIMit:LOWer {<value>|MINimum|MAXimum}           FREQuency:VOLTage:RANGe {<range>|MINimum|MAXimum}
  :LIMit:LOWer? [MINimum|MAXimum]                  FREQuency:VOLTage:RANGe? [MINimum|MAXimum]
  :LIMit:UPPer {<value>|MINimum|MAXimum}           FREQuency:VOLTage:RANGe:AUTO {OFF|ON}
  :LIMit:UPPer? [MINimum|MAXimum]                  FREQuency:VOLTage:RANGe:AUTO?
  :NULL:OFFSet {<value>|MINimum|MAXimum}           PERiod:VOLTage:RANGe {<range>|MINimum|MAXimum}
  :NULL:OFFSet? [MINimum|MAXimum]                  PERiod:VOLTage:RANGe? [MINimum|MAXimum]
                                                    PERiod:VOLTage:RANGe:AUTO {OFF|ON}
CALibration                                        PERiod:VOLTage:RANGe:AUTO?
  :COUNt?                                          ZERO:AUTO?
  :SECure:CODE <new code>
  :SECure:STATe {OFF|ON},<code>                  SYSTem
  :SECure:STATe?                                   :LOCal
  :STRing <quoted string>                          :REMote
  :STRing?                                         :RWLock

CONFigure
  :CONTinuity
  :DIODe

INPut
  :IMPedance:AUTO {OFF|ON}
  :IMPedance:AUTO?
```

<!-- page 169 -->

## IEEE-488 Compliance Information

**Dedicated Hardware Lines**

| | |
|---|---|
| `ATN` | Attention |
| `IFC` | Interface Clear |
| `REN` | Remote Enable |
| `SRQ` | Service Request Interrupt |

**Addressed Commands**

| | |
|---|---|
| `DCL` | Device Clear |
| `EOI` | End or Identify Message Terminator |
| `GET` | Group Execute Trigger |
| `GTL` | Go to Local |
| `LLO` | Local Lock-Out |
| `SDC` | Selected Device Clear |
| `SPD` | Serial Poll Disable |
| `SPE` | Serial Poll Enable |

**IEEE-488.2 Common Commands**

| | |
|---|---|
| `*CLS` | `*RST` |
| `*ESE <enable value>` | `*SRE <enable value>` |
| `*ESE?` | `*SRE?` |
| `*ESR?` | `*STB?` |
| `*IDN?` | `*TRG` |
| `*OPC` | `*TST?` |
| `*OPC?` | |
| `*PSC {0\|1}` | |
| `*PSC?` | |
