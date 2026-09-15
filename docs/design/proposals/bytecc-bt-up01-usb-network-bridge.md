# Proposal: BYTECC BT-UP01 — USB-over-Network Device Server

## Source

Unlike every other proposal in this directory, **there is no reverse-engineering to draw on here
yet** — no prior notes, no working code, no public writeup found. This document is a research
starting point, not a protocol spec: checked directly (2026-09-15) via web search, no independent
technical breakdown of this specific device's wire protocol turned up anywhere, and no shared OEM
reference design/chipset lineage was found to piggyback on either (unlike, say, the
[DE-5000](de5000-lcr-meter-protocol.md), whose chipset is shared across several rebadged meters).
Treat everything below as "the landscape," not "the spec."

## Device

[BYTECC BT-UP01](https://www.bytecc.com/) ("USB Net Share Station") — a small box with a
10/100 Mbps Ethernet port and a USB 2.0 port (backward-compatible with USB 1.1), marketed for
sharing a USB device (originally aimed at printers) across a network.

## Why this is a fundamentally different kind of proposal than everything else here

Every other transport/decoder proposal in this directory is about **one specific device's byte
protocol** riding over an existing transport (serial, TCP, HID, BLE). This device is different in
kind: it's asking dev-term to be a client for **USB itself, tunneled over the network** — not "read
and write bytes to this thing," but "make a USB device physically attached to a box elsewhere on
the network behave as if it were plugged into this machine." That's a materially bigger scope than
anything else proposed so far. Two genuinely different paths exist, worth deciding between rather
than assuming one:

### Option A — use the vendor's own client software, treat the result as local

USB device servers like this typically ship a proprietary Windows (sometimes Mac/Linux) client that
installs a virtual USB controller/hub driver: the client intercepts USB traffic locally and tunnels
it to the box, which replays it against the real attached device. **If that's how BT-UP01 works**,
the remote device shows up to the OS as an ordinary local device once the vendor client is running
— and dev-term already handles ordinary local devices (serial, HID, and eventually USBTMC). No new
dev-term work would be needed at all; the "network" part is entirely the vendor's problem, already
solved by their own software. This is almost certainly the fastest path to the user's actual stated
goal ("access some test equipment over the network") if the target OS is one the vendor supports.

### Option B — reverse-engineer BT-UP01's own wire protocol

The alternative — dev-term speaking the box's network protocol directly, without needing the
vendor's driver installed — is a much bigger undertaking:

- USB is host-controller-driven and timing-sensitive at a level far below "send bytes, get bytes
  back." A real implementation means tunneling USB Request Blocks (URBs) — device enumeration,
  control/bulk/interrupt/isochronous transfers, endpoint management — not just decoding a fixed
  packet format the way every other proposal here does.
- There's a real, existing open protocol for exactly this (**USB/IP**, part of the Linux kernel),
  but nothing found so far indicates BT-UP01 speaks it rather than something proprietary — that
  itself is unknown and would need checking (e.g. does a Linux `usbip` client happen to talk to it
  successfully? That would resolve this immediately, one way or the other, with zero reverse
  engineering).
- With no existing protocol writeup, no chipset lineage, and no captured traffic, the honest
  starting point is a **network + USB capture of the vendor's own client talking to a real unit** —
  the same "capture first, decode second" method already used for every other device in this
  directory, just at a level (USB transaction tunneling) this project hasn't attempted before.

## Recommendation

Try **Option A first** — check whether the vendor's own client software (if it still exists/installs
on a current OS) just makes the remote USB device appear local, since that fully satisfies "access
test equipment over the network" with zero new dev-term work. Only invest in Option B (real
reverse engineering, starting with a USB/IP compatibility check before assuming anything
proprietary) if Option A doesn't pan out — e.g. the vendor software is abandoned/unsupported on a
needed OS, or the goal is specifically to avoid depending on it.

## Open questions

- Does the vendor's client software still exist/install/work on a current OS? (The product looks
  old enough that this is a real risk, not a formality.)
- Does BT-UP01 respond to a standard Linux `usbip` client at all? This is a five-minute check that
  would immediately answer "proprietary or not" without any reverse engineering.
- If proprietary: what's actually on the wire? Needs a real capture (Wireshark on the network side,
  a USB analyzer or USBPcap on the client side simultaneously) with a real unit and a real USB
  device attached — nothing here can be designed further without that.
- Whether dev-term should even be the right home for a URB-tunneling USB transport if Option B ever
  happens — this looks architecturally closer to "a new kind of thing" than "another `ITransport`,"
  and is worth a dedicated design discussion (not attempted in this document) rather than assuming
  it slots into the existing transport contract unchanged.
