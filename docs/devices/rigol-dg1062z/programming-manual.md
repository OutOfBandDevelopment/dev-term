# Rigol DG1062Z — Programming Manual (USB-TMC / LAN-LXI)

Dual-Channel Function/Arbitrary Waveform Generator, DG1000Z series. This manual covers the
SCPI command set reachable over the instrument's **USB-TMC** and **LAN (LXI)** interfaces. The
instrument also supports GPIB, but only as an add-on requiring an external USB-GPIB interface
converter — GPIB is out of scope here (see "What's excluded and why").

## Source document

**RIGOL Programming Guide — DG1000Z Series Function/Arbitrary Waveform Generator**, Feb. 2014,
Publication Number **PGB09103-1110**, RIGOL Technologies, Inc. This is the sole, canonical,
current vendor source for this instrument's command set and is the document to trust throughout.

The DG1000Z series covers two models:

| Model | Channels | Max. output frequency |
|---|---|---|
| DG1032Z | 2 | 30 MHz |
| **DG1062Z** | 2 | 60 MHz |

Per the guide's own note: *"Unless otherwise noted in this manual, DG1062Z is taken as an example
to introduce each command of the DG1000Z series."* This manual follows that convention — all
frequency/period ranges given below are DG1062Z's. Where the guide gives a per-model table
(waveform frequency ranges, pulse period/width lower limits), both models' values are included so
the DG1032Z case is visible too, but DG1062Z is the default assumption throughout.

## Before you start

### Physical connection

The DG1000Z rear panel has (among other connectors) a **USB Device** port and a **LAN** port
(RJ45, LXI-compliant). A front-panel **USB Host** port exists too, but it is for USB flash storage
or — with an external USB-GPIB converter — extending a GPIB interface; it is not used for USB-TMC
control directly.

- **USB-TMC**: connect the rear-panel USB Device port to a PC USB port with a standard USB
  cable. Windows will prompt to install a driver; accept the **"USB Test and Measurement Device
  (IVI)"** driver (installed automatically alongside Rigol's Ultra Sigma software, or via NI-VISA /
  Agilent IO Libraries Suite / Keysight IO Libraries).
- **LAN/LXI**: connect the rear-panel LAN port to your network (or directly to a PC) with a
  network cable. Configure addressing either from the front panel (`Utility` → `I/O Config` →
  `LAN`) or remotely once you already have one working interface, via the `:SYSTem:COMMunicate:LAN:*`
  commands below. The instrument is LXI-compliant and exposes an `:LXI` SCPI subsystem (identify
  indicator, mDNS) described below.

### Driver / software prerequisites

- Install Rigol's **Ultra Sigma** PC software (free, from rigol.com) — it bundles an NI-VISA
  runtime and gives you a "SCPI Panel Control" for ad-hoc command testing and to confirm
  connectivity before writing code.
- Any VISA-compliant library works (NI-VISA, Keysight/Agilent IO Libraries Suite). Programmatic
  access is via a standard VISA resource string:
  - USB-TMC: `USB0::0x1AB1::0x0642::<serial>::INSTR` (VID `0x1AB1` = Rigol, PID `0x0642` = this
    generator family; `<serial>` is the instrument's serial number, readable from the front panel
    under `Utility` → `I/O Config`, or via `:SYSTem:COMMunicate:USB:INFormation?`).
  - LAN/LXI: `TCPIP0::<ip_address>::INSTR` (raw VXI-11 style; the guide's example is
    `TCPIP0::172.16.2.13::INSTR`). A raw socket port is also available for LAN — see
    `:SYSTem:COMMunicate:LAN:CONTrol?` below.
- Confirm connectivity by sending `*IDN?` first — every worked example in the vendor guide leads
  with this.

### Model scope note

This manual documents the full DG1000Z SCPI command set as it applies to the DG1062Z, over
USB-TMC and LAN only. Commands that only make sense over GPIB, and the `:PA` (external power
amplifier accessory) subsystem, are intentionally excluded — see the closing section.

## Command syntax conventions

(From the guide's Chapter 1, "SCPI Command Overview".)

- **Tree structure**: commands are a root keyword plus one or more colon-separated sub-keywords,
  e.g. `:SYSTem:COMMunicate:LAN:IPADdress <ip_address>`. A leading `:` returns you to the command
  tree's root; you can chain multiple commands on one line separated by `;` (standard SCPI, not
  explicitly demonstrated in the guide but implied by the IEEE488.2 base it conforms to).
- **Query form**: append `?` to a command to query it, e.g. `:SYSTem:COMMunicate:LAN:IPADdress?`.
- **Parameter separator**: a space separates the command path from its first parameter;
  additional parameters are comma-separated, e.g. `:DISPlay:TEXT[:SET] <quoted string>[,x[,y]]`.
- **Braces `{ }`**: one of the enclosed, `|`-separated alternatives must be chosen, e.g.
  `{ON|OFF}`.
- **Square brackets `[ ]`**: the enclosed keyword or parameter is optional; if omitted, the
  instrument uses its default. This applies to whole keyword segments too — e.g.
  `[:SOURce[<n>]]:FREQuency[:FIXed]` can be sent as `:FREQ 1000` and is equivalent to
  `:SOUR1:FREQuency:FIXed 1000`.
- **`[<n>]` channel selector**: almost every `:SOURce`, `:OUTPut`, `:TRIGger` and similar command
  takes an optional channel number (`1` or `2`) immediately after the root keyword, e.g.
  `:SOURce2:FREQuency 1000` or the abbreviated `:SOUR2:FREQ 1000`. **Omitting it defaults to
  CH1** — this is true throughout the entire command set and is *not* repeated per-command below
  except where it matters.
- **Triangle brackets `< >`**: must be replaced by an actual value, e.g. `<value>`.
- **`MINimum`/`MAXimum`**: most numeric parameters accept the literal keyword `MINimum` or
  `MAXimum` in place of a value, to set (or, on a query, to ask for) that parameter's current
  lower/upper bound.
- **Parameter types**: Bool (`ON|1|OFF|0`), Integer, Real (floating point, within a stated
  effective range/precision), Discrete (one of an enumerated set), ASCII string.
- **Units**: frequency defaults to Hz, sample rate to Sa/s, amplitude to Vpp/Vrms/dBm (context
  dependent), offset to V, time to s, phase/duty-cycle/etc. to °/%. Alternate unit suffixes are
  accepted on set commands (e.g. `MHz`, `kHz`, `mVpp`) but query responses always come back in the
  base unit, in scientific notation with 7 significant digits (e.g. `1.000000E+02`).
- **Case-insensitivity / abbreviation**: all commands are case-insensitive. Long-form keywords may
  be abbreviated to just their capitalized letters, e.g. `FUNCtion:SQUare:DCYCle?` may be sent as
  `FUNC:SQU:DCYC?`. Mixed case in the guide's spelling (e.g. `SQUare`) exists purely to show you
  the valid abbreviation — `squ`, `SQU`, `Square` are all equally valid.

## Command reference

Commands are grouped by the vendor guide's own Chapter 2 structure, in the same order. Every
`:SOURce...` command implicitly accepts the `[<n>]` channel selector described above; per-command
entries below omit repeating that unless it has a channel-specific nuance.

### IEEE 488.2 Common Commands

Standard `*`-prefixed commands, three-to-four characters, per IEEE 488.2.

- **`*CLS`** — Clear the event registers of all register sets and the error queue.
- **`*ESE <value>` / `*ESE?`** — Set/query the bits (as a decimal sum-of-weights integer) enabled
  to report into the status byte register from the standard event register. `<value>=0` clears the
  enable register. See also `*PSC` (controls whether this is cleared at power-on).
- **`*ESR?`** — Query (and clear-on-read) the standard event register's event bits, as a decimal
  sum-of-weights integer. Related: `*CLS`.
- **`*IDN?`** — Query instrument identity. Returns 4 comma-separated fields: manufacturer, model,
  serial number, firmware version, e.g. `Rigol Technologies,DG1062Z,DG1ZA000000001,00.01.03`.
  Always send this first to confirm remote comms are working.
- **`*OPC` / `*OPC?`** — `*OPC` sets the OPC bit in the standard event register once all
  previously-sent commands finish executing (non-blocking on the bus). `*OPC?` blocks — it returns
  `1` to the output buffer only once all previously-sent commands complete; reading that response
  is a synchronization point. Commonly placed as the last command in a queued sequence, or used to
  synchronize with sweep/burst completion.
- **`*OPT?`** — Query whether the 16M internal memory option ("Arb 16M") is installed. Returns
  `OFFICAL` (installed) or `UNINSTALL` (not installed). **This gates deeper arbitrary-waveform
  memory depth** — see the licensing note under `:LICense:INSTall` and the closing section.
  **Confirmed installed on the DG1062Z this manual was written for** — expect `*OPT?` to return
  `OFFICAL` on that unit; don't assume this for a different DG1062Z without checking.
- **`*PSC {0|1}` / `*PSC?`** — `1` (default): the standard-event and status-byte enable registers
  are cleared at every power-on. `0`: they persist across power cycles. Related: `*ESE`, `*SRE`.
- **`*RCL {USER1..USER10|ARB1..ARB10}`** — Recall a state file (`USERn`) or arbitrary waveform
  file (`ARBn`) from one of the instrument's 10 internal non-volatile storage slots. Only valid if
  that slot actually holds a file. State files capture both channels' waveform/frequency/
  amplitude/offset/duty/symmetry/phase, modulation/sweep/burst parameters, frequency-counter
  settings, and Utility-menu system parameters. Arbitrary waveform files store 14-bit (`0x0000`–
  `0x3FFF`) voltage-per-point binary data, 2 bytes/point.
- **`*RST`** — Reset to factory defaults (see Appendix B of the vendor guide for the full factory
  table); unaffected by `:MEMory:STATe:RECall:AUTO`. Abnormally stops any in-progress sweep/burst
  and re-enables the screen if it was off (`:DISPlay[:STATe]`).
- **`*SAV {USER1..USER10|ARB1..ARB10}`** — Store current instrument state (`USERn`) or the current
  channel's arbitrary waveform data (`ARBn`) to internal non-volatile memory, default filename
  `Scpi<n>.RSF` / `Scpi<n>.RAF`. Overwrites silently unless the target slot is locked
  (`:MEMory:STATe:LOCK`), in which case the command is a no-op.
- **`*SRE <value>` / `*SRE?`** — Set/query bits enabled in the status byte register to raise a
  service request (accumulate on bit 6). `<value>=0` clears the enable register. Related: `*PSC`.
- **`*STB?`** — Query the status byte register (sum-of-weights decimal). Does not clear the
  service-request condition (bit 6 clears only once its underlying cause clears).
- **`*TRG`** — Trigger a sweep or burst (only when sweep/burst is enabled and its trigger source
  is set to manual — `[:SOURce[<n>]]:SWEep:TRIGger:SOURce` / `...:BURSt:TRIGger:SOURce`).
  Equivalent to `[:SOURce[<n>]]:SWEep:TRIGger[:IMMediate]` /
  `[:SOURce[<n>]]:BURSt:TRIGger[:IMMediate]`.
- **`*WAI`** — Hold off executing any further commands over the interface until all pending
  operations complete. Only meaningful in triggered-sweep/triggered-burst mode; used for
  synchronization.

### `:COUNter` Commands

Frequency counter (measures an external signal fed to the counter input, not the generator's own
output).

- **`:COUNter:AUTO`** — No-op-style command (no parameter): instrument auto-selects a gate time
  based on the signal under test. See `:COUNter:GATEtime`.
- **`:COUNter:COUPling {AC|DC}` / `?`** — Input coupling. Default `AC`.
- **`:COUNter:GATEtime {USER1..USER6}` / `?`** — Selects a preset gate time: USER1=1.310 ms,
  USER2=10.48 ms, USER3=166.7 ms, USER4=1.342 s, USER5=10.73 s, USER6=>10 s. Use USER6 for
  low-frequency (<5 Hz) signals. Query returns `AUTO` transiently while `:COUNter:AUTO` is
  resolving, then the selected `USERn`. Default `USER1`.
- **`:COUNter:HF {ON|1|OFF|0}` / `?`** — High-frequency rejection filter. `ON`: use for signals
  <250 kHz to filter HF noise. `OFF` (default): required for signals >250 kHz (up to 200 MHz max
  input).
- **`:COUNter:LEVEl {<value>|MIN|MAX}` / `?`** — Trigger level, -2.5 V to 2.5 V, 6 mV resolution,
  default 0 V.
- **`:COUNter:MEASure?`** — Query the last/current measurement as 5 comma-separated scientific-
  notation values: frequency, period, duty cycle, positive pulse width, negative pulse width, e.g.
  `2.000000000E+03,5.000000000E-04,4.760800000E+01,2.380415000E-04,2.619585000E-04`. Returns all
  zeros if the counter is disabled.
- **`:COUNter:SENSitive {<value>|MIN|MAX}` / `?`** — Trigger sensitivity, 0%–100%, default 25%.
  Higher for small-amplitude signals, lower for slow-edge/low-frequency signals.
- **`:COUNter[:STATe] {ON|1|OFF|0|RUN|STOP|SINGLE}` / `?`** — Enable/disable and control run state.
  `RUN`/`STOP`/`SINGLE` are only valid once the counter is already enabled. Enabling the counter
  disables CH2's sync output. Default `OFF`.
- **`:COUNter:STATIstics:CLEAr`** — Clear accumulated statistics (only valid while statistics are
  enabled; auto-clears when disabled).
- **`:COUNter:STATIstics:DISPlay {DIGITAL|CURVE}` / `?`** — Statistics display format. Default
  `DIGITAL`.
- **`:COUNter:STATIstics[:STATe] {ON|1|OFF|0}` / `?`** — Enable/disable measurement statistics.
  Default `OFF`.

### `:COUPling` Commands

Ties CH1/CH2 frequency, amplitude and/or phase together so changing one channel adjusts the other
by a fixed deviation or ratio. (Equivalent per-channel forms also exist under `:SOURce`, cross-
referenced below — both paths control the same underlying state.)

- **`:COUPling:AMPL:DEViation <deviation>` / `?`** — Amplitude deviation, -19.998 Vpp to 19.998
  Vpp, default 0 Vpp. `ACH2 = ACH1 + ADev` (CH1 as reference) or `ACH1 = ACH2 - ADev` (CH2 as
  reference).
- **`:COUPling:AMPL:MODE {OFFSet|RATio}` / `?`** — Amplitude coupling mode. Default `RATio`.
- **`:COUPling:AMPL:RATio {<value>|MIN|MAX}` / `?`** — Amplitude ratio, 0.001 to 1000, default 1.
  `ACH2 = ACH1 * ARatio`.
- **`:COUPling:AMPL[:STATe] {ON|1|OFF|0}` / `?`** — Enable/disable amplitude coupling. Default
  `OFF`. **Set mode + deviation/ratio before enabling** — you cannot change mode or
  deviation/ratio once coupling is on.
- **`:COUPling:FREQuency:DEViation <deviation>` / `?`** — Frequency deviation,
  -59.999999999999 MHz to 0–59.999999999999 MHz, default 0 Hz.
- **`:COUPling:FREQuency:MODE {OFFSet|RATio}` / `?`** — Default `RATio`.
- **`:COUPling:FREQuency:RATio {<value>|MIN|MAX}` / `?`** — 0.000001 to 1000000, default 1.
- **`:COUPling:FREQuency[:STATe] {ON|1|OFF|0}` / `?`** — Default `OFF`. Same set-before-enable
  rule as amplitude coupling.
- **`:COUPling:PHASe:DEViation <deviation>` / `?`** — -360° to 360°, default 0°.
- **`:COUPling:PHASe:MODE {OFFSet|RATio}` / `?`** — Default `RATio`.
- **`:COUPling:PHASe:RATio {<value>|MIN|MAX}` / `?`** — 0.01 to 100, default 1.
- **`:COUPling:PHASe[:STATe] {ON|1|OFF|0}` / `?`** — Default `OFF`. Same set-before-enable rule.
- **`:COUPling[:STATe] {ON|1|OFF|0}` / `?`** — Enable/disable all three coupling types at once.
  Query returns a 3-part string, e.g. `FREQ:ON,PHASE:OFF,AMPL:OFF`.

**Gotcha (all coupling commands):** if either channel's coupled value would exceed that channel's
upper/lower limit, the instrument silently clamps the *other* channel's limit to avoid overrange —
watch for this in large-deviation/ratio setups.

### `:DISPlay` Commands

- **`:DISPlay:BRIGhtness {<brightness>|MIN|MAX}` / `?`** — 1%–100%, default 50%.
- **`:DISPlay:CONTrast {<contrast>|MIN|MAX}` / `?`** — 1%–100%, default 25%.
- **`:DISPlay:DATA?`** — Screenshot of the front-panel screen. Returns a `#`-prefixed definite-
  length binary block, e.g. `#9000230456BM6\x84\x03\x00...` (`9` = digit count of the following
  length field `000230456`). Equivalent to `:HCOPy:SDUMp:DATA?`.
- **`:DISPlay:MODE {DPV|DGV|SV}` / `?`** — `DPV` dual-channel parameters (digital+graph, default),
  `DGV` dual-channel graph only, `SV` single-channel (current channel) digital+graph.
- **`:DISPlay:SAVer:IMMediate`** — Trigger the screen saver immediately.
- **`:DISPlay:SAVer[:STATe] {ON|1|OFF|0}` / `?`** — Default `ON`. Screen saver kicks in after 15
  min idle, blanks fully after another 30 min.
- **`:DISPlay[:STATe] {ON|1|OFF|0}` / `?`** — Turn the screen off (only effective in remote mode;
  reverts to on automatically when the instrument returns to local — press `Help` on the front
  panel to force local). Default `ON`.
- **`:DISPlay:TEXT? `** — Query the string currently shown on-screen (via
  `:DISPlay:TEXT[:SET]`), returned quoted, e.g. `"RIGOL"`.
- **`:DISPlay:TEXT:CLEar`** — Clear the on-screen text.
- **`:DISPlay:TEXT[:SET] <quoted string>[,x[,y]]`** — Draw a string at pixel coordinate `(x,y)`
  (upper-left of the text), `x` 2–319 default 2, `y` 2–239 default 2. Max 45 characters (truncated
  if it can't fit a row); omitted coordinates reuse the last-set position (or the default at
  power-on).

### `:HCOPy` Commands

- **`:HCOPy:SDUMp:DATA?`** — Screenshot, same binary-block format as `:DISPlay:DATA?`.
- **`:HCOPy:SDUMp:DATA:FORMat BMP` / `?`** — Only `BMP` is supported; query always returns `BMP`.

### `:LICense` Command

- **`:LICense:INSTall <sn>`** — Installs the "16M internal memory" (Arb 16M) option using a
  28-byte alphanumeric serial number obtained from Rigol per-instrument. Query installation status
  with `*OPT?`. **This option gates some arbitrary-waveform memory-depth behavior** — see the
  closing section; the base instrument's documented arbitrary-waveform commands below work
  regardless, but deeper memory depth beyond the base configuration requires this license.
  Already installed on the DG1062Z this manual was written for — no need to re-run this command
  on that unit.

### `:LXI` Commands

Part of the LXI (LAN eXtensions for Instrumentation) standard this instrument complies with.

- **`:LXI:IDENtify[:STATE] {ON|1|OFF|0}` / `?`** — Toggle the on-screen "LXI Identify" indicator,
  used to visually confirm which physical unit a given LAN address maps to. Default `OFF`.
  Cleared by `*RST`.
- **`:LXI:MDNS:ENABle {ON|1|OFF|0}` / `?`** — Enable/disable mDNS. Default `ON`.
- **`:LXI:MDNS:HNAMe[:RESolved]?`** — Query the resolved mDNS host name.
- **`:LXI:MDNS:SNAMe:DESired <name>` / `?`** — Set/query the desired mDNS service name (letters +
  numbers only), default `rigollan`. Persists across power cycles and `*RST`; reset only by
  `:SYSTem:SECurity:IMMediate`.
- **`:LXI:MDNS:SNAMe[:RESolved]?`** — Query the resolved (actually-in-use) mDNS service name,
  which may differ from the desired one if there was a naming conflict on the network.
- **`:LXI:RESet`** — Reset LAN configuration to a known state, starting from DHCP (falls back to
  AutoIP if DHCP fails). Takes several seconds to complete.
- **`:LXI:RESTart`** — Restart the LAN interface using the *current* configuration (does not reset
  values). Takes several seconds.

### `:MEMory` Commands

Internal non-volatile state-file storage (10 slots).

- **`:MEMory:NSTates?`** — Query the number of state-file storage slots. Always returns `10`.
- **`:MEMory:STATe:CATalog?`** — Query filenames in all 10 slots as a comma-separated,
  quote-wrapped list (empty-quoted for an unused slot), e.g.
  `"Scpi1.RSF","Scpi2.RSF","0.RSF","1.RSF","012.RSF","","33.RSF","","",""`.
- **`:MEMory:STATe:DELete {USER1..USER10}`** — Delete a stored state file. Fails silently if the
  slot is empty or the file is locked (unlock first via `:MEMory:STATe:LOCK`).
- **`:MEMory:STATe:LOCK {USER1..USER10},{ON|1|OFF|0}` / `:MEMory:STATe:LOCK? {USER1..USER10}`** —
  Lock/unlock a slot. A locked file can be renamed but not deleted. Default `OFF`.
- **`:MEMory:STATe:NAME {0..9}[,<name>]` / `:MEMory:STATe:NAME? {0..9}`** — Rename the state file
  in slot `n+1` (parameter `0`–`9` maps to slots 1–10). Name ≤9 characters (Chinese chars count as
  2). Only valid on a slot that already holds a file (check with `:MEMory:STATe:VALid?` first).
  Query returns quoted, e.g. `"123.RSF"`.
- **`:MEMory:STATe:RECall:AUTO {ON|1|OFF|0}` / `?`** — `ON`/`1`: power on to the last-used
  configuration (all system parameters/state except channel output on/off). `OFF`/`0` (default):
  power on to factory defaults except parameters exempted by the factory-reset table.
- **`:MEMory:STATe:VALid? {USER1..USER10}`** — Query whether a slot holds a valid state file
  (`1`/`0`).

### `:MMEMory` Commands

External memory (USB flash drive) file operations — `D:\` and its subfolders.

- **`:MMEMory:CATalog[:ALL]? [<folder>]`** — List all files/folders in `<folder>` (default
  `"D:\"`). Returns `space_used,space_available,"size,property,name",...` — property empty for a
  file, `DIR` for a folder (folder's "size" = count of its contents + 1).
- **`:MMEMory:CATalog:DATA:ARBitrary? [<folder>]`** — Same format, filtered to `.RAF` (arbitrary
  waveform) files only.
- **`:MMEMory:CATalog:STATe? [<folder>]`** — Same format, filtered to `.RSF` (state) files only.
- **`:MMEMory:CDIRectory <directory_name>` / `?`** — Set/query the current working directory.
  Default `"D:\"`.
- **`:MMEMory:COPY <directory_name>,<file_name>`** — Copy a file from the current directory to
  `<directory_name>` (a *different* directory — this is not a rename-in-place).
- **`:MMEMory:DELete <file_name>`** — Delete a file or empty folder in the current directory.
- **`:MMEMory:LOAD[:ALL] <file_name>`** — Load a state or arbitrary waveform file from the
  external drive; an arbitrary waveform loads into the current channel.
- **`:MMEMory:LOAD:DATA[1|2] <file_name>`** — Load an arbitrary waveform file into a specific
  channel (default CH1 if `[1|2]` omitted).
- **`:MMEMory:LOAD:STATe <file_name>`** — Load a state file specifically.
- **`:MMEMory:MDIRectory <dir_name>`** — Create a folder (≤9 chars) in the current directory.
  Errors if a folder of that name already exists.
- **`:MMEMory:RDIRectory?`** — Query available disk drives, e.g. `1,"D:"`, or `0,"NULL"` if none.
- **`:MMEMory:RDIRectory <folder>`** — Delete an *empty* folder.
- **`:MMEMory:STORe[:ALL] <file_name>`** — Save current instrument state or the current channel's
  arbitrary waveform data (filename extension `.RSF`/`.RAF` chosen by you) to the current
  directory. Filename ≤9 characters excluding suffix.
- **`:MMEMory:STORe:DATA[1|2] <file_name>`** — Save a specific channel's arbitrary waveform data
  as `.RAF`.
- **`:MMEMory:STORe:STATe <file_name>`** — Save current instrument state as `.RSF`.

### `:OUTPut` Commands

`[<n>]` = channel 1 or 2 (default 1) throughout.

- **`:OUTPut[<n>]:GATe:POLarity {POSitive|NEGative}` / `?`** — Gate polarity for gated output mode
  (see `:OUTPut[<n>]:MODE`). `POSitive` (default): output enabled while the gate input on the rear
  panel's `[Mod/Trig/FSK/Sync]` connector is high.
- **`:OUTPut[<n>]:IMPedance {<ohms>|INFinity|MIN|MAX}` / `?`** and the equivalent
  **`:OUTPut[<n>]:LOAD`** — Set expected load impedance, 1 Ω–10 kΩ or `INFinity` (HighZ), default
  50 Ω. Query returns `9.900000E+37` for HighZ. **This does not change actual output impedance
  (always 50 Ω) — it only affects how the instrument *computes* displayed/programmed
  amplitude/offset to match your load.** Mismatched setting vs. actual load = wrong voltage at the
  DUT.
- **`:OUTPut[<n>]:MODE {NORMal|GATed}` / `?`** — Default `NORMal`. `GATed`: output state follows an
  external gate signal, see `:OUTPut[<n>]:GATe:POLarity`.
- **`:OUTPut[<n>]:POLarity {NORMal|INVerted}` / `?`** — Invert the output waveform about its
  offset voltage (offset itself unchanged; associated sync signal is *not* inverted). Default
  `NORMal`.
- **`:OUTPut[<n>][:STATe] {ON|1|OFF|0}` / `?`** — Channel output on/off. Default `OFF`.
- **`:OUTPut[<n>]:SYNC:DELay {<delay>|MIN|MAX}` / `?`** — Delay of the sync signal (rear-panel
  `[Mod/Trig/FSK/Sync]`) relative to the front-panel output, 0 s to one carrier period, default
  0 s. Ignored while modulation/sweep/burst is active.
- **`:OUTPut[<n>]:SYNC:POLarity {POSitive|NEGative}` / `?`** — Default `POSitive`.
- **`:OUTPut[<n>]:SYNC[:STATe] {ON|1|OFF|0}` / `?`** — Enable/disable the sync signal on
  `[Mod/Trig/FSK/Sync]`. Default `ON`. Disabling it also disables the sweep mark signal.
  Above 30 MHz carrier, sync is frequency-divided.

### `:ROSCillator` Commands

Internal/external 10 MHz reference clock, via the `[10MHz In/Out]` rear-panel connector — used to
synchronize two or more generators.

- **`:ROSCillator:SOURce {INTernal|EXTernal}` / `?`** — Default `INTernal`. If `EXTernal` is
  selected and no valid signal is detected on `[10MHz In/Out]`, the instrument displays a warning
  and reverts to internal automatically.
- **`:ROSCillator:SOURce:CURRent?`** — Query which clock source is *actually* in effect right now
  (distinct from the configured setting, since `EXTernal` can silently fall back).

### `:SOURce:APPLy` Commands

The fastest way to configure and (implicitly) select a waveform in one call — sets waveform type,
frequency, amplitude, offset (and phase, where applicable) together. All take the form
`[:SOURce[<n>]]:APPLy:<TYPE> [<freq>|DEF|MIN|MAX[,<amp>|DEF|MIN|MAX[,<offset>|DEF|MIN|MAX[,<phase>|DEF|MIN|MAX]]]]`
unless noted. Defaults for all: `<amp>` 5 Vpp, `<offset>` 0 VDC, `<phase>` 0°, and amplitude/offset
ranges are limited by the channel's impedance and frequency/period settings
(`:OUTPut[<n>]:IMPedance`/`:LOAD`).

- **`[:SOURce[<n>]]:APPLy?`** — Query CH*n*'s current waveform config as a quoted 5-part string:
  waveform name, frequency, amplitude, offset, phase, e.g.
  `"SQU,1.000000E+03,2.000000E+00,3.000000E+00,4.000000E+00"`. Waveform names: `SIN`, `SQU`,
  `RAMP`, `PULSE`, `NOISE`, `DC`, `USER` (arbitrary/harmonic show as `USER`/other tokens per
  the table in `[:SOURce[<n>]]:FUNCtion?`).
- **`:APPLy:SINusoid [<freq>...]`** — `<freq>` 1 µHz–60 MHz, default 1 kHz.
- **`:APPLy:SQUare [<freq>...]`** — `<freq>` 1 µHz–25 MHz, default 1 kHz. Overrides current duty
  cycle to 50% automatically.
- **`:APPLy:RAMP [<freq>...]`** — `<freq>` 1 µHz–1 MHz, default 1 kHz. Overrides symmetry to 50%.
- **`:APPLy:TRIangle [<freq>...]`** — `<freq>` 1 µHz–1 MHz, default 1 kHz. A ramp with fixed 100%
  symmetry.
- **`:APPLy:PULSe [<freq>...]`** — `<freq>` 1 µHz–25 MHz, default 1 kHz.
- **`:APPLy:NOISe [<amp>[,<offset>]]`** — No frequency parameter (noise has no periodicity); 5 MHz
  bandwidth fixed.
- **`:APPLy:DC [<freq>|DEF[,<amp>|DEF[,<offset>]]]`** — `<freq>`/`<amp>` are ignored placeholders
  (must still be present or `DEFault`); only `<offset>` matters.
- **`:APPLy:HARMonic [<freq>[,<amp>[,<offset>[,<phase>]]]]`** — `<freq>` 1 µHz–20 MHz, default
  1 kHz. Enables harmonic mode and sets the *fundamental* (sine) parameters; use the
  `[:SOURce[<n>]]:HARMonic:*` commands to configure the harmonics themselves. On execution, the
  instrument reuses previously-set (or default) harmonic order/type/amplitude/phase.
- **`:APPLy:USER [<freq>[,<amp>[,<offset>[,<phase>]]]]`** — `<freq>` 1 µHz–20 MHz, default 1 kHz.
  Sets the *currently selected* arbitrary waveform to output in **frequency mode** (as opposed to
  sample-rate mode) — does not itself choose *which* arbitrary waveform; use
  `[:SOURce[<n>]]:FUNCtion[:SHAPe]` for that (default arbitrary waveform is `Sinc`).
- **`:APPLy:ARBitrary [<sample_rate>|DEF|MIN|MAX[,<amp>[,<offset>]]]`** — Like `:APPLy:USER` but
  in **sample-rate mode**: `<sample_rate>` 1 µSa/s–60 MSa/s, default 20 MSa/s, instead of a
  frequency. Also does not select which arbitrary waveform — same caveat as `:APPLy:USER`.

All of the above have a `:CH2` sibling for the second channel (e.g. `[:SOURce[<n>]]:APPLy:SINusoid:CH2 [...]`,
`[:SOURce[<n>]]:APPLy:CH2?`) in the *older* DG1000 command style; on the DG1000Z the preferred and
fully-equivalent form is the `[<n>]` channel selector shown above (e.g.
`:SOURce2:APPLy:SINusoid ...` / `:SOUR2:APPL:SIN ...`) — use that form for new code.

### `:SOURce:BURSt` Commands

Output a defined number of cycles (or a gated block) instead of continuous waveform.

- **`[:SOURce[<n>]]:BURSt:GATE:POLarity {NORMal|INVerted}` / `?`** — Gate polarity for gated burst
  mode. Default `NORMal` (gate true when the external signal on `[Mod/Trig/FSK/Sync]` is high).
  Instrument finishes the current cycle then stops when gate goes false (stops immediately for
  noise).
- **`[:SOURce[<n>]]:BURSt:INTernal:PERiod {<period>|MIN|MAX}` / `?`** — N-cycle burst period in
  internal-trigger mode, 2.0166 µs–500 s, default 10 ms. Relation:
  `Pburst ≥ Pwaveform × Ncycle + 2µs` — the instrument silently increases too-small periods to fit.
- **`[:SOURce[<n>]]:BURSt:MODE {TRIGgered|INFinity|GATed}` / `?`** — Default `TRIGgered` (N-cycle).
  `TRIGgered`: outputs a fixed number of cycles per trigger (sine/square/ramp/pulse/arbitrary
  except DC; internal/external/manual trigger). `INFinity`: continuous output per trigger, stops
  on the next trigger (external/manual only). `GATed`: output follows an external gate level
  (sine/square/ramp/pulse/noise/arbitrary except DC; external trigger only).
- **`[:SOURce[<n>]]:BURSt:NCYCles {<cycles>|MIN|MAX}` / `?`** — Cycle count for N-cycle burst: 1 to
  1,000,000 (external/manual trigger) or 1 to 500,000 (internal trigger), default 1.
- **`[:SOURce[<n>]]:BURSt:PHASe {<phase>|MIN|MAX}` / `?`** — Burst start phase, 0°–360°, default
  0°.
- **`[:SOURce[<n>]]:BURSt[:STATe] {ON|1|OFF|0}` / `?`** — Default `OFF`. Enabling this
  auto-disables modulation and sweep. **Configure all other burst parameters before enabling** to
  avoid a flurry of intermediate waveform changes.
- **`[:SOURce[<n>]]:BURSt:TDELay {<delay>|MIN|MAX}` / `?`** — Delay from trigger receipt to burst
  start, N-cycle/infinite modes only. External/manual trigger: 0–100 s. Internal trigger:
  0 to `(Pburst - Pwaveform×Ncycle - 2µs)`, capped at 100 s.
- **`[:SOURce[<n>]]:BURSt:TRIGger[:IMMediate]`** — Fire a burst immediately (manual-trigger mode
  only; ignored if the channel output is off).
- **`[:SOURce[<n>]]:BURSt:TRIGger:SLOPe {POSitive|NEGative}` / `?`** — External-trigger edge.
  Default `POSitive`.
- **`[:SOURce[<n>]]:BURSt:TRIGger:SOURce {INTernal|EXTernal|MANual}` / `?`** — Default `INTernal`.
  `INTernal` valid for N-cycle only. `EXTernal` valid for all three burst modes. `MANual` valid for
  N-cycle/infinite (fire via `*TRG`, `:TRIGger[<n>][:IMMediate]`, or
  `[:SOURce[<n>]]:BURSt:TRIGger[:IMMediate]`).
- **`[:SOURce[<n>]]:BURSt:TRIGger:TRIGOut {POSitive|NEGative|OFF}` / `?`** — Trigger-*output* edge
  on `[Mod/Trig/FSK/Sync]`, for internal/manual trigger mode only. Default `OFF`.

### `:SOURce:FREQuency` Commands

- **`[:SOURce[<n>]]:FREQuency:CENTer {<frequency>|MIN|MAX}` / `?`** — Sweep center frequency,
  default 550 Hz. Interacts with `:FREQuency:SPAN` — see gotcha below. Modifying this resets sweep
  output to start from `:FREQuency:STARt` again.
- **`[:SOURce[<n>]]:FREQuency:COUPle:MODE {OFFSet|RATio}` / `?`**,
  **`:COUPle:OFFSet <frequency>` / `?`** (range as `:COUPling:FREQuency:DEViation`),
  **`:COUPle:RATio {<value>|MIN|MAX}` / `?`** (range as `:COUPling:FREQuency:RATio`),
  **`:COUPle[:STATe] {ON|1|OFF|0}` / `?`** — Per-channel-path aliases of the corresponding
  `:COUPling:FREQuency:*` commands documented above; same underlying state, same set-before-enable
  rule.
- **`[:SOURce[<n>]]:FREQuency[:FIXed] {<frequency>|MIN|MAX}` / `?`** — The waveform frequency for
  basic/arbitrary waveforms, default 1 kHz; range per Table 2-1 below (waveform-type dependent).
  Out-of-range values clamp to the limit. If the waveform type changes and the current frequency
  is invalid for the new type, the instrument clamps to that type's upper limit and warns.
- **`[:SOURce[<n>]]:FREQuency:SPAN {<frequency>|MIN|MAX}` / `?`** — Sweep frequency span, default
  900 Hz.
- **`[:SOURce[<n>]]:FREQuency:STARt {<frequency>|MIN|MAX}` / `?`** — Sweep start frequency,
  default 100 Hz.
- **`[:SOURce[<n>]]:FREQuency:STOP {<frequency>|MIN|MAX}` / `?`** — Sweep stop frequency, default
  1 kHz.

**Gotcha:** start/stop/center/span are interlinked: `Fcenter = (Fstart+Fstop)/2`,
`Fspan = |Fstop-Fstart|`. Setting one recomputes the others; the sweep always restarts from
`:FREQuency:STARt` after any of the four is touched. If start > stop, the sweep runs high→low; if
equal, it's a fixed-frequency output.

**Table 2-1 — waveform frequency ranges (DG1062Z / DG1032Z):**

| Waveform | DG1032Z | DG1062Z |
|---|---|---|
| Sine | 1 Hz–30 MHz | 1 Hz–60 MHz |
| Square | 1 Hz–15 MHz | 1 Hz–25 MHz |
| Ramp | 1 Hz–500 kHz | 1 Hz–1 MHz |
| Pulse | 1 Hz–15 MHz | 1 Hz–25 MHz |
| Harmonic | 1 Hz–10 MHz | 1 Hz–20 MHz |
| Noise (-3dB bandwidth) | 30 MHz | 60 MHz |
| Arbitrary waveform | 1 Hz–10 MHz | 1 Hz–20 MHz |

### `:SOURce:FUNCtion` Commands

- **`[:SOURce[<n>]]:FUNCtion:ARBitrary:MODE {FREQ|SRATe}` / `?`** — Arbitrary waveform output
  mode: `FREQ` (set frequency/period, point count auto-selected) or `SRATe` (set sample rate,
  points output one-by-one at that rate). Default `FREQ`.
- **`[:SOURce[<n>]]:FUNCtion:ARBitrary:SRATe {<srate>|MIN|MAX}` / `?`** — 1 µSa/s–60 MSa/s,
  default 20 MSa/s.
- **`[:SOURce[<n>]]:FUNCtion:PULSe:DCYCle {<percent>|MIN|MAX}` / `?`** — Pulse duty cycle,
  0.001%–99.999% (further bounded by minimum pulse width vs. pulse period — see formula below),
  default 50%.
- **`[:SOURce[<n>]]:FUNCtion:PULSe:HOLD {WIDTh|DCYCle}` / `?`** — Which of pulse-width/duty-cycle
  is the "held" (front-panel highlighted / interactively primary) parameter. Default `DCYCle`.
- **`[:SOURce[<n>]]:FUNCtion:PULSe:PERiod {<seconds>|MIN|MAX}` / `?`** — 40 ns–1 Ms (DG1062Z) /
  66.6 ns–1 Ms (DG1032Z), default 1 ms.
- **`[:SOURce[<n>]]:FUNCtion:PULSe:TRANsition[:BOTH] {<seconds>|MIN|MAX}`** — Set both rise and
  fall time to the same value in one call (set-only, no query — query the two edges individually).
  10 ns to 0.625×pulse-width, default 20 ns.
- **`[:SOURce[<n>]]:FUNCtion:PULSe:TRANsition:LEADing {<seconds>|MIN|MAX}` / `?`** — Rise time
  (10%→90%), same range/default as above.
- **`[:SOURce[<n>]]:FUNCtion:PULSe:TRANsition:TRAiling {<seconds>|MIN|MAX}` / `?`** — Fall time
  (90%→10%), same range/default.
- **`[:SOURce[<n>]]:FUNCtion:PULSe:WIDTh {<seconds>|MIN|MAX}` / `?`** — 16 ns to
  ~999,999,982,118,590.6 ns, default 500 µs; actual bound is
  `Pwmin ≤ Pwidth < Ppulse - 2×Pwmin`.
- **`[:SOURce[<n>]]:FUNCtion:RAMP:SYMMetry {<symmetry>|MIN|MAX}` / `?`** — % of period spent
  rising, 0%–100%, default 50%.
- **`[:SOURce[<n>]]:FUNCtion[:SHAPe] <name>` / `?`** — Select the waveform type/shape. `<name>` is
  one of the basic types (`SINusoid|SQUare|RAMP|PULSe|NOISe|USER|HARMonic|DC`) or one of ~150
  named built-in arbitrary waveforms (`SINC`, `EXP_RISE`, `GAUSS`, `CARDIAC`, `EOG`/`EEG`/`EMG`,
  window functions, IEC/ISO pulse standards, etc. — see the vendor guide's Appendix A for the full
  enumerated list). Query returns a short token, e.g. `SQU`.
- **`[:SOURce[<n>]]:FUNCtion:SQUare:DCYCle {<percent>|MIN|MAX}` / `?`** — Square-wave duty cycle,
  range frequency-dependent, default 50%.
- **`[:SOURce[<n>]]:FUNCtion:SQUare:PERiod {<seconds>|MIN|MAX}` / `?`** — 40 ns–1 Ms (DG1062Z) /
  66.6 ns–1 Ms (DG1032Z), default 1 ms.

**Duty-cycle bound formula (applies to both `:PULSe:DCYCle` and, analogously, timing/width
relationships):** `100×Pwmin/Ppulse ≤ Pdcycle < 100×(1 - 2×Pwmin/Ppulse)`, where `Pwmin` is the
minimum pulse width and `Ppulse` the pulse period (see the vendor "Specifications" chapter for
`Pwmin`'s numeric value).

### `:SOURce:HARMonic` Commands

Output the fundamental (sine) plus selected harmonics.

- **`[:SOURce[<n>]]:HARMonic:AMPL <sn>,{<value>|MIN|MAX}` / `? <sn>`** — Amplitude of harmonic
  order `<sn>` (2–8), 0 Vpp to the channel's amplitude upper limit, default 1.2647 Vpp.
- **`[:SOURce[<n>]]:HARMonic:ORDEr {<value>|MIN|MAX}` / `?`** — Highest harmonic order to output,
  integer 2–8 (further capped by `Fout_max / Ffundamental`), default 2.
- **`[:SOURce[<n>]]:HARMonic:PHASe <sn>,{<value>|MIN|MAX}` / `? <sn>`** — Phase of harmonic order
  `<sn>` (2–8), 0°–360°, default 0°.
- **`[:SOURce[<n>]]:HARMonic[:STATe] {ON|1|OFF|0}` / `?`** — Default `OFF`.
- **`[:SOURce[<n>]]:HARMonic:TYPe {EVEN|ODD|ALL|USER}` / `?`** — Default `EVEN`. `USER`: pick
  arbitrary orders via `:HARMonic:USER`.
- **`[:SOURce[<n>]]:HARMonic:USER <user>` / `?`** — 8-character string `X0000000`–`X11111111`; `X`
  (fixed) = fundamental, remaining 7 bits = harmonic orders 2–8, `1`=on/`0`=off. E.g. `X0010001` =
  fundamental + 4th + 8th harmonic.

### `:SOURce:MARKer` Commands

Sweep frequency-mark — an extra sync-signal transition at a specified frequency point during a
sweep (independent of the center/mark point used when marking is off).

- **`[:SOURce[<n>]]:MARKer:FREQuency {<frequency>|MIN|MAX}` / `?`** — Must lie between the sweep's
  start and stop frequency; default 550 Hz. If it doesn't exactly match a step-sweep point, the
  nearest point is used instead.
- **`[:SOURce[<n>]]:MARKer[:STATe] {ON|1|OFF|0}` / `?`** — Default `OFF`.

### `:SOURce[:MOD]:AM`, `:ASKey`, `:FM`, `:FSKey`, `:PM`, `:PSKey`, `:PWM` Commands

All six modulation types share a consistent shape: a depth/deviation/rate parameter, an internal
modulating-waveform choice, an internal-vs-external source switch, and an enable state. Carrier
waveform can be Sine/Square/Ramp/Arbitrary (except DC) for AM/FM/PM/ASK/FSK/PSK; PWM's carrier can
*only* be Pulse. Enabling any modulation auto-disables sweep/burst; harmonic mode must be off
first (can't modulate a harmonic output).

**AM (amplitude modulation):**
- **`[:SOURce[<n>]][:MOD]:AM[:DEPTh] {<depth>|MIN|MAX}` / `?`** — 0%–120%, default 100%. At 0%,
  output = half carrier amplitude; at 100%, output = full carrier amplitude; >100% is clamped so
  output never exceeds 10 Vpp into 50 Ω.
- **`:AM:DSSC {ON|1|OFF|0}` / `?`** — Double-sideband suppressed-carrier mode. Default `OFF`.
- **`:AM:INTernal:FREQuency {<frequency>|MIN|MAX}` / `?`** — 2 mHz–1 MHz, default 100 Hz (internal
  source only).
- **`:AM:INTernal:FUNCtion <name>` / `?`** — `SINusoid|SQUare|TRIangle|RAMP|NRAMp|NOISe|USER`,
  default `SINusoid` (internal source only). `NRAMp` = 0% symmetry ramp, `USER` = the arbitrary
  waveform currently selected on this channel.
- **`:AM:SOURce {INTernal|EXTernal}` / `?`** — Default `INTernal`. External: modulating signal is
  the ±5 V level on the rear-panel `[Mod/Trig/FSK/Sync]` connector.
- **`:AM:STATe {ON|1|OFF|0}` / `?`** — Default `OFF`.

**ASK (amplitude shift keying) — shifts between two preset amplitudes:**
- **`[:SOURce[<n>]][:MOD]:ASKey:AMPLitude {<amplitude>|MIN|MAX}` / `?`** — 0 Vpp–10 Vpp (HighZ),
  default 2 Vpp.
- **`:ASKey:INTernal[:RATE] {<frequency>|MIN|MAX}` / `?`** — 2 mHz–1 MHz, default 100 Hz.
- **`:ASKey:POLarity {POSitive|NEGative}` / `?`** — Default `POSitive`: modulating-signal-high →
  greater of carrier/modulation amplitude.
- **`:ASKey:SOURce {INTernal|EXTernal}` / `?`** — Default `INTernal` (fixed 50% duty-cycle square
  internally).
- **`:ASKey:STATe {ON|1|OFF|0}` / `?`** — Default `OFF`.

**FM (frequency modulation):**
- **`[:SOURce[<n>]][:MOD]:FM[:DEViation] {<deviation>|MIN|MAX}` / `?`** — Default 1 kHz.
  `deviation ≤ carrier frequency`; `deviation + carrier ≤ carrier upper limit + 1 kHz`. If carrier
  is Sine and this sum exceeds the carrier upper limit, carrier amplitude is capped at 2 Vpp.
- **`:FM:INTernal:FREQuency {<frequency>|MIN|MAX}` / `?`** — 2 mHz–1 MHz, default 100 Hz.
- **`:FM:INTernal:FUNCtion <name>` / `?`** — Same enum as AM's, default `SINusoid`.
- **`:FM:SOURce {INTernal|EXTernal}` / `?`** — Default `INTernal`.
- **`:FM:STATe {ON|1|OFF|0}` / `?`** — Default `OFF`.

**FSK (frequency shift keying) — shifts between two preset frequencies:**
- **`[:SOURce[<n>]][:MOD]:FSKey[:FREQuency] {<frequency>|MIN|MAX}` / `?`** — The "hop" frequency,
  range = current waveform's frequency range, default 10 kHz.
- **`:FSKey:INTernal:RATE {<rate>|MIN|MAX}` / `?`** — 2 mHz–1 MHz, default 100 Hz.
- **`:FSKey:POLarity {POSitive|NEGative}` / `?`** — Default `POSitive`.
- **`:FSKey:SOURce {INTernal|EXTernal}` / `?`** — Default `INTernal`.
- **`:FSKey:STATe {ON|1|OFF|0}` / `?`** — Default `OFF`.

**PM (phase modulation):**
- **`[:SOURce[<n>]][:MOD]:PM[:DEViation] {<deviation>|MIN|MAX}` / `?`** — 0°–360°, default 90°.
- **`:PM:INTernal:FREQuency {<frequency>|MIN|MAX}` / `?`** — 2 mHz–1 MHz, default 100 Hz.
- **`:PM:INTernal:FUNCtion <name>` / `?`** — Same enum as AM's, default `SINusoid`.
- **`:PM:SOURce {INTernal|EXTernal}` / `?`** — Default `INTernal`.
- **`:PM:STATe {ON|1|OFF|0}` / `?`** — Default `OFF`.

**PSK (phase shift keying) — shifts between two preset phases:**
- **`[:SOURce[<n>]][:MOD]:PSKey:INTernal:RATE {<rate>|MIN|MAX}` / `?`** — 2 mHz–1 MHz, default
  100 Hz.
- **`:PSKey:PHASe {<phase>|MIN|MAX}` / `?`** — Modulation phase, 0°–360°, default 180°.
- **`:PSKey:POLarity {POSitive|NEGative}` / `?`** — Default `POSitive`.
- **`:PSKey:SOURce {INTernal|EXTernal}` / `?`** — Default `INTernal`.
- **`:PSKey:STATe {ON|1|OFF|0}` / `?`** — Default `OFF`.

**PWM (pulse width modulation) — carrier must be Pulse:**
- **`[:SOURce[<n>]][:MOD]:PWM[:DEViation]:DCYCle {<percent>|MIN|MAX}` / `?`** — Duty-cycle
  deviation, default 20%; capped by minimum duty cycle, current edge time, and the current pulse
  duty cycle. Front panel shows "Duty Dev" when `:PULSe:HOLD` is `DCYCle`, "Width Dev" when it's
  `WIDTh`.
- **`:PWM[:DEViation][:WIDTh] {<deviation>|MIN|MAX}` / `?`** — Width deviation, default 200 µs;
  same capping logic.
- **`:PWM:INTernal:FREQuency {<frequency>|MIN|MAX}` / `?`** — 2 mHz–1 MHz, default 100 Hz.
- **`:PWM:INTernal:FUNCtion <name>` / `?`** — Same enum as AM's, default `SINusoid`.
- **`:PWM:SOURce {INTernal|EXTernal}` / `?`** — Default `INTernal`.
- **`:PWM:STATe {ON|1|OFF|0}` / `?`** — Default `OFF`. Only enterable while the channel's current
  waveform is Pulse.

### `:SOURce:MOD` Commands

Top-level modulation enable/type selector (works alongside the per-type `:STATe`/`:SOURce`
commands above — this is the "which modulation, and is it on" umbrella).

- **`[:SOURce[<n>]]:MOD[:STATe] {ON|1|OFF|0}` / `?`** — Default `OFF`. Not available in sample-rate
  arbitrary-waveform mode. Auto-disables sweep/burst; blocked while harmonic mode is on.
- **`[:SOURce[<n>]]:MOD:TYPe {AM|FM|PM|ASK|FSK|PSK|PWM}` / `?`** — Default `AM`.

### `:SOURce:PERiod` Command

- **`[:SOURce[<n>]]:PERiod[:FIXed] {<period>|MIN|MAX}` / `?`** — Waveform period for basic and
  arbitrary waveforms (reciprocal of `:FREQuency[:FIXed]` — setting one updates the other),
  default 1 ms. Range per Table 2-1 above. Too-low values clamp to the period lower limit; a
  waveform-type change that invalidates the current period auto-clamps it (with a warning) to the
  new type's lower limit.

### `:SOURce:PHASe` Commands

- **`[:SOURce[<n>]]:PHASe[:ADJust] {<phase>|MIN|MAX}` / `?`** — Waveform start phase, 0°–360°,
  default 0°.
- **`[:SOURce[<n>]]:PHASe:INITiate`** and **`[:SOURce[<n>]]:PHASe:SYNChronize`** — Execute an
  "align phase" operation: re-synchronizes CH1/CH2 so both output at their configured frequency
  and phase relationship. Invalid while either channel is in modulation mode. (Both command
  spellings do the same thing — pick either.)

### `:SOURce:PULSe` Commands (top-level alias)

Identical in every respect to the `[:SOURce[<n>]]:FUNCtion:PULSe:*` commands above — same syntax,
ranges, defaults, gotchas — just without the `FUNCtion` keyword segment:
`[:SOURce[<n>]]:PULSe:DCYCle`, `:PULSe:HOLD {WIDTh|DUTY}` (note: `DUTY` here, not `DCYCle`, but
same semantics), `:PULSe:TRANsition[:LEADing]`, `:PULSe:TRANsition:TRAiling`, `:PULSe:WIDTh`. Use
whichever form your existing code favors; they read/write the same underlying state.

### `:SOURce:SUM` Commands

Sums a second waveform onto the channel's basic waveform.

- **`[:SOURce[<n>]]:SUM:AMPLitude {<amplitude>|MIN|MAX}` / `?`** — Sum ratio (summed waveform's
  amplitude as % of the basic waveform's), 0%–100%, default 10%.
- **`[:SOURce[<n>]]:SUM:INTernal:FREQuency {<frequency>|MIN|MAX}` / `?`** — 1 µHz–60 MHz, default
  1 kHz.
- **`[:SOURce[<n>]]:SUM:INTernal:FUNCtion <name>` / `?`** — `SIN|SQU|RAMP|NOISe|ARB`, default
  `SIN`.
- **`[:SOURce[<n>]]:SUM[:STATe] {ON|1|OFF|0}` / `?`** — Default `OFF`. Only valid on basic
  waveforms (except noise), arbitrary (except DC), and harmonic. Cannot enable while
  modulation/sweep/burst is active.

### `:SOURce:SWEep` Commands

Continuously vary output frequency between start and stop over a defined time. Only sine, square,
ramp, and arbitrary (except DC) support sweep — pulse and noise do not.

- **`[:SOURce[<n>]]:SWEep:HTIMe:STARt {<seconds>|MIN|MAX}` / `?`** — Time held at start frequency
  before sweeping, 0 s–500 s, default 0 s.
- **`[:SOURce[<n>]]:SWEep:HTIMe[:STOP] {<seconds>|MIN|MAX}` / `?`** — Time held at stop frequency
  after reaching it, 0 s–500 s, default 0 s.
- **`[:SOURce[<n>]]:SWEep:RTIMe {<seconds>|MIN|MAX}` / `?`** — Return-to-start time after the stop
  hold, 0 s–500 s, default 0 s.
- **`[:SOURce[<n>]]:SWEep:SPACing {LINear|LOGarithmic|STEp}` / `?`** — Default `LINear`. `LINear`:
  Hz/s constant rate. `LOGarithmic`: octave/decade-per-second rate. `STEp`: discrete frequency
  steps, count set by `:SWEep:STEP`, dwell per step from `:SWEep:TIME`.
- **`[:SOURce[<n>]]:SWEep:STATe {ON|1|OFF|0}` / `?`** — Default `OFF`. Auto-disables
  modulation/burst on enable; blocked while harmonic mode is on.
- **`[:SOURce[<n>]]:SWEep:STEP {<n>|MIN|MAX}` / `?`** — Number of steps (step-sweep only),
  integer 2–1024, default 2.
- **`[:SOURce[<n>]]:SWEep:TIME {<seconds>|MIN|MAX}` / `?`** — Time to sweep start→stop, 1 ms–500 s,
  default 1 s.
- **`[:SOURce[<n>]]:SWEep:TRIGger[:IMMediate]`** — Fire a sweep now (manual-trigger mode only,
  channel output must be on).
- **`[:SOURce[<n>]]:SWEep:TRIGger:SLOPe {POSitive|NEGative}` / `?`** — External-trigger edge.
  Default `POSitive`.
- **`[:SOURce[<n>]]:SWEep:TRIGger:SOURce {INTernal|EXTernal|MANual}` / `?`** — Default `INTernal`
  (continuous, period governed by sweep time + holds + return time).
- **`[:SOURce[<n>]]:SWEep:TRIGger:TRIGOut {POSitive|NEGative|OFF}` / `?`** — Trigger-output edge
  for internal/manual trigger mode. Default `POSitive`.

### `:SOURce:TRACe` Commands

Arbitrary waveform data editing — volatile (RAM, editable in real time) and non-volatile (stored,
named) waveform files.

- **`[:SOURce[<n>]][:TRACe]:DATA:CATalog?`** — Query stored `.RAF` filenames across all 10 slots,
  comma-separated quoted (empty quotes for unused slots), e.g.
  `"000.RAF","330.RAF","","","","","","","",""`.
- **`[:SOURce[<n>]][:TRACe]:DATA:COPY <trace_name>,VOLATILE`** — Copy a stored `.RAF` file into
  the channel's volatile (editable) memory.
- **`[:SOURce[<n>]][:TRACe]:DATA:DAC16 VOLATILE,<flag>,<data>`** — Download a waveform table
  directly to DDRII memory as raw binary. `<flag>` = `CON` (more packets coming) or `END` (last
  packet — switches the channel to arbitrary output automatically). `<data>` is a `#`-prefixed
  IEEE definite-length binary block (e.g. `#516384<16384 bytes>`), 14-bit values (`0x0000`–
  `0x3FFF`) per point, 2 bytes/point, 8 pts–16 kpts per transfer.
- **`[:SOURce[<n>]][:TRACe]:DATA:DAC VOLATILE,[<binary_block_data>|<value>,<value>,...]`** —
  Download either a binary block (same format as `DAC16`) or a comma-separated list of decimal DAC
  values (0–16383 each) to volatile memory. **Gotcha:** if the point count is 8–8192, the
  instrument auto-extends it to 8192 via average interpolation *only in frequency mode*; in
  sample-rate mode the count is left as-is. If the count is >8192 and ≤16384, the instrument
  auto-switches to sample-rate mode. After this command, the channel switches to volatile-waveform
  output automatically.
- **`[:SOURce[<n>]][:TRACe]:DATA[:DATA] VOLATILE,<value>{,<value>...}`** — Download normalized
  floating-point voltages (`-1` to `+1`, where `±1` = the waveform's configured max/min given its
  current amplitude/offset) to volatile memory, 8–16384 points per call. Overwrites the prior
  volatile waveform outright (no error raised). Auto-switches the channel to volatile output.
- **`[:SOURce[<n>]][:TRACe]:DATA:DELete[:NAME] <trace_name>`** — Delete a stored `.RAF` file
  (fails if it's locked — see `:DATA:LOCK[:STATe]`).
- **`[:SOURce[<n>]][:TRACe]:DATA:LOAD? VOLATILE`** — Query how many data packages the current
  volatile waveform is split into (for readback).
  **`[:SOURce[<n>]][:TRACe]:DATA:LOAD? <Num>`** — Read back package `<Num>` (1-based), returned as
  a `#`-prefixed binary block, e.g. `#9000016384`.
- **`[:SOURce[<n>]][:TRACe]:DATA:LOCK[:STATe] <trace_name>,{ON|OFF|1|0}` /
  `? <trace_name>`** — Lock/query-lock a stored `.RAF` file against deletion. Default `OFF`.
- **`[:SOURce[<n>]][:TRACe]:DATA:POINts VOLATILE[,<points>|MIN|MAX]` / `? VOLATILE[,MIN|MAX]`** —
  Set the initial point count for a new volatile-waveform edit session, 8–16384, default 8.
  Switches the channel to volatile arbitrary output and zero-fills the new points; follow with
  `:DATA:VALue` to populate them.
- **`[:SOURce[<n>]][:TRACe]:DATA:VALue VOLATILE,<point>,<data>` / `? VOLATILE,<point>`** — Set/read
  one point's raw decimal value (0–16383) by 1-based index. Only valid while the channel's output
  is the volatile arbitrary waveform.

### `:SOURce:TRACK` Command

- **`[:SOURce[<n>]]:TRACK {ON|OFF|INVerted}` / `?`** — Default `OFF`. `ON`: CH2 continuously
  mirrors CH1's parameters/state (not output on/off) — both channels output the same signal.
  `INVerted`: same mirroring, but CH2 outputs CH1's signal inverted. Enabling this disables
  coupling and channel-copy, and forces single-channel view on CH1.

### `:SOURce:VOLTage` Commands

- **`[:SOURce[<n>]]:VOLTage:COUPle[:STATe] {ON|1|OFF|0}` / `?`** — Alias of
  `:COUPling:AMPL[:STATe]` documented above (same underlying state, same set-before-enable rule).
- **`[:SOURce[<n>]]:VOLTage[:LEVel][:IMMediate][:AMPLitude] {<amplitude>|MIN|MAX}` / `?`** —
  Waveform amplitude, 2 mVpp minimum, upper bound governed by impedance + frequency/period,
  default 5 Vpp. If a config change (e.g. frequency) invalidates the current amplitude, it's
  auto-clamped (with a warning) to the new upper limit.
- **`:VOLTage[:LEVel][:IMMediate]:HIGH {<voltage>|MIN|MAX}` / `?`** — High level, default
  2.5 Vpp. `High = Offset + Amplitude/2`.
- **`:VOLTage[:LEVel][:IMMediate]:LOW {<voltage>|MIN|MAX}` / `?`** — Low level, default -2.5 Vpp.
  `Low = Offset - Amplitude/2`.
- **`:VOLTage[:LEVel][:IMMediate]:OFFSet {<voltage>|MIN|MAX}` / `?`** — DC offset, default 0 VDC;
  range governed by impedance, frequency, and amplitude settings.
- **`:VOLTage:RANGe:AUTO {OFF|ON|0|1}` / `?`** — Default `ON`. `ON`: instrument auto-selects the
  optimum amplifier/attenuator combination. `OFF` ("hold"): fixes the current range, avoiding
  transient amplitude glitches from range-switching at the cost of amplitude/offset accuracy,
  resolution, and waveform fidelity in that fixed range.
- **`:VOLTage:UNIT {VPP|VRMS|DBM}` / `?`** — Default `VPP`. `DBM` is invalid when impedance is
  HighZ (dBm requires a defined resistive load: `dBm = 10·log10(Vrms²/R / 0.001W)`).

### `:SYSTem` Commands

- **`:SYSTem:BEEPer[:IMMediate]`** — Beep once, immediately, regardless of the beeper's on/off
  state.
- **`:SYSTem:BEEPer:STATe {ON|1|OFF|0}` / `?`** — Default `ON`. When on, beeps on error (front
  panel or remote).
- **`:SYSTem:CHANnel:CURrent {CH1|CH2}` / `?`** — Which channel is "current" (affects front-panel
  display/interaction, not remote addressing — remote commands always explicitly target a channel
  via `[<n>]`). Default `CH1`.
- **`:SYSTem:CHANnel:NUMber?`** — Query channel count. Always `2` on this model.
- **`:SYSTem:COMMunicate:GPIB[:SELF]:ADDRess <integer>` / `?`** *(GPIB-only — out of scope for
  this manual's USB-TMC/LAN focus, listed for completeness)* — 0–30, default 2.
- **`:SYSTem:COMMunicate:LAN:APPLy`** — **Commit** pending LAN parameter changes. Nothing you set
  via the `:SYSTem:COMMunicate:LAN:*` commands below takes effect until you send this.
- **`:SYSTem:COMMunicate:LAN:AUTOip[:STATe] {ON|1|OFF|0}` / `?`** — Default `ON`. AutoIP acquires
  an address in `169.254.0.1`–`169.254.255.254` with mask `255.255.0.0`. **Priority when multiple
  modes are on: DHCP > AutoIP > ManualIP** — to actually use AutoIP, turn DHCP off. Not all three
  modes can be off simultaneously. Requires `:LAN:APPLy` to take effect.
- **`:SYSTem:COMMunicate:LAN:CONTrol?`** — Query the initial socket-communication control port.
  Returns `5555` if socket comms are supported, else `0`.
- **`:SYSTem:COMMunicate:LAN:DHCP[:STATe] {ON|1|OFF|0}` / `?`** — Default `ON`. Requires
  `:LAN:APPLy`.
- **`:SYSTem:COMMunicate:LAN:DNS <address>` / `?`** — DNS server IP, `nnn.nnn.nnn.nnn` (first
  octet 1–223 excluding 127). Only meaningful with ManualIP on. Requires `:LAN:APPLy`.
- **`:SYSTem:COMMunicate:LAN:DOMain <name>` / `?`** — Domain name, ≤99 chars, default (a
  serial-derived) `...RigolLan`.
- **`:SYSTem:COMMunicate:LAN:GATEway <address>` / `?`** — Default gateway IP, same format as DNS.
  ManualIP only. Requires `:LAN:APPLy`.
- **`:SYSTem:COMMunicate:LAN:HOSTname <name>` / `?`** — Host name, ≤99 chars, default (a
  serial-derived) `...rigollan`.
- **`:SYSTem:COMMunicate:LAN:IPADdress <ip_address>` / `?`** — Static IP, same format as DNS.
  ManualIP only. Requires `:LAN:APPLy`.
- **`:SYSTem:COMMunicate:LAN:MAC?`** — Query the instrument's MAC address, e.g.
  `00-14-0E-42-12-CF`.
- **`:SYSTem:COMMunicate:LAN:SMASk <mask>` / `?`** — Subnet mask, `nnn.nnn.nnn.nnn` (must be a
  contiguous bitmask). ManualIP only. Requires `:LAN:APPLy`.
- **`:SYSTem:COMMunicate:LAN:STATic[:STATe] {ON|1|OFF|0}` / `?`** — "ManualIP" mode. Default
  `OFF`. Same DHCP > AutoIP > ManualIP priority note as above — turn the higher-priority modes off
  to actually use static addressing. Requires `:LAN:APPLy`.
- **`:SYSTem:COMMunicate:LAN:UPDate`** — Persist all pending LAN changes to non-volatile memory
  and restart the LAN driver with the new settings. **Send this (after `:LAN:APPLy`) once you've
  finished all LAN parameter changes**, or they won't survive a power cycle.
- **`:SYSTem:COMMunicate:USB:INFormation?`** — Query the full USB VISA resource identity string,
  e.g. `:USB0::0x1AB1::0x0642::DG1ZA000000001::INSTR`.
- **`:SYSTem:COMMunicate:USB[:SELF]:CLASs {COMPuter|PRINter}` / `?`** — What kind of device is on
  the rear USB Device port. Default `COMPuter` — leave this alone for USB-TMC control.
- **`:SYSTem:CSCopy <name>,<name>`** — Channel copy: `CH1,CH2` copies all of CH1's
  parameters/state/arbitrary-waveform-data (except output on/off) onto CH2, or vice versa with
  `CH2,CH1`. Unavailable while channel coupling or track mode is on.
- **`:SYSTem:ERRor?`** — Pop the oldest entry off the error queue (query clears it). Returns
  `<code>,"<message>"`, e.g. `-113,"Undefined header; keyword cannot be found"`. Also clearable via
  `*CLS`, `*RST`, or a power cycle.
- **`:SYSTem:KLOCk[:STATe] {ON|1|OFF|0}` / `?`** — Lock/unlock front-panel keys (all except
  `Help`, which also toggles lock via press-and-hold). Default `OFF`.
- **`:SYSTem:LANGuage {ENGLish|SCHinese}` / `?`** — Default `SCHinese` on this vendor guide's unit
  — **explicitly set `ENGLish` if you need English-language front-panel prompts/errors**, don't
  assume the factory default.
- **`:SYSTem:POWeron {DEFault|LAST}` / `?`** — Power-on state: `DEFault` (factory, minus
  reset-exempt params) or `LAST` (prior session's params/state, except channel output on/off and
  clock source). Default `DEFault`.
- **`:SYSTem:PRESet {DEFault|USER1..USER10}`** — Reset to defaults, or recall one of the 10
  internal state slots directly (equivalent effect to `*RCL`, phrased as a `:SYSTem` command).
- **`:SYSTem:ROSCillator:SOURce {INTernal|EXTernal}` / `?`** — System-level alias of
  `:ROSCillator:SOURce` documented above; same behavior (auto-fallback to internal if no valid
  external clock is present, same multi-instrument-sync use case via `[10MHz In/Out]`).
- **`:SYSTem:SECurity:IMMediate`** — **Destructive**: sanitizes all user-accessible memory (state
  files, arbitrary waveforms, I/O settings including IP address) and restores factory values.
- **`:SYSTem:VERSion?`** — Query the SCPI standard version implemented, `YYYY.V` form, e.g.
  `1999.0`.

### `:TRIGger` Commands

A second, channel-scoped path to burst/sweep triggering (functionally overlapping with the
`[:SOURce[<n>]]:BURSt:TRIGger:*` / `:SWEep:TRIGger:*` commands above — same underlying trigger
engine, different command-tree entry point).

- **`:TRIGger[<n>]:DELay {<seconds>|MIN|MAX}` / `?`** — Burst delay (N-cycle/infinite only), same
  semantics/range as `[:SOURce[<n>]]:BURSt:TDELay`. Default 0 s.
- **`:TRIGger[<n>][:IMMediate]`** — Fire a trigger on channel `<n>` (default CH1) — applies to
  whichever of burst/sweep is currently active in manual-trigger mode. Ignored if the channel
  output is off.
- **`:TRIGger[<n>]:SLOPe {POSitive|NEGative}` / `?`** — External-trigger edge, applies to
  burst/sweep alike. Default `POSitive`.
- **`:TRIGger[<n>]:SOURce {INTernal|EXTernal|BUS}` / `?`** — Default `INTernal`. Note: despite the
  enum literally being `BUS` here (this command's manual trigger source is invoked the same way as
  `MANual` elsewhere — via `*TRG`, `:TRIGger[<n>][:IMMediate]`, or the `:SOURce`-path immediate
  commands), it plays the same functional role as `MANual` in the `:SOURce:BURSt:TRIGger:SOURce`
  / `:SOURce:SWEep:TRIGger:SOURce` enums.

## Worked end-to-end example: build and output a user-defined arbitrary waveform

Combines the vendor guide's "To Output Basic Waveform" and "To Output Arbitrary Waveform"
examples (Chapter 3) into one flow: confirm connectivity, output a plain sine to prove the basics
work, then switch to a small custom arbitrary waveform.

```scpi
*IDN?
; -> "Rigol Technologies,DG1062Z,DG1ZA000000001,00.01.03"  -- confirms comms are alive

:SOUR1:APPL:SIN 500,2.5,1,90
; Output a 500 Hz, 2.5 Vpp, 1 VDC-offset sine with 90 deg start phase, in one call
:OUTP1 ON
; Turn CH1's output on -- you should now see the sine on a scope

:SOUR1:APPL:ARB 500
; Switch CH1 to arbitrary waveform, sample-rate-mode carrier at 500 Hz
:SOUR1:DATA VOLATILE,-0.6,-0.4,-0.3,-0.1,0,0.1,0.2,0.3,0.5,0.7
; Download 10 normalized points (-1..+1 scale) into CH1's volatile arbitrary waveform buffer
; -- this also auto-switches CH1 to output the volatile waveform

*OPC?
; -> 1   -- block until the download/switch settles before touching the channel further

:SOUR1:APPL?
; -> "USER,5.000000E+02,5.000000E+00,0.000000E+00,0.000000E+00"  -- confirm the new config
```

To make the same edit permanent, store it to non-volatile memory and give it a name:

```scpi
:SOUR1:DATA:COPY MYWAVE.RAF,VOLATILE     ; copies FROM volatile TO a named nonvolatile slot is
                                          ; not this command's direction -- to persist FROM
                                          ; volatile, use MMEMory or *SAV instead:
*SAV ARB1                                 ; saves the current channel's arbitrary waveform data
                                           ; to internal nonvolatile slot 1 as Scpi1.RAF
*RCL ARB1                                 ; recall it later
```

## Common gotchas

- **`[<n>]` defaults to CH1 everywhere.** If you only ever write to CH1 and never explicitly
  address CH2, that's correct-by-default — but the reverse mistake (assuming an unqualified
  command touched "the currently selected channel" per the front panel's `:SYSTem:CHANnel:CURrent`
  setting) is wrong: remote commands are always explicit or default to CH1, never influenced by
  front-panel channel selection.
- **Query replies are always scientific notation, 7 significant digits**, regardless of what unit
  suffix you used on the *set* command (e.g. you can `SOUR1:FREQ 1MHz` but the query always answers
  in bare Hz: `1.000000E+06`). Parse accordingly — don't assume the reply echoes your input's
  scale.
- **Mode-exclusivity cascade**: enabling modulation, sweep, or burst on a channel auto-disables
  whichever of the *other two* was active; harmonic mode blocks modulation/sweep entirely (turn
  harmonic off first). If your sequence enables things in the "wrong" order, you'll silently lose
  a setting you thought was still active — always re-query state (`:MOD:STATe?`, `:SWEep:STATe?`,
  `:BURSt:STATe?`, `:HARMonic:STATe?`) after a mode change if you're chaining several.
  - **Coupling/deviation/ratio set-before-enable**: for all three `:COUPling:*` families (and
  their `:SOURce:FREQuency:COUPle:*` / `:SOURce:VOLTage:COUPle:*` aliases), you must select the
  mode and set the deviation/ratio *before* turning the coupling state on — attempts to change
  mode or deviation/ratio while already enabled are rejected.
- **LAN changes need two commands, not one**: `:SYSTem:COMMunicate:LAN:APPLy` makes new IP/DHCP/
  DNS/etc. settings take effect on the *running* LAN driver; `:SYSTem:COMMunicate:LAN:UPDate`
  persists them to non-volatile memory and restarts the driver. Sending only `:APPLy` means your
  changes are live but will revert on the next power cycle.
- **Arbitrary-waveform point-count auto-behavior** (`[:TRACe]:DATA:DAC`): 8–8192 points
  auto-interpolate to 8192 in frequency mode (but not sample-rate mode); >8192 up to 16384
  auto-switches you into sample-rate mode whether you asked for it or not. If your code assumes
  the point count you sent is the point count that ends up active, verify with
  `[:TRACe]:DATA:POINts? VOLATILE` after the download.
- **Impedance setting doesn't change actual output impedance** (always 50 Ω) — it only changes how
  the instrument *computes* the amplitude/offset values it reports and clamps against, to match
  your actual load. Get this wrong and every amplitude number the instrument shows you is correct
  for a load you don't actually have.
- **`*OPC?` vs `*OPC`**: `*OPC?` blocks and is a genuine synchronization point (read its `1`
  response before sending anything else); `*OPC` merely sets a status bit asynchronously — if you
  need to know a multi-step configuration (e.g. an arbitrary-waveform download) has actually
  landed before you touch the channel again, use `*OPC?`, not `*OPC`.

## What's excluded and why

- **GPIB.** The DG1000Z reaches GPIB only via an external USB-GPIB interface converter cabled into
  the *front-panel* USB Host port (`:SYSTem:COMMunicate:GPIB[:SELF]:ADDRess` sets its bus address
  once attached) — it is not a native rear-panel interface like USB-TMC and LAN are. The user's
  request was scoped to USB-TMC and LAN/LXI, and GPIB needs extra hardware neither of those does,
  so GPIB-specific framing/setup is out of scope here. The one GPIB command that exists
  (`:SYSTem:COMMunicate:GPIB[:SELF]:ADDRess`) is listed above for completeness but not elaborated
  on. If GPIB becomes a real requirement, add the converter and treat this exactly like any other
  SCPI-over-GPIB instrument — the command *set* itself (everything under "Command reference"
  above) is identical regardless of transport.
- **`:PA` Commands (external power amplifier).** This subsystem (`:PA:GAIN`, `:PA:OFFSet[:STATe]`,
  `:PA:OFFSet:VALUe`, `:PA:OUTPut:POLarity`, `:PA:SAVE`, `:PA[:STATe]`) controls a separate Rigol
  PA1011 power-amplifier accessory connected to the generator — it is not part of the base
  DG1062Z and wasn't requested. If a PA1011 (or compatible) is added to the setup later, these six
  commands are straightforward gain/offset/polarity/enable controls in the same style as
  everything else in this manual — add that subsection then.
- **16M internal memory option ("Arb 16M").** `*OPT?` reports whether this per-instrument-licensed
  option (installed via `:LICense:INSTall <sn>`, serial number obtained from Rigol) is present.
  This manual documents the full base command set as-is — none of it was omitted for licensing
  reasons — but be aware that arbitrary-waveform memory-depth behavior in `:MEMory`/`:MMEMory`/
  `:SOURce:TRACe` may differ once that option is installed (deeper storage, more headroom in the
  `[:TRACe]:DATA:DAC`/`:DAC16` point-count auto-switch thresholds). Check `*OPT?` before assuming
  which regime you're in on a different unit.

  **Confirmed status on the DG1062Z this manual was written for: the Arb 16M option is
  installed** (per the user, 2026-09-24). The vendor programming guide itself doesn't spell out
  a separate, distinct command syntax or numeric limit set for the licensed-vs-unlicensed state
  beyond the `*OPT?`/`:LICense:INSTall` gating described above — the point-count ranges and
  memory-slot counts documented throughout this manual (e.g. `:SOURce:TRACe` above, `:MEMory`/
  `:MMEMory` below) are what the guide states outright, and they should be taken as accurate for
  this licensed unit. If a `DevTerm.Devices.Scpi` profile is later built from this manual, it can
  assume the extended-memory regime is active rather than treating it as an unknown runtime
  condition to probe via `*OPT?` first — though probing is still harmless and more portable if
  the profile might ever target an unlicensed DG1062Z.
