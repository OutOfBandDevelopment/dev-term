# Proposal: EByte E810-DTU(RS485) — Ethernet-to-Serial Bridge Configuration Protocol

## Source

Unlike the other proposals in this directory (sourced from `mwwhited-notes/shared`), this one comes
from a different personal project — a local binary-protocol-decoder library:

- `C:\repo\oobdev\dotex\Incoming\BinaryDecoders\src\OoBDev.EByteElectronicTechnology\e810dturs485_notes.txt` —
  raw reverse-engineering notes (UDP packet captures + a hand-annotated field table), not a
  finished writeup like [Radex One](../features/radex-one-protocol.md)'s source — treat the field table below
  as a working hypothesis to verify against a fresh capture, not a settled spec.
- `C:\repo\_archives\BinaryDataDecoders\src\BinaryDataDecoders.EByteElectronicTechnology\e810dturs485_notes.txt` —
  a second archived copy of the same raw notes (same filename, different archive path/project
  rename), consulted 2026-09-30 to resolve the byte-count discrepancy below: it contains several
  consecutive real captures (TCP client/server, UDP client/server, plus a run of incremental
  field-probing edits) where the earlier archived copy apparently only had one example. Walking one
  full 203-byte capture from this copy offset-by-offset (not just summing the field table) is what
  actually found the error — see "Open questions."

## Device

[EByte E810-DTU(RS485)](https://www.cdebyte.com/) (model code `0x03` in its own config response,
firmware `0x16`) — an Ethernet-to-serial bridge, conceptually the same role as the
[USR-TCP232-302](https://shop.usriot.com/1-port-rs232-to-ethernet-converters-usr-tcp232-302.html)
already used in this project (see the top-level [README](../../../README.md) and the vendor-variance
caveat in [rfc2217.md](../rfc2217.md)), but a different manufacturer (Ebyte/`cdebyte.com`, Chinese)
with an entirely different, non-RFC2217, non-USR configuration protocol. Bridges Ethernet to
RS-485 (and possibly RS-422 electrically — the notes only cover the RS485 model variant).

## Why this is a distinct case from the USR-TCP232-302 already in dev-term

The USR device's actual *data* passthrough already works today via the plain TCP transport — no
protocol work was needed there, only picking the right port/mode (see the README's status notes).
This device is different in one specific way: **its configuration is a whole separate protocol**,
not something dev-term has needed to touch before:

- **Data passthrough** (once configured): presumably a plain raw TCP/UDP socket mirroring the
  RS-485 bytes, same as the USR device — no new transport work implied, whatever's actually wired
  to the RS-485 port is a separate, device-specific decoder concern for later.
- **Configuration** (this proposal's actual subject): a **UDP broadcast protocol on port 1901** —
  discover devices, read/write their network+serial+behavior settings, and reboot them. This is a
  genuinely new category for dev-term: not decoding telemetry from an instrument, but *configuring
  a piece of network infrastructure* (the bridge itself) — closer in spirit to
  [device-control-modules.md](../device-control-modules.md)'s `IControlSurface` concept, but aimed
  at the bridge hardware rather than a measurement instrument. If dev-term ever wants a "find and
  set up my serial-to-Ethernet bridges" feature, this is what it would look like.

## Protocol summary (from the source notes — see caveats above)

All UDP, broadcast to port 1901 unless noted:

- **Discovery**: broadcast the literal ASCII payload `www.cdebyte.comwww.cdebyte.com` (the magic
  string, doubled) to `255.255.255.255:1901`. Every E810-DTU on the network replies with its full
  config (see below).
- **Reboot**: broadcast `FE03` + 6-byte target MAC to port 1901.
- **Read/write config**: the notes state the 2-byte command is `FD00` = Read, `FE00` = Write, but
  the actual captures include a distinct `FD01` too (see open questions — this needs resolving,
  not guessing).

**Config packet fields** (as documented in the source notes, in order):

```
Command          : 2 bytes  (FD00=Read, FE00=Write per the notes; FD01 also observed - unresolved)
MAC address       : 6 bytes
Address type      : 1 byte   (0x00=static, 0x01=DHCP)
IP address        : 4 bytes
Gateway           : 4 bytes
Subnet mask       : 4 bytes
Primary DNS       : 4 bytes
Secondary DNS     : 4 bytes
Local port        : 2 bytes
Target type       : 1 byte   (0x00=remote IP, 0x01=DNS name)
Target IP/name    : 64 bytes (ASCII, zero-padded) [corrected 2026-09-30 from 60 - see "Open questions"]
Target port       : 2 bytes
Connection type   : 1 byte   (0x00=TCP client, 0x01=TCP server, 0x02=UDP client, 0x03=UDP server)
Serial framing    : 1 byte   (0x00=8N1, 0x01=8O1, 0x02=8E1)
Baud rate         : 3 bytes
Short link switch : 1 byte   (meaning unclear - marked "?" in the source notes themselves)
Timeout           : 2 bytes
Wipe cache        : 1 byte   (0x00=closed, 0x01=open)
Custom-registry type   : 1 byte  (0=off, 1=send MAC on connect, 2=send custom on connect, 3=send MAC every packet, 4=send custom every packet)
Custom registry        : 40 bytes (ASCII, zero-padded)
Custom registry length : 1 byte
Heartbeat type          : 1 byte  (0=network, 1=serial)
Heartbeat message       : 40 bytes (ASCII, zero-padded)
Heartbeat length        : 1 byte
Heartbeat time          : 2 bytes
MTU                     : 2 bytes
Model                   : 1 byte  (0x03 = E810-DTU RS485)
Firmware version        : 1 byte
Unknown                 : 6 bytes (marked "???" in the source notes - not yet decoded)
```

## Proposed shape

```plantuml
@startuml
skinparam componentStyle rectangle
skinparam backgroundColor #FEFEFE

actor "User" as user
note right of user : WPF panel / TUI form / CLI flags

package "EByte E810-DTU Bridge Control Module (plugin)" {
  [E810-DTU Discovery/Config Surface] <<IControlSurface>> as surface
  note bottom of surface : Discover / Read Config /\nWrite Config / Reboot
  [E810-DTU Config Codec] <<internal>> as codec
  note bottom of codec : Fixed-field config record\n(network + serial + behavior settings)
}

[Session / Transport] <<ITransport>> as transport
note right of transport : UDP broadcast, port 1901\n(config channel only)

user --> surface : Invokes command\n(e.g. Discover, Set IP, Reboot)
surface --> codec : Builds request packet
codec --> transport : Broadcast/unicast UDP
transport --> codec : Device response(s)
codec --> surface : Parsed config fields
surface --> user : Human-readable text baseline\n(e.g. device list, current config)

note bottom of transport : Once configured, the device's actual\ndata passthrough is a plain TCP/UDP\nsocket - already covered by existing\ntransports, no new work there.
@enduml
```

- **This needs the not-yet-built UDP transport** (see `docs/design/transports.md`) to send/receive
  broadcast datagrams — the config protocol is entirely UDP, unlike the USR device's Telnet-ish
  config approach.
- **Broadcast discovery is itself a distinct capability** dev-term doesn't have a pattern for yet:
  send one datagram, collect replies from an a-priori-unknown number of devices within some window,
  rather than a single request/response pair. Worth deciding whether this is a generic
  `IControlSurface` "discovery" concept or specific glue in this module.

A "find and configure my bridges" feature is a genuinely GUI-shaped task — a scannable device list
plus a settings form reads far better than a CLI flag dump:

```plantuml
@startsalt
{
  {* File | Device | Help}
  {SI
    {T
      MAC | IP | Model | Mode
      B6:F4:E8:EE:E5:15 | 192.168.4.100 | E810-DTU RS485 | TCP Client
    }
    |
    {
      IP Address:  | "192.168.4.100"
      Gateway:     | "192.168.4.1"
      Target IP:   | "192.168.4.100"
      Target Port: | "8887"
      Mode:        | ^TCP Client^
      Baud:        | ^9600^  Framing: | ^8N1^
    }
  }
  {
    [Discover] | [Read Config] | [Write Config] | [Reboot]
  }
}
@endsalt
```

## Open questions

- ~~**Byte-count discrepancy**~~ **Resolved 2026-09-30.** Summing the field table's old widths gave
  199 bytes total against a real 203-byte captured packet — a 4-byte shortfall. Walking a full
  capture (the "TCP client" example, first line) offset-by-offset from the second archived notes
  copy (see "Source") against ASCII anchors in the payload (the literal text `192.168.4.100` for a
  DNS-name target, `regist msg`/`heart beat msg` for the two ASCII fields and their length bytes)
  found the real field boundaries: the `Target IP/name` field is **64 bytes**, not 60 — every field
  from there on (target port through the trailing unknown block) lines up exactly against the
  documented order once that one width is corrected, and the corrected sum (203 bytes) matches the
  real packet exactly. Every other field width in the table above checked out unchanged. Fixed in
  the field table above; still worth a fresh capture to confirm before writing any code, per the
  general caveat in "Source."
- **`FD00` vs `FD01`**: the notes' own summary says the command is `FD00=Read` or `FE00=Write`, but
  the captures include `FD01` too, still unexplained even after the 2026-09-30 pass above. In the
  second archived notes copy, `FD01` appears in exactly two consecutive response lines out of a
  ~15-line capture run, both immediately following an edit to the byte the field table calls "short
  link switch" (offset 103 in the 203-byte layout) — every other line in that run uses `FD00`. That
  correlation is circumstantial (2 data points, no explanation for *why* that field would flip the
  command byte, and command byte 1 sits nowhere near offset 103) and could just as easily be a
  hand-editing mistake in how the notes' author constructed that one test payload rather than real
  device behavior. Still needs a fresh, deliberate capture to resolve — not guessed at.
- Several fields are explicitly marked as not understood in the source notes themselves: the
  "short link switch" byte, and a trailing 6-byte "other???" block. These aren't safe to write
  blindly (a config protocol has real, immediate consequences on real hardware if a write gets an
  unknown field wrong) — read-only support (discovery + reading current config) is the safer first
  slice; treat write support as needing the unresolved fields nailed down first.
- Whether RS-422 variants of the E810-DTU family share this exact config format (the notes only
  cover the RS485 model) — worth checking if/when an RS-422 unit is in hand.

## Completion checklist

What is needed before this proposal can be closed. Tick items as they land, in the same change.

- [x] Field table's widths reconciled against a real captured packet (2026-09-30)
- [ ] Resolve the `FD00`/`FD01` ambiguity with a fresh, deliberate capture (blocker)
- [ ] Build the UDP transport (see `transports.md`; `BACKLOG.md`)
- [ ] Discovery/config codec and unit tests
- [ ] Both front ends' panel, plus a `docs/specs/` and `docs/user-guide/` entry
- [ ] Real-hardware pass; writes verified against a real unit

## Status

**Not started — design only.** No code exists yet, and it's gated on the not-yet-built UDP transport
(`docs/design/transports.md`; see `BACKLOG.md`'s Transports section). The byte-count discrepancy is
resolved (2026-09-30, see "Open questions") — the field table's widths now sum correctly against a
real captured packet. The `FD00`/`FD01` ambiguity is still open and needs resolving against a fresh,
deliberate capture before implementation, not just the existing source notes — this remains a
blocker, not a nice-to-have, since a config protocol has real, immediate consequences on real
hardware if a write uses a wrong field.
