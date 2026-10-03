# Z-Wave support

Sourced from `BACKLOG.md`'s "Proposed Ideas" section (added 2026-09-30): "Z-Wave support — ZStick,
ZWave RPi hat."

## Transport: already covered

Both named target devices are serial-attached — a Z-Stick (e.g. Aeotec's) is a USB-serial adapter
exposing the Z-Wave Serial API over a virtual COM port, and Z-Wave Raspberry Pi HATs typically expose
the same Serial API over UART. This needs **no new transport**: `DevTerm.Transports.Serial` as-is
covers it, the same way GPIB-via-Prologix and (per the [LXI proposal](../features/lxi-support.md)) raw-socket LXI
instruments need no new transport either — the new work here is entirely the protocol/decoder/
control-module layer above the byte stream, not how the bytes arrive.

## Protocol: a real Kaitai Struct candidate

The Z-Wave Serial API is a binary, frame-based protocol (start-of-frame byte, length, frame type,
command class, checksum, plus ACK/NAK/CAN handshake bytes) — squarely the shape
[device-control-modules.md](../device-control-modules.md) already flags Kaitai Struct (`.ksy`) as the
right tool for ("binary response layouts — byte-level fields, conditionals, repeats, bit widths"),
rather than the lightweight text send-template/response-pattern schema `DevTerm.Devices.Scpi` uses for
ASCII query/response gear. [Device manifests](../device-manifests.md) already reference `.ksy` as part
of its design ("a folder/zip of one, when a binary Kaitai `.ksy` reference is needed") but that
reference is still unimplemented — building Z-Wave support would be the first real forcing function to
actually implement `.ksy` decoding rather than leave it purely speculative in the design docs.

## Scope

Z-Wave is a mesh network protocol: one controller (the stick/HAT) manages potentially many paired
nodes, each exposing its own device class and command classes (Binary Switch, Multilevel Sensor,
Multilevel Switch, and dozens more in the full Z-Wave specification). A complete controller
implementation — inclusion/exclusion, mesh routing, OTA firmware updates — is a large undertaking, well
beyond what any device module built so far has attempted.

Recommended initial scope, matching how every other device module in this project started with one
device's core commands rather than everything at once (K8055's first pass covered digital/analog I/O
and counter reset, leaving several open questions for later; Busylight shipped color/blink/sound and
dropped a confirmed-no-op batch-program format rather than keep chasing it):

- Controller frame encode/decode (SOF/ACK/NAK/CAN handshake, the basic request/response frame shape)
  via a `.ksy` definition.
- Basic control of a single already-paired node's common command classes — Binary Switch, Basic
  Get/Set, Multilevel Switch — as a `UiDefinition`/`IControlSurface` control panel, the same pattern
  K8055/Busylight already use.
- Explicitly **out of scope for a first pass**: node inclusion/exclusion (pairing), mesh
  routing/repair, security (S0/S2) key exchange, OTA firmware. These are real, substantial pieces of
  the Z-Wave spec on their own and shouldn't block shipping basic node control.

## Open questions

- Whether a paired node's command-class set needs to be user-declared (a manifest listing what
  command classes the one target node supports) or whether dev-term should attempt to query it from
  the controller — the latter is more capable but is real protocol work beyond basic frame decode.
- How the `.ksy`-decoding runtime actually gets wired into a device module once built — this proposal
  would be its first real consumer, so the integration shape (a decoder wrapping a Kaitai-generated
  parser, presumably) isn't proven yet the way the text-schema SCPI path is.

## Completion checklist

What is needed before this proposal can be closed. Tick items as they land, in the same change.

- [ ] Acquire a Z-Wave controller (Z-Stick or RPi HAT)
- [ ] Capture real frames to ground the protocol work
- [ ] Transport and codec with unit tests
- [ ] Both front ends, plus `docs/specs/` and `docs/user-guide/` entries
- [ ] Real-hardware pass (`docs/test/`)

## Status

**Not started — design only.** No Z-Wave hardware (a Z-Stick or an RPi HAT) is confirmed on hand yet.
Needs one before real implementation, per this project's consistent real-hardware-grounding
convention (see the BYTECC and LXI proposals for the same caution) — the protocol work described above
is speculative until there's a real controller to capture and validate frames against.
