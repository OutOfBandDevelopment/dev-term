# Proposal: Tektronix 2230 — Pre-SCPI "Codes" Protocol

## Status: minimal profile added (2026-09-23), reusing `DevTerm.Devices.Scpi` as-is

Revised from the original "out of scope, needs its own decoder" framing below: `ScpiControlSurface`'s
`{Name}`-token template substitution has no actual dependency on SCPI's `*`-prefixed common commands
or `:SUBSYSTEM:COMMAND` hierarchy — a `Template` is just an arbitrary string with `{Name}` tokens, so
a plain `ID?` command works exactly like any other profile's command. There was therefore no need to
build a separate `TektronixCodesControlSurface`/`TektronixCodesReplyPresenter` pair just to send one
confirmed command; the plumbing described in "Why this isn't folded into the SCPI module" below still
holds for anything *beyond* the one confirmed exchange (see the caveats immediately below), so this
stays a proposal doc rather than a finished module.

`src/DevTerm.Devices.Scpi/Profiles/tektronix-2230.json` was added:

```json
{
  "Name": "Tektronix 2230 (pre-SCPI \"codes\" protocol - NOT IEEE-488.2/SCPI compliant; see docs/design/features/tektronix-2230-protocol.md)",
  "Terminator": "\n",
  "Commands": [
    { "Id": "id", "Label": "Identify", "Category": "Common", "Template": "ID?", "IsQuery": true }
  ]
}
```

Only the one confirmed command (`ID?`) is included, consistent with this repo's practice of never
curating a command without a confirmed real reply. The name field itself carries the
non-compliance warning (shown wherever a profile name is displayed — the picker list, the panel
title) since there's no separate "compliance" field in `ScpiInstrumentProfile`. Everything else
about this device is still reached through the always-present "Custom Command" section every SCPI
profile gets, same as any other unconfirmed command on any other profile.

**Caveats carried over from reusing the SCPI plumbing as-is, not fully resolved:**

- **No `IdnPattern`, so no auto-detect.** The existing auto-detect flow
  (`MainWindow.DetectScpiProfileAsync`/`TuiMode`'s equivalent) hardcodes sending `*IDN?`, which this
  device doesn't understand — the 2230 must always be picked explicitly from the profile list
  ("Tektronix 2230 (pre-SCPI...)"), never via "Auto-detect (\*IDN?)". Giving it an `IdnPattern` would
  be actively misleading (it would never match, since auto-detect never sends `ID?`), so it's left
  unset — the same convention `ScpiProfileCatalog.Generic` already uses for "manual selection only".
- **Reply framing is still unconfirmed.** `ScpiReplyPresenter` line-buffers on `\n`; the one known
  reply (`ID TEK/2230,V81.1,VERS:14;`) is `;`-terminated, and whether a trailing newline actually
  follows the `;` on the wire has not been checked against real hardware. If it doesn't, the `id`
  command's `IndicatorControl` simply won't update (the FIFO queue stays pending) even though the
  raw bytes did arrive — the same failure shape as a presenter not being wired into the pipeline at
  all, so don't assume this profile's indicator working is proof of anything beyond `ID?` specifically
  until checked live.
- **`Terminator: "\n"` on the outbound side is a guess**, not confirmed — the known exchange doesn't
  establish what, if anything, needs to follow `ID?` when sent.

The original scoping below (full command enumeration, `;`-based reply framing work) remains
accurate for going beyond this one command.

---

## Original scoping (superseded above for the one-command case)

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

## Why this isn't (fully) folded into the SCPI module

Superseded in part by the "Status" section above: a single `ID?` command needed nothing beyond
`ScpiControlSurface`'s plain template substitution, so it *is* just a `DevTerm.Devices.Scpi` JSON
profile today, not a bespoke plugin. What still doesn't fold in cleanly, if/once more commands are
confirmed:

- **Auto-detect doesn't generalize.** `ScpiProfileCatalog`'s `IdnPattern`-based matching assumes a
  `*IDN?`-shaped bootstrap query this device doesn't have — fine for one manually-picked profile,
  but there's no `*IDN?`-equivalent to hang a real "detect this exact instrument automatically" flow
  on within the existing auto-detect entry point without special-casing it there.
- **Reply framing (`;`-terminated, command-name-echoing) doesn't match `ScpiReplyPresenter`'s
  newline-buffered, bare-value assumption.** Untested whether it happens to work anyway (see the
  caveat above); if it turns out not to, correlated replies for this device need their own decoder,
  not a `ScpiReplyPresenter` change (which is built around real SCPI's actual convention, not a
  guess at Tek's).
- Reusing `DevTerm.UiDefinitions`/`IControlSurface`/the generic `ControlPanelMode`/`ControlPanelWindow`
  renderers is still proven and unaffected either way — the renderer itself is protocol-agnostic. A
  dedicated `TektronixCodesControlSurface`/`TektronixCodesReplyPresenter` pair (mirroring
  `DevTerm.Devices.Scpi`'s shape) only becomes worth building if real-hardware probing turns up
  either a large confirmed command set or reply framing `ScpiReplyPresenter` genuinely can't handle
  — not before either is known.

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
3. Once a handful of confirmed commands exist, extend `Profiles/tektronix-2230.json` with them as
   long as `ScpiReplyPresenter`'s newline-buffered FIFO correlation is confirmed to actually work for
   this device's replies (step 1). If it doesn't, build a separate `DevTerm.Devices.TektronixCodes`
   mirroring `DevTerm.Devices.Scpi`'s shape (a profile/control-surface/reply-presenter trio) instead
   of forcing a `;`-terminated protocol through `ScpiReplyPresenter` — a shared *pattern*, not shared
   *code*, matching this repo's existing precedent of independent per-protocol decoders (K8055/
   Busylight/SCPI don't share decoder code with each other either).

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
