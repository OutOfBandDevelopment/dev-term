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

### TCP

Supports both directions from day one, since either the device or dev-term may be the one that connects:

- **Client mode** — connect out to a host:port (the common case: a device exposing a TCP server).
- **Listener mode** — bind and accept incoming connections on a local port, for devices that connect out to dev-term (e.g., a board acting as a TCP client). A listener session represents "waiting for a peer"; `OpenAsync` doesn't complete until a peer connects, and the session then behaves like any other connected transport for read/write. **v1 policy**: one peer at a time — the listening socket stops accepting as soon as one connection is accepted, so a second simultaneous inbound connection is simply not accepted until the session is closed and reopened. Queuing/multiplexing multiple concurrent peers on one listener session is not supported yet (see open questions).

Which mode a given session uses is a configuration choice (see the Options pattern in [platform.md](platform.md)), not two different transport plugins.

### UDP

Datagram-oriented; no connection lifecycle in the traditional sense, but still modeled as open/close for consistency with other transports. Also supports both directions:

- **Target mode** — send datagrams to a fixed host:port (and receive any replies from that same peer).
- **Listener mode** — bind a local port to receive datagrams from any sender, useful for devices that push telemetry/broadcast data unprompted. A bound listener can still send (e.g., reply to whichever peer last sent data, or to a separately configured target), since UDP has no inherent client/server asymmetry.

Because UDP preserves datagram boundaries, each received datagram is delivered to the presenter pipeline as one discrete message rather than merged into an undifferentiated byte stream (see [architecture.md](architecture.md)).

### USB HID

Report-based I/O against a specific VID/PID (and usage page/usage where needed) — useful for devices that expose a HID interface for control/debug rather than a serial port. Direction: [HidSharp](https://www.nuget.org/packages/HidSharp) — cross-platform (Windows/Linux/macOS), no vendor driver install required since HID already has a generic OS-level class driver everywhere (unlike raw USB device classes, which is exactly why HID was ruled out for USBTMC-class instruments — see the note under "Extensibility" below). Not a fit for report-based protocols alone; a device that turns out to actually be a different USB class entirely (as [Radex One](proposals/radex-one-protocol.md) initially seemed but wasn't confirmed to be until checked directly) needs re-verifying before assuming this transport applies.

### BLE

Connect to a peripheral by address/name, then read/write/subscribe to specific GATT characteristics.

**Cross-platform by design, via a pluggable per-OS adapter seam** — rather than picking one platform's native BLE API and making the whole transport (and everything that references it) platform-locked, `DevTerm.Transports.Ble` defines its own small internal adapter contract (peripheral discovery, GATT service/characteristic read/write/subscribe) that the transport itself talks to, with OS-specific implementations behind it:

- **Windows** — `Windows.Devices.Bluetooth` (WinRT GATT client), the most mature/documented option, built first.
- **Linux** — BlueZ over D-Bus.
- **macOS** — CoreBluetooth.

None of these ship day one; the adapter seam exists so each can land independently (including as a community/self-contributed adapter) without touching the transport's public shape or forcing every front end onto one platform's API. This is a real, non-trivial commitment — BLE stack differences are still likely the hardest part of this transport to get uniform — but it's a deliberate choice over the easier Windows-only path, given for the console app (unlike WPF) cross-platform reach already matters.

**BLE Serial** is the common special case worth naming explicitly: many hobbyist/embedded BLE devices don't expose a bespoke GATT profile at all — they emulate a UART over two characteristics (one for host→device writes, one for device→host notifications), most commonly following the de facto [Nordic UART Service](https://developer.nordicsemi.com/nRF_Connect_SDK/doc/latest/nrfxlib/nrf_ble/doc/service.html) UUIDs (`6E400001-B5A3-F393-E0A9-E50E24DCCA9E` service, `...002` RX/write, `...003` TX/notify). A BLE Serial transport mode defaults to those UUIDs but stays configurable per device, since not every device that "acts like serial over BLE" actually uses NUS — worth confirming per device (e.g., via a BLE scanner app) before assuming the default applies, same caution as every other vendor-protocol-claim in this project.

## Extensibility

New transports (CAN bus, SPI/I2C bridge adapters, raw sockets, SSH, named pipes, etc.) implement the same `ITransport` contract and are picked up via the plugin host — no core changes required. RFC 2217 (a remote-controllable serial port over Telnet) is a concrete planned one — see [rfc2217.md](rfc2217.md), including a real-world caveat about vendor-specific variants that don't actually match the IETF standard despite the name.

**USBTMC** (the USB device class most bench test equipment actually uses for local USB control, e.g. Rigol/Keysight instruments) is deliberately *not* in the initial transport list — it's neither HID nor a virtual COM port, but its own USB class with dedicated bulk endpoints and message framing, so it needs its own transport built against raw USB (WinUSB/LibUsbDotNet) rather than reusing HID or Serial. See the [SCPI proposal](proposals/scpi-instrument-control.md) for target hardware; not yet designed in detail.

**GPIB** is reachable without a new `ITransport` at all, for the common case: most inexpensive "IEEE-488 to USB/Ethernet" adapters (including cheap eBay clones, and DIY boards running the open-source [AR488](https://github.com/Twilight-Logic/AR488) firmware) implement the **Prologix** command protocol — a simple `++`-prefixed ASCII control language (`++addr`, `++mode`, `++read`, `++auto`, ...) layered over what the OS sees as a plain serial port (USB) or a plain TCP socket (Ethernet variants), so the existing Serial/TCP transports already do the I/O; only a thin Prologix-protocol controller layer (GPIB addressing, read-after-write/EOI handling) is new. **Caveat**: a separate family of cheap eBay clones instead emulates National Instruments' own GPIB-USB-HS device — a proprietary, non-serial USB protocol requiring the NI-488.2 driver (or risky reverse-engineered access), with real reported compatibility problems even between genuine-NI-driver and clone hardware. Verify which protocol a specific listing's adapter actually speaks before buying against this design — "GPIB-USB adapter" alone doesn't imply Prologix-compatible.

## Open questions

- Exact shape of the internal BLE adapter contract (discovery, characteristic read/write/subscribe) that per-OS BLE backends implement — needs to be settled before the first (Windows) backend is built, so later platform adapters aren't retrofitted against something that only fits Windows.Devices.Bluetooth's shape by accident. HID stays a single cross-platform implementation (HidSharp already handles the OS differences) — the "how much can stay uniform" question is now BLE-specific, not both.
- Reconnect/retry policy: a core concern, or left to each transport plugin?
- How a UDP listener's "first datagram" moment maps onto the `Session` model — same question as TCP, not yet resolved for the datagram case.
- Whether a future version should support one session per accepted TCP connection (a real multi-peer listener) instead of v1's one-peer-at-a-time policy, and if so how that's surfaced (e.g., the session spawning child sessions per peer).
