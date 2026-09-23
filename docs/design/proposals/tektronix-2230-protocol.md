# Proposal: Tektronix 2230 — Pre-SCPI "Codes" Protocol

## Status: proposal only — not implemented

Explicitly out of scope for the SCPI instrument control work
([scpi-instrument-control.md](scpi-instrument-control.md)): the 2230 predates SCPI and does not
speak it, so none of that module's `ScpiInstrumentProfile`/`ScpiControlSurface`/`ScpiReplyPresenter`
machinery applies here without a different, non-SCPI decoder underneath it. This doc exists to
capture what's already known and scope the follow-up work, per `BACKLOG.md`'s note ("worth its own
proposal doc when picked up") — not to guess a full command set without more real-hardware probing.

## Device

Two Tektronix 2230 digital storage oscilloscopes are confirmed real, owned test devices (see
[scpi-instrument-control.md](scpi-instrument-control.md)'s "Target hardware" table: "the
already-verified Tektronix 2230 (×2) remains the one confirmed real oscilloscope target"). Era:
1980s DSO, GPIB/RS-232 remote interface, predates the SCPI-99 standard entirely — it uses Tektronix's
own pre-SCPI "codes" command language (sometimes called "Tek codes" informally), not IEEE 488.2
common commands or any SCPI-style hierarchical `:SUBSYSTEM:COMMAND` syntax.

## What's known

The one confirmed real exchange, from this project's own prior real-hardware use (per `BACKLOG.md`):

```
> ID?
< ID TEK/2230,V81.1,VERS:14;
```

This alone tells us:

- Commands are plain ASCII, queried with a trailing `?`, matching the general shape of "textual
  query/response bench instrument" that makes dev-term's existing serial/TCP transports and
  line-buffered decoding approach usable — no new transport needed here, same as the SCPI proposal's
  own reasoning.
- Replies echo the command name (`ID?` → `ID ...`) rather than just returning a bare value — a
  different reply convention than SCPI's typical bare-value response to `*IDN?`. A generic "next
  line is the answer" correlator (like `ScpiReplyPresenter`'s FIFO id-tracking) would likely still
  work mechanically, but the *parsing* of that reply (splitting `ID TEK/2230,V81.1,VERS:14;` into
  make/model/firmware/version fields) needs Tektronix-specific field knowledge, not the generic
  `*IDN?`-comma-split convention SCPI instruments share.
- The reply is `;`-terminated, not newline-terminated the way the SCPI profiles' `Terminator`
  convention assumes on the *inbound* side (SCPI profiles only declare an outbound terminator).
  Confirming whether a trailing newline still follows the `;` (needed for
  `ScpiReplyPresenter`-style line-buffering to work unmodified, or whether this protocol needs its
  own buffering-until-`;` decoder) is real-hardware work, not something to assume from one example.

Beyond `ID?`, the actual command set (channel/scale/trigger/measurement queries, if any are
remotely accessible on this model at all — many period scopes of this class are GPIB-only for
*acquisition* readback, with the front panel remaining the only way to change most settings) is
**unknown** and not guessed here.

## Why this isn't folded into the SCPI module

- Different command syntax entirely (no `:SUBSYSTEM:COMMAND` hierarchy, no `*`-prefixed IEEE 488.2
  common commands) — `ScpiControlSurface`'s `{Name}`-token template substitution and
  `ScpiProfileCatalog`'s `IdnPattern`-based auto-detect both assume a `*IDN?`-shaped bootstrap query
  this device doesn't have.
- Different reply framing (`;`-terminated, command-name-echoing) than the newline-terminated,
  bare-value SCPI convention `ScpiReplyPresenter` was built around.
- Reusing `DevTerm.UiDefinitions`/`IControlSurface`/the generic `ControlPanelMode`/`ControlPanelWindow`
  renderers is still very plausible once a real command set and reply grammar are known — the
  renderer itself is protocol-agnostic (proven across K8055, Busylight, and SCPI with zero renderer
  changes). What's missing is a `TektronixCodesControlSurface`/`TektronixCodesReplyPresenter` pair
  analogous to the SCPI ones, not a UI-layer change.

## Proposed next steps (not started)

1. Connect to a real 2230 (RS-232 or GPIB-via-Prologix, once a GPIB transport exists — see
   `BACKLOG.md`'s GPIB transport entry) and send `ID?` to confirm the exact byte-for-byte framing
   (trailing newline? `\r`? just `;`?) — this determines whether `ScpiReplyPresenter`'s line-buffer
   can be reused as-is or needs its own `;`-based variant.
2. Probe a small set of likely-supported queries from period Tektronix documentation (channel
   scale/position, trigger source/level, a measurement readback) against the real device, recording
   exact command/reply pairs the same way `ID?` was already confirmed — no command should be added
   to a profile without a real confirmed reply, consistent with this repo's stated practice of
   flagging unverified curated command sets explicitly.
3. Once a handful of confirmed commands exist, build `DevTerm.Devices.TektronixCodes` mirroring
   `DevTerm.Devices.Scpi`'s shape (a profile/control-surface/reply-presenter trio) rather than
   forcing it through the SCPI-specific types — a shared *pattern*, not shared *code*, matching this
   repo's existing precedent of independent per-protocol decoders (K8055/Busylight/SCPI don't share
   decoder code with each other either).

## Open questions

- Whether the 2230's remote interface (as actually fitted/configured on the two owned units) is
  RS-232, GPIB, or both — affects whether any real testing can happen before a GPIB transport
  exists.
- Whether this model supports *any* remote configuration (vs. read-only status/measurement query)
  — many instruments of this era and class only expose acquisition data remotely, leaving all
  front-panel settings physical-only, which would make an `IControlSurface` here mostly a read-only
  querying tool rather than a control panel in the K8055/Busylight/SCPI sense.
- Whether Tektronix ever published a full command reference for the 2230 specifically (vs. a family
  reference covering several period scope models loosely) — determines how much of the command set
  can be sourced from documentation vs. needing exhaustive real-hardware probing.
