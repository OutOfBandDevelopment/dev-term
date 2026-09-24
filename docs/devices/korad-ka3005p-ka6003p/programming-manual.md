# Korad KA3005P — Programming Manual (USB / RS-232)

Single-channel, 30V/5A programmable DC linear power supply. This manual covers the full
remote-control command set, which is identical whether reached over the rear-panel **USB**
port or the rear-panel **RS-232** port — there is no framing/syntax difference between the
two on this instrument, so both are covered together.

## Source documents

1. **"KA Series Remote Control Syntax V2.0"** — Korad Technology Co., Ltd. This is the
   vendor's own command-protocol reference sheet (2 pages) for the single-channel "P"-suffix
   KA-series power supplies (KA3005P, KA3003P, KA6003P, KA6002P, KA3010P, and others sharing
   this firmware protocol family). It is the primary source for every command in this manual.
   A copy circulates in several open-source Korad client projects, e.g.
   [`DudeYarvie/Korad-KA3005P-PySerial-Example`](https://github.com/DudeYarvie/Korad-KA3005P-PySerial-Example),
   under the filename `KA Series Single Channel Remote Control Syntax V2.0.pdf`.
2. **KORAD KA3000-6000 Series Digital Control and Programmable DC Power Supply User
   Manual** (KORAD Technology Co., Ltd, current as of May 2024) — covers KA3003D/P,
   KA3005D/P, KA3010D/P, KA6002D/P, KA6003D/P, KA6005D/P. Used here for serial-port
   settings and the remote-control entry/exit procedure. Confirms: "Baud rate: 9600, Parity
   bit: None, Data bit: 8, Stop bit: 1, Data flow control: None."
3. **RND 320-KA3005D/P User Manual** (RND lab — a rebrand of the same Korad hardware) —
   cross-checked for rear-panel connector layout and the remote-control exit procedure, since
   it documents the P-suffix model's physical panel more explicitly than source 2. Confirms
   the rear panel has a separate **USB Interface** and a separate **RS232 Interface**, both
   labeled "(only ...-P model)", and describes the exit sequence: closing the remote-control
   application / disconnecting the cable returns the unit to front-panel control with an
   audible beep.
4. Cross-checked against the community-maintained protocol notes at
   [sigrok.org/wiki/Korad_KAxxxxP_series](https://sigrok.org/wiki/Korad_KAxxxxP_series) for
   known firmware quirks not called out in the vendor sheet (see "Common gotchas" below).
   Sigrok's notes are used only to corroborate real-world behavior, not as a primary source
   for command syntax — the vendor sheet (source 1) wins on any conflict.

## Before you start

### Physical connection

The KA3005P's rear panel has two independent remote-control connectors:

- **USB** — a USB port that presents as a virtual serial (COM) port on the host PC. Depending
  on the unit's USB-to-serial chipset (commonly CH340 or PL2303), you may need to install a
  vendor driver for the port to enumerate; no Korad-specific driver is required for the
  command protocol itself; any serial terminal or serial library works once the COM port
  exists.
- **RS-232** — a standard RS-232 port wired for direct connection to a PC's serial port (or a
  USB-to-RS232 adapter).

Both ports speak the **exact same ASCII command protocol** described below — there is no
GPIB, LAN, or other interface on this instrument, and no command-syntax difference between
the USB and RS-232 paths. Use whichever is physically convenient.

### Serial settings

| Setting | Value |
|---|---|
| Baud rate | 9600 |
| Data bits | 8 |
| Parity | None |
| Stop bits | 1 |
| Flow control | None |

### Entering / exiting remote control

The unit does not require an explicit "go to remote" command — opening the serial
connection and sending commands is sufficient, and the front panel locks out local control
while a host is connected. To return to local (front-panel) control, close the
controlling application and disconnect the cable; the unit beeps once and unlocks the front
panel (per the RND 320-KA3005D/P manual's documented exit procedure).

### Model scope note

This manual documents **KA3005P** specifically (30V / 5A, single channel). The vendor
protocol sheet (source 1) is shared verbatim across the whole single-channel "P"-suffix KA
family (KA3003P, KA6002P, KA6003P, KA3010P, etc.) — the command *set* is identical, but the
valid voltage/current *ranges* differ per model's rated output. Do not reuse the numeric
ranges below for a different model in the family without checking that model's rated
voltage/current.

**Verified for KA6003P specifically:** the same generic vendor sheet (redistributed by RND
as "Single Channel Remote Control Syntax 2.0 – KA Series," with no per-model syntax branch)
is confirmed by two independent open-source KA6003P control tools
([`dj-on-github/korad_control`](https://github.com/dj-on-github/korad_control),
[`Tamagotono/Korad-KA6003P-Software`](https://github.com/Tamagotono/Korad-KA6003P-Software))
to use the identical 14-command set at 9600/8N1 — same `VSET`/`ISET`/`IOUT`/`VOUT`/`OUT`/
`STATUS?`/`*IDN?`/`RCL`/`SAV`/`OCP`/`OVP`/`BEEP` commands, same fixed-decimal formats
(voltage `XX.XX`, current `X.XXX`), no additional or renamed commands. The only real
difference is the rated range: **KA6003P is 60V / 3A**, so its `VSET1`/`VSET1?` values run
`00.00`–`60.00` and its `ISET1`/`ISET1?`/`IOUT1?` values run `0.000`–`3.100`, in place of this
manual's `30.00` / `5.100` ceilings. `*IDN?` on a KA6003P returns `KORAD KA6003P V<firmware>`
instead of `KORAD KA3005P V<firmware>`. Every command, gotcha, and the worked example in this
manual apply to KA6003P unchanged once those two range substitutions are made — no separate
manual is needed for it.

## Command syntax conventions

- Every command is a plain ASCII string, case-sensitive as written (uppercase), with **no
  line terminator** (no CR, no LF) appended by the host or expected in the reply — this is
  the single most important framing detail for this protocol (see "Common gotchas").
- `<X>` is the output channel number. The KA3005P is single-channel, so `<X>` is always `1`.
- `<NR2>` denotes a fixed-decimal numeric parameter (e.g. `20.50` for voltage, `2.225` for
  current) — see the per-command value-format notes below; the instrument does not accept
  scientific notation or a variable number of decimal places.
- `<NR1>` denotes a plain integer parameter (used only by `RCL`/`SAV` memory-slot numbers).
- `<Boolean>` is a single digit: `1` = on/enable, `0` = off/disable.
- A command with no `?` is a **set** command and produces no reply. A command ending in `?`
  is a **query** and produces exactly one reply, sent back with no terminator — the host must
  read a fixed/expected number of bytes or apply a read timeout rather than scanning for a
  line ending.
- There is no command concatenation/chaining syntax — send one command, read its reply (if
  it is a query), then send the next.

## Command reference

### 1. `ISET<X>:<NR2>`

- **Syntax:** `ISET1:<current>` — set command, no reply.
- **Description:** Sets the CH1 output current limit.
- **Value format:** 3 decimal places, amps, e.g. `2.225`. For the KA3005P's 5A rating, valid
  range is `0.000`–`5.100` (per community-observed behavior; the vendor sheet does not state
  the exact range, only the format).
- **Example:** `ISET1:2.225` — sets the CH1 current limit to 2.225 A.

### 2. `ISET<X>?`

- **Syntax:** `ISET1?` — query.
- **Description:** Returns the current *limit setting* for CH1 (not the actual live output
  current — use `IOUT1?` for that).
- **Return format:** Same 3-decimal-place ASCII string as the set command, e.g. `2.225`, with
  no terminator.
- **Gotcha:** on some firmware/protocol revisions this query is reported to return one extra
  trailing byte beyond the expected 5 characters — read defensively (see "Common gotchas").

### 3. `VSET<X>:<NR2>`

- **Syntax:** `VSET1:<voltage>` — set command, no reply.
- **Description:** Sets the CH1 output voltage limit.
- **Value format:** 2 decimal places, volts, e.g. `20.50`. For the KA3005P's 30V rating,
  valid range is `00.00`–`30.00` (some units accept slightly above rated, up to `~31.00`, as
  headroom — treat `30.00` as the documented ceiling and don't rely on any overrange).
- **Example:** `VSET1:20.50` — sets the CH1 voltage to 20.50 V.

### 4. `VSET<X>?`

- **Syntax:** `VSET1?` — query.
- **Description:** Returns the voltage *limit setting* for CH1 (not the live output — use
  `VOUT1?`).
- **Return format:** 2-decimal-place ASCII string, e.g. `20.50`, no terminator.

### 5. `IOUT<X>?`

- **Syntax:** `IOUT1?` — query.
- **Description:** Returns the actual, live output current being drawn on CH1 right now.
- **Return format:** 3-decimal-place ASCII string, amps, e.g. `1.203`.
- **Gotcha:** this differs from `ISET1?` — `IOUT` is a measurement, `ISET` is the configured
  limit. If the supply is in constant-current (CC) mode, `IOUT1?` will read at/near the
  `ISET1` value; in constant-voltage (CV) mode it reads whatever the load is actually
  drawing, which can be far below the limit.

### 6. `VOUT<X>?`

- **Syntax:** `VOUT1?` — query.
- **Description:** Returns the actual, live output voltage on CH1 right now.
- **Return format:** 2-decimal-place ASCII string, volts, e.g. `12.34`.
- **Gotcha:** in CC mode this reads whatever the load's IR drop produces, not `VSET1`.

### 7. `BEEP<Boolean>`

- **Syntax:** `BEEP1` / `BEEP0` — set command, no reply.
- **Description:** Enables (`1`) or disables (`0`) the unit's front-panel key-press/alert
  beep.
- **Example:** `BEEP0` — silences the beeper.

### 8. `OUT<Boolean>`

- **Syntax:** `OUT1` / `OUT0` — set command, no reply.
- **Description:** Turns the CH1 output relay on (`1`) or off (`0`). While off, the output
  terminals are disconnected regardless of the voltage/current limit settings.
- **Example:** `OUT1` — enables the output.
- **Gotcha:** always issue `ISET1`/`VSET1` *before* `OUT1` when starting a new test — enabling
  the output first can briefly apply whatever limits were previously set (including a prior
  session's values, since the unit retains its last state).

### 9. `STATUS?`

- **Syntax:** `STATUS?` — query.
- **Description:** Returns a single status byte reporting the unit's current mode.
- **Return format:** One raw byte (not ASCII digits — read it as a byte and inspect bits),
  bit-mapped as follows:

  | Bit | Meaning |
  |---|---|
  | 0 | CH1 mode: 0 = CC (constant current), 1 = CV (constant voltage) |
  | 1 | CH2 mode (not applicable on the single-channel KA3005P) |
  | 2–3 | Output tracking mode (not applicable — single channel) |
  | 4 | Beep: 0 = off, 1 = on |
  | 5 | Front-panel lock: 0 = locked, 1 = unlocked |
  | 6 | Output: 0 = off, 1 = on |
  | 7 | N/A |

- **Gotcha:** community testing (sigrok project) found only bits 0 (CV/CC), 5/6 depending on
  firmware, and 6 (output on/off) to be consistently reliable across units/firmware
  revisions; treat bits 1–4 and 7 as informational at best. Don't build critical logic on
  the beep or lock bits without validating against your specific unit's firmware.

### 10. `*IDN?`

- **Syntax:** `*IDN?` — query.
- **Description:** Returns the instrument identification string.
- **Return format:** `KORAD KA3005P V<firmware>`, e.g. `KORAD KA3005P V2.0` — manufacturer,
  model, and firmware version concatenated with spaces (not comma-separated like typical
  SCPI `*IDN?` — this instrument is *not* a true SCPI device, only SCPI-*like*).
- **Use this as the connectivity smoke test** — it's the safest first command to send when
  verifying a new connection, since it's read-only and has a well-known fixed reply shape.

### 11. `RCL<NR1>`

- **Syntax:** `RCL1` through `RCL5` (see gotcha below) — set command, no reply.
- **Description:** Recalls a previously saved voltage/current panel setting from internal
  memory into the active output setting (equivalent to pressing the corresponding `M`
  button on the front panel).
- **Example:** `RCL1` — recalls memory slot 1.
- **Gotcha — vendor documentation is internally inconsistent here:** the source sheet's own
  text says "NR1 1 - 5: Memory number 1 to 4," i.e. it states the valid range is 1–5 in one
  breath and then labels it "1 to 4" in the next. Treat memory slots **1–4** as the
  documented-safe range; the front panel commonly shows 5 `M` buttons on this product line,
  so a 5th slot may exist, but verify against real hardware before depending on `RCL5`/`SAV5`.

### 12. `SAV<NR1>`

- **Syntax:** `SAV1` through `SAV5` (see gotcha under `RCL`, above) — set command, no reply.
- **Description:** Stores the current voltage/current panel setting into internal memory
  slot `<NR1>` for later recall via `RCL<NR1>`.
- **Example:** `SAV1` — stores the active settings into memory slot 1.

### 13. `OCP<Boolean>`

- **Syntax:** `OCP1` / `OCP0` — set command, no reply.
- **Description:** Enables (`1`) or disables (`0`) over-current protection — when enabled,
  the output trips off if the load draws more than the `ISET1` limit rather than simply
  current-limiting.
- **Example:** `OCP1` — turns on over-current protection.

### 14. `OVP<Boolean>`

- **Syntax:** `OVP1` / `OVP0` — set command, no reply.
- **Description:** Enables (`1`) or disables (`0`) over-voltage protection — when enabled,
  the output trips off if the output voltage exceeds the `VSET1` limit (relevant mainly in
  CC mode, where back-EMF from a load could push voltage up).
- **Example:** `OVP1` — turns on over-voltage protection.

## Worked end-to-end example: set limits, enable output, read back

A typical bench-automation sequence — configure a voltage/current limit, verify it took,
enable the output, and poll the live readings:

```text
*IDN?
  -> KORAD KA3005P V2.0          (confirms you're talking to the right device)

VSET1:12.00                      (set voltage limit to 12.00 V)
ISET1:1.000                      (set current limit to 1.000 A)

VSET1?
  -> 12.00                       (read back the voltage limit to confirm it was accepted)
ISET1?
  -> 1.000                       (read back the current limit to confirm it was accepted)

OCP1                             (enable over-current protection before energizing)
OVP1                             (enable over-voltage protection before energizing)

OUT1                             (enable the output)

VOUT1?
  -> 11.98                       (actual output voltage under load)
IOUT1?
  -> 0.412                       (actual output current under load)

STATUS?
  -> <one byte>                  (inspect bit 6 to confirm output is on, bit 0 for CV/CC mode)

OUT0                             (disable the output when done)
```

Note there is no acknowledgement after a set command (`VSET1:12.00`, `OUT1`, etc.) — the
protocol has no ACK/NAK. The only way to confirm a set command was accepted is to
immediately follow it with the matching query, as shown above.

## Common gotchas

- **No line terminators anywhere.** Neither commands sent to the unit nor replies from it
  are terminated with CR/LF or any other delimiter. A host implementation that waits for a
  newline to know a reply is complete will hang. Read either a fixed number of bytes
  (each reply's byte length is deterministic per command, given the fixed-decimal formats
  above) or apply a short read timeout after which "no more bytes" means "reply complete."
- **`*IDN?` is not comma-delimited.** Unlike conventional SCPI `*IDN?` replies
  (`manufacturer,model,serial,firmware`), this unit returns a single space-separated string
  with no serial number field at all. Don't parse it as if it were standard SCPI.
- **Set commands produce no reply and no error indication.** There is no status/error queue
  and no command-rejection mechanism — an out-of-range or malformed set command is either
  silently ignored or silently clamped to the nearest valid value, depending on firmware.
  Always read back with the matching query after any set command in a script that needs to
  verify state.
- **The unit remembers its last output state across power cycles** in some firmware
  revisions — don't assume a freshly powered-on unit has `OUT` off or default V/I limits;
  explicitly set what you need before calling `OUT1`.
- **Front panel locks out while remote-connected.** Don't leave a serial connection open
  indefinitely if a human may need physical access to the unit — disconnect to hand control
  back to the front panel.
- **This is not true SCPI**, despite superficial resemblance (query commands ending in `?`,
  colon-free flat command names). There's no `:SYSTem`, no `*RST`, no standard event/status
  registers, and no command tree — it's a small flat ASCII command set specific to this
  product family. Don't assume any generic SCPI behavior beyond what's documented above.

## What's excluded and why

- **Nothing was scoped out of this manual for interface reasons.** The KA3005P's USB and
  RS-232 ports share one identical protocol with no per-interface framing differences (unlike
  GPIB-vs-serial on many other instruments), so unlike some other manuals in this repo, there
  is no "GPIB-only" or "LAN-only" subsection to strip out here — both requested interfaces are
  fully covered by the single command reference above.
- **Other KA-series models (KA3003P, KA6002P, KA6003P, KA3010P, KA6005P, etc.) are out of
  scope for a dedicated manual, but not because the protocol differs.** They share the exact
  same command *protocol* (source 1 documents the whole single-channel "P" family together;
  verified independently for KA6003P — see "Model scope note" above), and this manual states
  value ranges (`30.00` V, `5.100` A) specific to the KA3005P. If a profile is later built for
  KA6003P or another family member, the command list here can be reused as-is — only the
  per-command numeric ranges (and the `*IDN?` model string) need to change; no new research
  pass is required.
- **Dual/multi-channel "D-2S"-style Korad supplies are out of scope.** Those use a related
  but distinct multi-channel command set (documented in a separate "KA Series multiple
  channel DC power supplies User Manual") that adds channel-tracking and series/parallel
  commands not present in the single-channel protocol documented here. The KA3005P is
  single-channel, so none of that applies, but a future manual for a "D-2S"-suffix model
  should not assume this document's command list is complete for it.
- **No GPIB, LAN, or other interface exists on this instrument at all** — there was nothing
  further to exclude on that front; USB and RS-232 are the only two ports this unit has.
