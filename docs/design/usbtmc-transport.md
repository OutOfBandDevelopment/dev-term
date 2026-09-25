# USBTMC transport

## Purpose

Describes what's needed to add a `DevTerm.Transports.Usbtmc` transport for USB Test & Measurement
Class devices — the USB class most local-USB bench equipment (Rigol, Keysight, and others) actually
uses. Not yet built; this doc replaces the placeholder paragraph in
[transports.md](transports.md)'s Extensibility section with an actual design. Real target hardware:
the plain **Rigol DG1022** (no LAN option, unlike the DG1022Z/DG1062Z) and the **Rigol DS1102E**
oscilloscope (confirmed to have USB, likely USBTMC for this era of Rigol scope but not yet confirmed
for this specific unit).

## Why this needs its own transport

USBTMC is neither a virtual COM port nor a HID report interface — it's its own USB device class
(`bInterfaceClass 0xFE`, `bInterfaceSubClass 0x03`) with dedicated bulk endpoints and its own message
framing on top of them. Windows doesn't expose it as `COMx`, and `HidSharp`'s generic HID class
driver doesn't apply either (USBTMC devices generally don't even present a HID interface). Both of
dev-term's existing USB-ish transports (`DevTerm.Transports.Serial`, `DevTerm.Transports.Hid`) are
therefore the wrong shape — this needs raw USB access.

## Considered and rejected: IVI/VISA (IVI.NET)

The IVI Foundation's driver architectures (IVI-C, IVI-COM, IVI.NET) plus a vendor VISA runtime
(NI-VISA, Keysight IO Libraries Suite, R&S VISA, ...) are the standard way commercial test software
talks to this hardware, so it's worth ruling out explicitly rather than silently:

- **IVI.NET and IVI-COM are built on .NET Framework and COM**, not .NET Core/.NET 5+. Instrument
  driver DLLs following either spec are typically compiled against .NET Framework and registered as
  COM servers; they aren't a reliable target for a `net10.0` process.
- **VISA itself is a separate, proprietary, machine-wide native runtime** the user has to install
  from the instrument vendor — a fundamentally different dependency model than every existing
  dev-term transport, none of which need a vendor driver install (`HidSharp` uses the OS's own
  generic HID class driver; `SerialPort` needs nothing beyond what Windows/Linux already ship).
  Some vendors do publish a thin **`Ivi.Visa`-style .NET Standard 2.0 NuGet wrapper** around the C
  VISA API, which *can* load under .NET Core/.NET 5+/.NET 10 as a library — but it still calls into
  that same separately-installed native VISA runtime underneath, so it doesn't remove the install
  dependency, only makes the managed calling convention nicer.
- Even if the runtime dependency were acceptable, VISA's own abstraction (resource strings, generic
  `viRead`/`viWrite`) sits at roughly the same level dev-term's own `ITransport` already does — it
  wouldn't reduce the actual protocol work below (framing, endpoint management), just relocate it
  behind a proprietary runtime.

**Conclusion**: build directly against raw USB, matching how `Serial`/`Hid` were built (own the
protocol, no vendor driver). This also keeps the door open to Linux (see below), which several VISA
implementations support unevenly at best.

## USB library choice

**Decided: LibUsbDotNet** (`LibUsbDotNet` NuGet package, v3.0.224 — v3.1.0 doesn't exist despite an
earlier assumption it might; 3.0.224 is nearest), over hand-rolled WinUSB P/Invoke, for the
cross-platform fit (`DevTerm.Transports.Ble`'s planned per-OS adapter seam, RFC 2217 being
OS-agnostic already set this precedent).

**Real wrinkle confirmed against actual hardware (2026-09-23, four Rigol USBTMC instruments — see
"Real-hardware findings" below)**: `LibUsbDotNet` is a *managed P/Invoke wrapper only* — it does not
bundle the native `libusb-1.0` shared library itself, and nothing on a stock Windows install
provides one (confirmed: a bare `dotnet run` failed with
`DllNotFoundException: Unable to load DLL 'libusb-1.0'`). The nearest thing on nuget.org that
actually bundles a prebuilt native Windows binary is **`libusb-dynamic`** (a vcpkg export, not
designed for .NET consumption — its own `build/native/*.props`/`.targets` target native C++
projects and no-op harmlessly here). `DevTerm.Transports.Usbtmc.csproj` references it with
`GeneratePathProperty="true"` purely so restore fetches a matching win-x64 build, then a plain
`<None Include="$(Pkglibusb-dynamic)\installed\x64-windows\bin\libusb-1.0.dll" CopyToOutputDirectory="PreserveNewest" Link="libusb-1.0.dll" />`
item copies the DLL into this project's own output — which, via the .NET SDK's standard
`ProjectReference` content-propagation behavior, flows through to `DevTerm.Console`'s output too,
with no changes needed there. This is Windows-only for now (`win-x64` specifically); Linux/macOS
would need the equivalent native `libusb-1.0`/`libusb.dylib` from the platform's own package manager
instead, not this package.

Confirmed, separately, that even with the native library present, **opening a device handle still
needs the OS driver actually bound to the interface to be WinUSB-compatible** — see "Real-hardware
findings" below. This was flagged as a real risk above (Zadig) before ever touching real hardware,
and turned out to matter immediately, not hypothetically.

## Protocol shape

USBTMC (USB Test and Measurement Class, USB-IF-published spec) layers message framing on top of
plain bulk transfers:

- **Bulk-OUT**: `DEV_DEP_MSG_OUT` — a 12-byte USBTMC header (`MsgID`, `bTag`/`bTagInverse` — a
  sequence number that must alternate and be acknowledged correctly, transfer size, an `EOM` bit
  marking "this is the last transfer of one logical message") followed by the actual SCPI/command
  bytes, padded to a 4-byte boundary.
- **Bulk-IN**: `DEV_DEP_MSG_IN` — a request-for-response frame sent bulk-OUT, then the device's
  reply arrives on bulk-IN, again framed with the same header shape; **one logical reply can span
  multiple bulk-IN transfers**, ending only when a transfer's `EOM` bit is set — the transport has
  to reassemble these into one message before handing bytes to `Session`/`Pipeline`, the same kind
  of framing responsibility already handled internally by `HidReadStream`'s report-to-channel
  reassembly.
- **Control endpoint requests**: `INITIATE_ABORT`/`CHECK_ABORT_BULK_OUT_STATUS` (abort a stuck
  write), `INITIATE_ABORT_BULK_IN`/`CHECK_ABORT_BULK_IN_STATUS` (abort a stuck read),
  `CLEAR_FEATURE` on the bulk endpoints (recover from a stall), and `GET_CAPABILITIES`.
- **USB488 subclass** (most SCPI-over-USBTMC instruments, including Rigol's, implement this
  extension on top of plain USBTMC): control-endpoint requests for `REN_CONTROL`/`GO_TO_LOCAL`/
  `LOCAL_LOCKOUT` (remote/local mode, mirroring what SCPI's `SYSTem:REMote` already had to do for
  the 34401A over RS-232 — see `docs/changes/2026-09-23.md`) and `READ_STATUS_BYTE` (serial-poll
  equivalent). Realistically needed for anything beyond the most trivial send/query cycle.

## Real-hardware findings (2026-09-23) — device discovery only, not the full transport

`DevTerm.Transports.Usbtmc` now has `IUsbtmcDeviceDiscovery`/`SystemUsbtmcDeviceDiscovery`
(`UsbContext.List()`, filtering each device's `Configs[*].Interfaces[*]` for
`Class == ClassCode.Application (0xFE) && SubClass == 0x03`), wired to a `--listusbtmcdevices true`
CLI flag in `DevTerm.Console` (mirroring `--listports`/`--listhiddevices`) — enumeration only, no
`ITransport` implementation yet.

Run against four real USBTMC-class Rigol instruments (VID `1AB1`) newly available on the bench:

- **Bare enumeration works with no driver changes at all**: `UsbContext.List()` correctly found all
  four devices and correctly identified each as USBTMC-class by interface class/subclass, purely
  from the standard device/config/interface descriptors — this doesn't require a WinUSB-compatible
  driver, just the native `libusb-1.0.dll` being present (see "USB library choice" above).
- **`IUsbDevice.Open()` fails on every device** with `UsbException: Operation not supported or
  unimplemented on this platform` (libusb's `LIBUSB_ERROR_NOT_SUPPORTED`) — confirmed by temporarily
  logging the caught exception, not assumed. This is exactly the driver-binding risk flagged
  speculatively above: these instruments are still bound to whatever Windows driver they enumerated
  with out of the box (not WinUSB), so libusb can list them but can't open a handle to them.
  `Info.Manufacturer`/`.Product`/`.SerialNumber` (which need a control transfer, hence an open
  handle) come back empty as a result — `--listusbtmcdevices true`'s output today is VID:PID pairs
  only, e.g. `1AB1:09C4  (unknown)`.
- **Consequence for the real transport, not just discovery**: nothing beyond bare enumeration is
  possible until each target device's USBTMC interface is rebound to a WinUSB-compatible driver via
  [Zadig](https://zadig.akeo.ie/) (or the OS already ships one bound to it, which it evidently
  doesn't for these four). This needs doing, and reverifying open/string-descriptor/control-transfer
  behavior afterward, before any `UsbtmcTransport : ITransport` work below can be verified against
  real hardware — it's a per-machine setup step, not a code fix.
- **Confirmed fixed after the Zadig/WinUSB rebind** (see "Setting up WinUSB" below): re-running
  `--listusbtmcdevices true` against the same four devices after rebinding them to WinUSB now
  resolves full string descriptors with no code changes — `Open()` succeeds and
  `Info.Manufacturer`/`.Product`/`.SerialNumber` all populate correctly, e.g.
  `1AB1:09C4  Rigol Technologies DM3000 SERIES   SN:DM3R232301438`. This closes the last open
  question about whether the driver swap alone is sufficient (it is) and confirms `LibUsbDotNet`
  itself needs no further fixing — the remaining work is entirely the `UsbtmcTransport` build below.

## Setting up WinUSB for a USBTMC device (Windows)

Confirmed (see "Real-hardware findings" above): opening any of these devices needs their USBTMC
interface rebound from whatever Windows assigned by default to a WinUSB-compatible driver first.
This is a one-time, per-device, per-machine setup step, done with
**[Zadig](https://zadig.akeo.ie/)** — the standard tool for this, also the reference implementation
behind `libwdi` (the library `LibUsbDotNet`'s native `libusb-1.0` ultimately talks to on Windows).

**This step cannot be fully scripted/automated** — checked directly against the `libwdi`/Zadig
project (its GitHub releases and wiki), not assumed:

- Zadig's own releases only ever ship `zadig*.exe` (the GUI). `libwdi` also builds a command-line
  tool, `wdi-simple`, that *can* install a WinUSB driver non-interactively — but it has never been
  published as a prebuilt binary in any `libwdi` release; getting it means building `libwdi` from
  source (Visual Studio + WDK), which is a heavier ask than the manual GUI step it would replace.
- Zadig itself has no documented command-line flags for an unattended install (checked its wiki's
  "Zadig" page). It does support a `zadig.ini` (global defaults) and a separate, individually-loaded
  "preset device" file (`Device` menu → `Load Preset Device`, a small `[device]` INI block with
  `Description`/`VID`/`PID`) that can pre-fill the VID/PID so you don't hand-type hex, but the actual
  `Install Driver`/`Replace Driver` click — the one part that needs Administrator elevation and
  genuinely changes a real device's driver binding — still has to be a deliberate, manual click in
  the GUI. That's arguably correct, not just a gap: it's the same kind of confirmation Windows itself
  requires (UAC) for any driver change.

**What this repo provides instead**: [`scripts/usbtmc/New-ZadigPreset.ps1`](../../scripts/usbtmc/New-ZadigPreset.ps1)
generates a `zadig.ini` and one Zadig preset file per attached USBTMC device (by default, discovered
by shelling out to this project's own `--listusbtmcdevices true`; VID/PID can also be passed
directly). It writes files only — no elevation needed to run it, and it does not install anything
itself. Usage:

```powershell
./scripts/usbtmc/New-ZadigPreset.ps1
```

Then, manually:

1. Download Zadig and run it **as Administrator** (required for the driver install step).
2. Copy the generated `zadig.ini` next to `zadig.exe` (or launch Zadig from the same folder).
3. `Device` → `Load Preset Device` → pick one of the generated `zadig-preset-<VID>-<PID>.cfg` files.
4. Confirm the driver dropdown shows **WinUSB**, then click **Install Driver** (first time for that
   hardware ID) or **Replace Driver** (if something else is already bound).
5. Repeat steps 3-4 for each additional device — Zadig handles one device per run.

**Caveats carried over from "Real-hardware findings," restated here since they matter most at this
step**: this rebinds the device away from whatever it used before (a stock Windows driver, or a
vendor's own instrument-control software's driver, if installed) — if other software on the same
machine depends on that original driver, rebinding to WinUSB will break it for that other software
until reverted (Device Manager → the device → `Update driver` → roll back, or re-run Zadig against
the original driver). This is a real, not hypothetical, tradeoff for anyone who also runs vendor
instrument software (Rigol Ultra Sigma, Keysight BenchVue/Connection Expert, NI-VISA/MAX, etc.)
against the same physical unit.

## `ITransport` mapping

`UsbtmcTransport : ITransport` (`DevTerm.Transports.Usbtmc`):

- `OpenAsync`: enumerate/open the target device by VID/PID (mirroring `HidTransportOptions`' own
  VID/PID + optional serial-number matching), claim the USBTMC interface, do the USB488
  `REN_CONTROL` remote-enable handshake if the subclass is present.
- `WriteAsync`: split into one or more `DEV_DEP_MSG_OUT` bulk-OUT transfers (single-transfer for
  anything under the device's reported max transfer size), alternating `bTag`.
- Read path: request a reply via `DEV_DEP_MSG_IN`, pump bulk-IN transfers via `StreamToPipePump`
  (same pattern every other transport uses) until a transfer's `EOM` bit is set, then treat the
  reassembled payload as one chunk into the `PipeWriter` — this is the one place USBTMC's framing is
  genuinely different from a plain byte stream, so it can't just hand a raw `Stream` to the existing
  pump unmodified; a small adapter `Stream` (or a bespoke pump loop) that strips USBTMC headers and
  only surfaces payload bytes sits in between, analogous to how `HidReadStream` already isolates
  HID's own per-report framing from the rest of the pipeline.
- `CloseAsync`: USB488 `GO_TO_LOCAL` if applicable, release the interface, close the device handle.

## Cancellation/threading — check before assuming, per this codebase's own established pattern

`CLAUDE.md` already documents two prior cases (`SerialPort.BaseStream.ReadAsync`, HidSharp's
`HidStream`) where the obvious `ReadAsync(..., CancellationToken)` call didn't actually honor
cancellation on real hardware, and both were fixed with an owned background thread instead of a
`Task.Run`-wrapped blocking call. Whichever USB library is chosen here (WinUSB's async I/O via
overlapped `IOCP`, or `libusb`'s own async transfer API) needs the same real-hardware verification
before assuming its cancellation story is trustworthy — don't assume either is fine just because the
API surface looks like it should be.

## Testing strategy

Same principle as RFC 2217's plan (verify against an independent implementation) and this project's
own repeated practice of not trusting a fix until checked against real hardware:

- Unit-test the USBTMC header encode/decode (bTag sequencing, EOM detection, message reassembly
  across multiple bulk-IN transfers) as a pure codec, no real device — mirroring
  `Rfc2217Codec`'s planned shape.
- Real-hardware verification against the DG1022 and DS1102E once reachable: confirm device
  enumeration/driver binding actually works as assumed above (WinUSB or `libusb` binding, with or
  without a Zadig driver swap), confirm a `*IDN?` round-trip, and confirm multi-transfer reassembly
  actually triggers for a reply too large for one bulk-IN transfer (a scope's waveform/screen-dump
  command is a realistic way to force this, once the SCPI profile work for these instruments exists).

## Open questions

- **Resolved**: the Zadig/WinUSB rebind is sufficient by itself — no further `LibUsbDotNet`/native
  fix needed. Confirmed by re-running `--listusbtmcdevices true` after the rebind: all four devices'
  string descriptors resolved correctly (see "Real-hardware findings" above).
- Whether the DG1022/DS1102E specifically (not yet tested — the four devices checked so far are a
  different set of Rigol instruments) also need a Zadig driver swap, or happen to enumerate
  differently — unconfirmed until checked against those exact two units.
- **Resolved**: the Zadig driver-swap step is now documented, and partially automated (VID/PID
  preset generation, not the elevated install click itself — see "Setting up WinUSB" above) via
  `scripts/usbtmc/New-ZadigPreset.ps1`. Still open: whether this belongs in `docs/user-guide/` too
  once the full transport ships (today it's design-doc-only, appropriate while there's no connectable
  transport yet to document a user-facing flow for).
- Whether USB488's `READ_STATUS_BYTE`/service-request handling is worth building in v1, or whether a
  simple synchronous send/query cycle (no serial-poll, no SRQ) is enough for how these instruments
  are actually used from dev-term today (matching how GPIB-via-Prologix's own v1 scope in
  `docs/design/transports.md` also skips SRQ handling).
- Whether this transport can share any code with a possible future GPIB-via-Prologix controller
  layer (both ultimately drive SCPI-speaking bench instruments) — likely not much, since GPIB's
  framing (Prologix's `++` ASCII layer over serial/TCP) and USBTMC's (binary headers over raw USB
  bulk transfers) don't overlap; probably two independent implementations sharing only the SCPI
  profile data above them, the same way Serial/TCP/HID share no decoder code with each other today.
- **Resolved**: bulk-endpoint stall recovery (`CLEAR_FEATURE` mentioned above) is now implemented —
  `SystemUsbtmcDevice.Open()` calls `ClearHalt()` on both endpoints before first use, and
  `WriteBulkOut`/`ReadBulkIn` each clear-and-retry once on `Error.Pipe` — see
  `docs/changes/2026-09-23.md`'s real-hardware section for the DM3000 STALL this fixed.
- **Corrected (real-hardware, 2026-09-23)**: the bullet below originally concluded the DM3000's
  bulk-IN stall on a query reply was a firmware-level limitation. **That conclusion was wrong** —
  confirmed directly by the user: the same physical Rigol units (DM3000 and, separately, a DM3058E,
  which hits the identical stall via the real SCPI control-panel UI) work correctly under Rigol's own
  Ultra Sigma / NI-VISA driver stack. The firmware can clearly complete a `*IDN?` command/reply cycle
  correctly — the failure is specific to talking to the device over a generic WinUSB/libusb driver
  binding, not the device itself. Since the identical stall reproduces byte-for-byte from two
  independent libusb-based stacks (`LibUsbDotNet` and Python/pyusb) but not from NI-VISA, the real
  cause is most likely a difference in the low-level initialization/handshake/timing sequence NI-VISA's
  own driver performs before its first bulk transfer that a naive libusb
  open→claim-interface→bulk-transfer sequence doesn't replicate — not a `dev-term` code bug in the
  sense of wrong framing (framing was independently verified byte-correct), but not a dead end either.
  **Next concrete step** (needs hands-on access to the hardware, not further guessing): capture a
  working Ultra Sigma/NI-VISA `*IDN?` round-trip with Wireshark + USBPcap and diff the exact
  control/bulk transfer sequence against what `UsbtmcTransport`/the Python diagnostic send — this is
  the only way left to find the actual missing step rather than continuing to guess blindly.
  **Deprioritized (2026-09-23)**: the user opted to park this rather than chase further blind guesses
  or spend time on a packet capture right now — Ultra Sigma/NI-VISA remains the working path for these
  specific Rigol units in the meantime. Don't re-attempt already-ruled-out mitigations (stall recovery,
  `INITIATE_CLEAR`, full device reset, smaller `MaxTransferSize`, alternate terminator/`TermCharEnabled`
  variants — all tried, see below) without new evidence; a packet capture is the actual unblocking step
  whenever this gets picked back up.
  `UsbtmcTransport`'s query/reply path remains **unverified end-to-end against real hardware** despite
  the codec/framing/stall-recovery layers below it now being real-hardware-tested individually.
  Original (retracted) investigation notes, kept for the record:
  reading a query's reply from a real Rigol DM3000 consistently stalls the bulk-IN endpoint even with
  correct framing/tagging and after stall recovery, `INITIATE_CLEAR`, and a full device reset — the
  device's own Status Byte never sets MAV after receiving `*IDN?`, reproduced identically from a
  second, independent USB stack (Python/pyusb). See `docs/changes/2026-09-23.md` for the full
  investigation and this correction.
- **Resolved (real-hardware, 2026-09-25)**: the "unverified end-to-end" status and the deprioritized
  packet-capture plan above are both now out of date. The USBTMC protocol-conformance rework (see
  [`docs/design/features/usbtmc-protocol-conformance.md`](features/usbtmc-protocol-conformance.md))
  fixed the real bugs actually causing the DM3000/DM3058E-family symptoms — a naive libusb sequence
  never needed to replicate NI-VISA's handshake after all. DM3058E's stall was the bulk-IN
  header-re-decode bug (`usbtmc-bulk-in-reassembly-fix.md`); the DG1062Z's "2-byte first read" was a
  data-toggle desync from an unconditional open-time `ClearHalt` (fixed via a new `ClearHaltOnOpen`
  option, default off); the DG1022's failures were a per-device inter-request timing requirement
  (`UsbtmcDeviceQuirks`, see the DG1022 entry above). All four bench Rigols (DM3058E, DS1102E,
  DG1062Z, DG1022) now pass real-hardware query/reply round-trips reliably across repeated runs — see
  `docs/test/2026-09-25-18-03-06.md` and `docs/changes/2026-09-25.md`. No packet capture was needed.
- **New, resolved (real-hardware, 2026-09-23)**: `LibUsbDotNet.Device` (behind `IUsbDevice`) is a
  `SafeHandle`-backed `IDisposable` — every device object `UsbContext.List()` returns must be
  disposed once it's no longer needed, not just `Close()`d if it was opened, or its finalizer can run
  on the GC finalizer thread after the owning `UsbContext` was already disposed, unref'ing a device
  against an already-freed native context. This reproduced as a real `0xC0000005` access violation in
  `LibUsbDotNet.NativeMethods.UnrefDevice` during test-process teardown once real USBTMC hardware was
  attached to the dev machine (surfacing only then, since `SystemUsbtmcDeviceDiscovery`/
  `SystemUsbtmcDevice` are reachable from several UNIT-tagged WPF/TUI tests via
  `ConnectionEditorViewModel`'s default real discovery). Fixed by disposing every enumerated device
  that isn't kept, in both `SystemUsbtmcDeviceDiscovery.GetDevices()` and `SystemUsbtmcDevice.Open()`,
  plus disposing (not just closing) the kept device on `Close()`/on `Open()`'s exception path.
- **Code review of `SystemUsbtmcDevice`, applied** (queued 2026-09-23, applied 2026-09-24): all four
  findings below were fixed; only the packet capture above remains as an actual blocker for this
  transport.
  - **Real bug, fixed**: endpoint selection (`iface.Endpoints.First(...)`) filtered by direction
    (`EndpointAddress & 0x80`) only, not transfer type — a USBTMC interface can expose an optional
    Interrupt-IN endpoint (for USB488 SRQ) alongside Bulk-IN/-OUT, and one sorting before Bulk-IN in
    the descriptor would get picked as the "bulk in" reader instead. Fixed by also filtering
    `(e.Attributes & 0x03) == 0x02` (Bulk) on both lookups, and raising an explicit `IOException`
    instead of relying on `First()`'s generic `InvalidOperationException` if either lookup comes up
    empty.
  - ~~**Spec nit, fixed**: `REN_CONTROL`/`GO_TO_LOCAL`'s `wValue` is now always `0`~~ —
    **this "fix" was wrong and has been reverted (2026-09-25)**: USB488 Table 15 defines
    `REN_CONTROL`'s `wValue` as the REN state itself (1 = assert, 0 = de-assert); only
    `GO_TO_LOCAL`/`LOCAL_LOCKOUT` use 0. Sending 0 on open actively de-asserted REN. See
    [`features/usbtmc-protocol-conformance.md`](features/usbtmc-protocol-conformance.md).
  - **Minor, fixed**: `TryGetSerialNumber`'s catch now logs the swallowed exception via
    `Debug.WriteLine` before returning `null`, so an unexpected "every candidate rejected" is
    traceable instead of silent.
  - **Minor, fixed**: `Open()`'s failure-path catch (after `ClaimInterface` succeeds but
    before/during `OpenEndpointReader`/`Writer`) now explicitly calls
    `ReleaseInterface(_interfaceNumber)` before `Close()`/`Dispose()`, since whether those implicitly
    release the claim depends on the libusb backend.
  - Everything else in the review (stall recovery, short-write detection, timeout-as-empty-read) was
    assessed as correct and matching real USBTMC device behavior as already implemented.
- **Resolved (real-hardware, 2026-09-24, later same day)**: the query/reply path's "remains
  unverified end-to-end" status above (line ~278) is now out of date. A real bug in
  `UsbtmcTransport.ReadReply` — re-decoding a bulk-IN header on every continuation transfer of a
  multi-transfer reply instead of only the first, hanging forever on any reply long enough to span
  more than one physical transfer — turned out to be a second, independent cause of "communication
  locks up" reports, separate from the earlier DM3000/DM3058E MAV-never-sets mystery this section
  parked. Fixed, tested, and verified against real hardware: see
  [`docs/design/features/usbtmc-bulk-in-reassembly-fix.md`](features/usbtmc-bulk-in-reassembly-fix.md).
  DM3058E and DS1102E both now complete a full query/reply round-trip correctly end-to-end. DG1022
  remains genuinely stuck at the device/USB level in the current bench state — possibly the same
  family of issue as the parked DM3000/DM3058E MAV mystery above, possibly something else entirely —
  but now surfaces as a clean, reported error instead of a silent hang.
- **Resolved (real-hardware, 2026-09-25)**: the DG1022 "stuck at device/USB level" issue above was
  root-caused during the USBTMC protocol-conformance rework, not via `INITIATE_CLEAR`/
  `CHECK_CLEAR_STATUS` — a raw probe showed the device never answers a `REQUEST_DEV_DEP_MSG_IN` sent
  back-to-back with the query (reproducible across 3 runs); any gap of about 1 ms or more fixes it.
  `UsbtmcDeviceQuirks` now gives this VID:PID a 20 ms delay and also skips REN (the device times out
  `GO_TO_LOCAL`, and libsigrok blacklists remote/local for this PID too). After both fixes,
  `RealHardwareUsbtmcTests`'s DG1022 test passed 3 of 3 runs. See
  [`docs/design/features/usbtmc-protocol-conformance.md`](features/usbtmc-protocol-conformance.md)
  and `docs/test/2026-09-25-18-03-06.md`.
