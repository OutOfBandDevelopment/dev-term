# Transport Layer

## Purpose

Defines the `ITransport` contract and the initial set of transport plugins.

## Contract responsibilities

- **Discovery** — enumerate available endpoints where the platform supports it (e.g., list serial ports, list paired BLE devices, list HID devices).
- **Configuration** — transport-specific settings (baud/parity/stop bits for serial; host/port for TCP/UDP; VID/PID/usage page for HID; service/characteristic UUIDs for BLE), validated before open.
- **Lifecycle** — open, close, and connection-state notification (connected/disconnected/error), including unexpected disconnects (e.g., USB unplug).
- **I/O** — async byte-stream read and write. Transports that are inherently message-oriented (UDP datagrams, BLE characteristic notifications, HID reports) preserve message boundaries for presenters that care about them, rather than flattening everything into one undifferentiated byte stream.

## Initial transports

### Serial / UART

The most common case for embedded dev boards. Config: port, baud rate, data bits, parity, stop bits, flow control. Discovery via platform-native serial enumeration (`--listports` in the CLI).

**Implementation notes learned against real hardware** (a bench oscilloscope over RS-232), each a genuine requirement now baked into `DevTerm.Transports.Serial`, not just a design intention:

- **DTR/RTS must often be asserted explicitly.** `System.IO.Ports.SerialPort` defaults both `DtrEnable` and `RtsEnable` to `false`, unlike common terminal tools (and pyserial, which asserts DTR on open by default) — plenty of devices treat DTR as a "terminal is present" signal and simply never respond without it. dev-term defaults both to `true` (configurable) specifically to avoid a silently-connected-but-unresponsive device being the default experience.
- **`SerialPort.BaseStream.ReadAsync(Memory<byte>, CancellationToken)` does not reliably honor cancellation on an in-flight read**, at least on the driver tested. Relying on it meant Close/Ctrl+C could hang indefinitely waiting for a read that was never going to notice it was canceled. The transport instead reads via the `SerialPort.DataReceived` event: a pending read is a plain `TaskCompletionSource` that the caller's `CancellationToken` can complete directly, and the actual (fast, non-blocking) `Read` only happens once data is already known to be buffered. No polling, no background work item that can outlive whoever's waiting on it.
- **Writes need a bounded timeout.** With hardware (RTS/CTS) flow control on, a device that never asserts CTS makes `Write` block forever with the default infinite `WriteTimeout`. A finite default (`WriteTimeoutMs`, 5s) turns a silent hang into a catchable `TimeoutException` with an actionable CLI message instead.

### Write pacing (inter-byte delay)

`WriteDelayStream` (`DevTerm.Core.Transports`) is a write-only `Stream` decorator for slow devices
without FIFO buffers that can't absorb a burst write — a full-line/full-packet write arrives faster
than the device can consume it, and bytes get dropped or corrupted on the receiving end. It wraps
whatever stream a transport's write path already uses (the same "wrap a transport's stream for one
orthogonal concern" shape as `XonXoffReadStream`/`Rfc2217TelnetReadStream`, but on the write side),
writing one byte at a time with an explicit `FlushAsync` after each one, and delaying the configured
interval between bytes — never after the last byte, so a paced write doesn't add trailing latency
with nothing left to send.

The delay is a single `int`, `WriteByteDelayMs`, with a three-way sentinel meaning (not just
positive/zero): **negative** (the default, `-1`) disables pacing entirely — the transport writes
unpaced, exactly as it did before this feature existed, with no per-byte flush overhead; **zero**
paces with no delay — still one write-and-flush per byte (useful for a device that needs the flush
boundary itself, e.g. one that reads a byte at a time off the wire, without actually needing time
between them); **positive** adds a real `Task.Delay` of that many milliseconds between bytes. This
is write-only and has no effect on how a transport reads.

Supported on **Serial, TCP, and RFC 2217** (`SerialTransportOptions`/`TcpTransportOptions`/
`Rfc2217TransportOptions.WriteByteDelayMs`) — the transports whose write path is already a single
stream that `WriteDelayStream` can wrap. Not offered for HID or USBTMC: both write one atomic,
device-framed report/message per call, where splitting it into single-byte writes would break
framing rather than pace it; not offered for BLE or Loopback either, for the same "not a byte-stream
write path" reason. `ConnectionEditorViewModel.SupportsWriteByteDelay` gates the Connection Editor's
"Timing" form section to exactly these three transports — see
[`docs/specs/connection-editor.md`](../specs/connection-editor.md).

### TCP

Supports both directions from day one, since either the device or dev-term may be the one that connects:

- **Client mode** — connect out to a host:port (the common case: a device exposing a TCP server).
- **Listener mode** — bind and accept incoming connections on a local port, for devices that connect out to dev-term (e.g., a board acting as a TCP client). A listener session represents "waiting for a peer"; `OpenAsync` doesn't complete until a peer connects, and the session then behaves like any other connected transport for read/write. **v1 policy**: one peer at a time — the listening socket stops accepting as soon as one connection is accepted, so a second simultaneous inbound connection is simply not accepted until the session is closed and reopened. Queuing/multiplexing multiple concurrent peers on one listener session is not supported yet (see open questions).

Which mode a given session uses is a configuration choice (see the Options pattern in [platform.md](platform.md)), not two different transport plugins.

**Software (XON/XOFF) flow control** (`TcpTransportOptions.SoftwareFlowControl`, landed
2026-10-01): off by default — stripping 0x11/0x13 from a stream that isn't actually using them for
flow control would silently eat legitimate device data. Some serial-to-Ethernet bridges forward the
attached serial port's XON/XOFF bytes over the wire rather than honoring them locally; enabling this
strips those bytes from the decoded output (`XonXoffReadStream`) and pauses/resumes `TcpTransport`'s
own writes on them (`XonXoffFlowControlGate`) — the software equivalent of the hardware RTS/CTS
handshake serial already honors. The options flag only sets the *initial* value for a new
`TcpTransport`; `TcpTransport.SoftwareFlowControl` is a live, settable property that toggles the gate
without reconnecting, since a bridge's actual behavior often can't be confirmed until a connection is
already open. Disabling it releases any write currently paused on a stale XOFF rather than leaving it
stuck forever. Both front ends expose this as a View > Software Flow Control (XON/XOFF) menu item,
enabled only when the active tab's transport is `TcpTransport` — see
[`docs/specs/tui-main-screen.md`](../specs/tui-main-screen.md) and
[`docs/specs/wpf-main-window.md`](../specs/wpf-main-window.md).

### UDP

Datagram-oriented; no connection lifecycle in the traditional sense, but still modeled as open/close for consistency with other transports. Also supports both directions:

- **Target mode** — send datagrams to a fixed host:port (and receive any replies from that same peer).
- **Listener mode** — bind a local port to receive datagrams from any sender, useful for devices that push telemetry/broadcast data unprompted. A bound listener can still send (e.g., reply to whichever peer last sent data, or to a separately configured target), since UDP has no inherent client/server asymmetry.

Because UDP preserves datagram boundaries, each received datagram is delivered to the presenter pipeline as one discrete message rather than merged into an undifferentiated byte stream (see [architecture.md](architecture.md)).

### USB HID

Report-based I/O against a specific VID/PID (and usage page/usage where needed) — useful for devices that expose a HID interface for control/debug rather than a serial port. Direction: [HidSharp](https://www.nuget.org/packages/HidSharp) — cross-platform (Windows/Linux/macOS), no vendor driver install required since HID already has a generic OS-level class driver everywhere (unlike raw USB device classes, which is exactly why HID was ruled out for USBTMC-class instruments — see the note under "Extensibility" below). Not a fit for report-based protocols alone; a device that turns out to actually be a different USB class entirely (as [Radex One](features/radex-one-protocol.md) initially seemed but wasn't confirmed to be until checked directly) needs re-verifying before assuming this transport applies.

### BLE

Connect to a peripheral by address/name, then read/write/subscribe to specific GATT characteristics.

**Cross-platform by design, via a pluggable per-OS adapter seam** — rather than picking one platform's native BLE API and making the whole transport (and everything that references it) platform-locked, `DevTerm.Transports.Ble` defines its own small internal adapter contract (peripheral discovery, GATT service/characteristic read/write/subscribe) that the transport itself talks to, with OS-specific implementations behind it:

- **Windows** — `Windows.Devices.Bluetooth` (WinRT GATT client), the most mature/documented option, built first.
- **Linux** — BlueZ over D-Bus.
- **macOS** — CoreBluetooth.

Windows is the first backend built (2026-09-25): `DevTerm.Transports.Ble.Windows`, a
`Windows.Devices.Bluetooth`-backed `IBleAdapter`/`IBleAdapterFactory`/`IBleDeviceDiscovery`, loaded
into DI at runtime by `DevTerm.Configuration.BlePlatformAdapterLoader` (reflection, not a
project reference — see that class's own doc comment for why: a per-OS backend needs a
Windows-versioned TFM, which a plain `net10.0` front end can't take a compile-time
`ProjectReference` on). `DevTerm.Transports.Ble` itself (the contract + cross-platform transport)
stays a plain `net10.0` library with a default "unsupported platform" registration, so selecting
`ble` on Linux/macOS today fails clearly at connect time rather than crashing at startup. Linux/
macOS backends remain not-yet-built (see below).

Every front end can select `ble` as a transport (`--transport ble --bledeviceid <id>` on the CLI,
the TUI Configure screen's transport selector, and WPF's Device Profiles window), configuring the
device id plus the three GATT UUIDs (service/write/notify — blank uses the Nordic UART Service
defaults below), and `--listbledevices true` lists nearby peripherals from a live ~4s advertisement
scan (`BluetoothLEAdvertisementWatcher`), not Windows' paired-devices list — pairing isn't required
to connect, and plenty of cheap BLE UART clones use "Just Works" pairing that Windows' Settings page
can show as "Paired" without a real LE bond ever completing, which left them unlistable under the
prior paired-only selector (see `WindowsBleDeviceDiscovery`'s own doc comment).

Both the TUI and WPF have a live "Detect..." device picker for BLE now, the same idea as HID/USBTMC's
(`ConnectionEditorViewModel.SelectedBleDevice`/`BleDeviceOptions`, populated by a front end calling
`SetBleDeviceOptions` after its own `BleDeviceScanner.Scan()`) — unlike HID/USBTMC's fast, eager
enumeration, a BLE scan is slow (~4s) and only available via the runtime-loaded Windows backend, so
it can't run eagerly from the shared view model's constructor and instead runs on demand: the TUI's
"Detect BLE..." button blocks for the scan's duration like its other pickers do, and WPF's runs the
scan on a background thread (`Task.Run`) so the window stays responsive. The device id field stays
directly typable either way.

None of the non-Windows backends ship yet; the adapter seam exists so each can land independently
(including as a community/self-contributed adapter) without touching the transport's public shape
or forcing every front end onto one platform's API. This is a real, non-trivial commitment — BLE
stack differences are still likely the hardest part of this transport to get uniform — but it's a
deliberate choice over the easier Windows-only path, given for the console app (unlike WPF)
cross-platform reach already matters.

**BLE Serial** is the common special case worth naming explicitly: many hobbyist/embedded BLE devices don't expose a bespoke GATT profile at all — they emulate a UART over two characteristics (one for host→device writes, one for device→host notifications), most commonly following the de facto [Nordic UART Service](https://developer.nordicsemi.com/nRF_Connect_SDK/doc/latest/nrfxlib/nrf_ble/doc/service.html) UUIDs (`6E400001-B5A3-F393-E0A9-E50E24DCCA9E` service, `...002` RX/write, `...003` TX/notify). `BleTransportOptions` defaults its three UUIDs to these — a BLE Serial transport mode stays configurable per device, since not every device that "acts like serial over BLE" actually uses NUS — worth confirming per device (e.g., via a BLE scanner app) before assuming the default applies, same caution as every other vendor-protocol-claim in this project.

**Known limitation — HC-08/SH-HC-08-family BLE-to-serial bridges may refuse to reconnect until reset.**
Verified against a real SH-HC-08 bridge module 2026-09-29 (`docs/test/2026-09-29-17-12-20.md`): the
first connection each power-on works fine (including a full BLE-to-serial byte round-trip), but a
reconnect after a clean disconnect can fail every time with a generic "operation was canceled"
cancellation, unless the peripheral's TTL-serial side is reset first. Root-caused to the peripheral,
not dev-term: `WindowsBleAdapter.Cleanup()` already disposes/unsubscribes cleanly on every close, and
the module's own manufacturer manual (HC-08 V3.1 User Manual, hc01.com) rules out both AT-tunable
candidates checked directly against the hardware — `AT+MODE?` reported the module already in
full-power/always-advertising mode (not a power-saving mode that would need a wake sequence), and
`AT+CTOUT` isn't implemented by this module's firmware (`AT+VERSION` identifies it as `SH-V1.251`, an
"SH"-vendor fork, not stock hc01.com firmware) — no response, unlike `AT+MODE?` over the same link.
The manual also confirms AT command mode only works while the module has *no* active BLE connection
at all ("Connection after entering serial transparent transmission mode"); there is no escape
sequence back into AT mode while connected, so any further AT-based workaround (e.g. issuing
`AT+RESET` as a software-triggered equivalent of the physical reset) has to happen in the gap between
a disconnect and the next reconnect attempt, never while something is connected. Treated as an
inherent limitation of this device class rather than a dev-term defect — no code change planned
unless a software-issuable fix is found.

### Loopback

A zero-configuration, in-process fake device — `DevTerm.Transports.Loopback` — for exercising the
UI (TUI Configure screen, WPF Device Profiles/Connection Editor, either front end's send/receive
flow) without any real hardware attached. It implements `ITransport` over an in-memory `Pipe`: no
real `Stream`, no background pump, and none of the cancellation hazards documented for
serial/HID above, since there's no external I/O to cancel.

It runs a small fixed script of rules against each typed command and pushes each response line back
as its own separate write (so line-buffering presenters like the ASCII presenter see distinct
lines/events), with an unmatched command producing a visible `? Unrecognized: ...` marker rather
than silence. Matching is case-insensitive (`HELLO`, `Hello`, and `hello` all match):

- `hello` → `From Loopback test`
- `Send Stream: N, ascii` → a deterministic N-character ASCII run
- `Send Events: N` → N separate lines, `Event 1` through `Event N`
- `MEAS?` → the next simulated sensor sample (`A=50.00 B=90.00 C=0.00 X=0.80 Y=0.00 Z=0.00 R=0.50 T=0.00 H=0.00` first); `Samples: N` → the next N, one per line. The sample counter is per connection, so the sequence is deterministic from connect (`LoopbackGenerators.SensorSample(index)`); the bundled "Loopback Sensor Demo" [device manifest](device-manifests.md) charts it
- `help` or `?` → the command list above (`LoopbackScript.HelpLines`)

The script is fixed today — no user-configurable custom script via the profile editor — and the
transport takes an `IOptions<LoopbackTransportOptions>` (currently empty) purely as the natural
extension point for that later. Selecting `loopback` as the transport needs no other fields in
either front end, so both pickers show only an informational label/panel in its place.

This is a distinct, real production transport, not a repurposed version of the internal
`LoopbackTransport` test helper in `tests/DevTerm.Console.Tests/` (documented in
[testing.md](testing.md)) — that one stays a test-only prototype used to drive `Session`/`TuiMode`/
`MainWindow` in automated tests; this one is registered through the same `AddDevTermFrontEnd` DI path
as every other transport and is selectable by an actual end user.

## Extensibility

New transports (CAN bus, SPI/I2C bridge adapters, raw sockets, SSH, named pipes, etc.) implement the same `ITransport` contract and are picked up via the plugin host — no core changes required. RFC 2217 (a remote-controllable serial port over Telnet) is a concrete planned one — see [rfc2217.md](rfc2217.md), including a real-world caveat about vendor-specific variants that don't actually match the IETF standard despite the name.

**USBTMC** (the USB device class most bench test equipment actually uses for local USB control, e.g. Rigol/Keysight instruments) is deliberately *not* in the initial transport list — it's neither HID nor a virtual COM port, but its own USB class with dedicated bulk endpoints and message framing, so it needs its own transport built against raw USB (WinUSB/LibUsbDotNet) rather than reusing HID or Serial, or an IVI/VISA driver (ruled out — Windows/.NET-Framework-oriented and a separate proprietary runtime install). See [usbtmc-transport.md](usbtmc-transport.md) for the design (protocol framing, library choice, why IVI.NET/VISA was rejected) and the [SCPI proposal](features/scpi-instrument-control.md) for target hardware; not yet built.

**GPIB** is reachable without a new `ITransport` at all, for the common case: most inexpensive "IEEE-488 to USB/Ethernet" adapters (including cheap eBay clones, and DIY boards running the open-source [AR488](https://github.com/Twilight-Logic/AR488) firmware) implement the **Prologix** command protocol — a simple `++`-prefixed ASCII control language (`++addr`, `++mode`, `++read`, `++auto`, ...) layered over what the OS sees as a plain serial port (USB) or a plain TCP socket (Ethernet variants), so the existing Serial/TCP transports already do the I/O; only a thin Prologix-protocol controller layer (GPIB addressing, read-after-write/EOI handling) is new.

Three distinct things get called "GPIB-USB adapter" on eBay, worth telling apart before buying:

- **Genuine Prologix hardware** — the commercial reference implementation; if the controller layer is built against the published Prologix manual, this is the known-good target to validate against.
- **AR488** (self-built: an Arduino Uno/Nano/Mega + a GPIB transceiver IC, e.g. SN75160/SN75161, running open-source firmware) — implements the *full* Prologix `++` command set except `++lon` (device/listen-only mode), plus its own extensions (a macro feature, Bluetooth support on some builds, and a different `++savecfg` behavior to reduce Arduino EEPROM wear). Being open source, any protocol question is answerable by reading the firmware directly rather than guessing — genuinely lower-risk than either commercial option below, at the cost of having to assemble it yourself. Pre-built eBay boards that are just AR488 flashed onto a ready-made Arduino+transceiver board exist too, giving turnkey convenience without losing the open-source-firmware guarantee.
- **NI GPIB-USB-HS clones** (**not recommended**) — a different, proprietary, non-serial USB protocol requiring the NI-488.2 driver (or risky reverse-engineered access), with real reported compatibility problems even between genuine-NI-driver and clone hardware.

Verify which of these three a specific listing's adapter actually speaks before buying against this design — "GPIB-USB adapter" alone doesn't imply Prologix-compatible.

## Open questions

- Exact shape of the internal BLE adapter contract (discovery, characteristic read/write/subscribe) that per-OS BLE backends implement — needs to be settled before the first (Windows) backend is built, so later platform adapters aren't retrofitted against something that only fits Windows.Devices.Bluetooth's shape by accident. HID stays a single cross-platform implementation (HidSharp already handles the OS differences) — the "how much can stay uniform" question is now BLE-specific, not both.
- ~~Reconnect/retry policy: core or per transport?~~ **Decided 2026-10-03:** timeout and retry are **configurable options**, owned by the core. **Built 2026-10-03** as `SessionLimits` (`--connecttimeoutms`, `--connectretries`, `--connectretrydelayms`); all default to off, so per-transport defaults are not set yet.
- How a UDP listener's "first datagram" moment maps onto the `Session` model — same question as TCP, not yet resolved for the datagram case.
- Whether a future version should support one session per accepted TCP connection (a real multi-peer listener) instead of v1's one-peer-at-a-time policy, and if so how that's surfaced (e.g., the session spawning child sessions per peer).
