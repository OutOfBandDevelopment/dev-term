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

- Whether the DG1022/DS1102E specifically (not yet tested — the four devices checked so far are a
  different set of Rigol instruments) also need a Zadig driver swap, or happen to enumerate
  differently — unconfirmed until checked against those exact two units.
- Whether to automate/document the Zadig driver-swap step for end users (a real per-machine setup
  burden this transport now provably has, per "Real-hardware findings" above) or treat it as a
  documented prerequisite in the user guide once this transport actually ships.
- Whether USB488's `READ_STATUS_BYTE`/service-request handling is worth building in v1, or whether a
  simple synchronous send/query cycle (no serial-poll, no SRQ) is enough for how these instruments
  are actually used from dev-term today (matching how GPIB-via-Prologix's own v1 scope in
  `docs/design/transports.md` also skips SRQ handling).
- Whether this transport can share any code with a possible future GPIB-via-Prologix controller
  layer (both ultimately drive SCPI-speaking bench instruments) — likely not much, since GPIB's
  framing (Prologix's `++` ASCII layer over serial/TCP) and USBTMC's (binary headers over raw USB
  bulk transfers) don't overlap; probably two independent implementations sharing only the SCPI
  profile data above them, the same way Serial/TCP/HID share no decoder code with each other today.
