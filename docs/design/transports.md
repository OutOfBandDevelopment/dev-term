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

Report-based I/O against a specific VID/PID (and usage page/usage where needed) — useful for devices that expose a HID interface for control/debug rather than a serial port.

### BLE

Connect to a peripheral by address/name, then read/write/subscribe to specific GATT characteristics. Platform BLE stack differences (Windows/macOS/Linux) make this likely the hardest transport to keep fully uniform cross-platform.

## Extensibility

New transports (CAN bus, SPI/I2C bridge adapters, raw sockets, SSH, named pipes, etc.) implement the same `ITransport` contract and are picked up via the plugin host — no core changes required.

## Open questions

- How much platform-specific BLE/HID capability can realistically stay uniform across Windows/macOS/Linux vs. needing per-platform plugin variants.
- Reconnect/retry policy: a core concern, or left to each transport plugin?
- How a UDP listener's "first datagram" moment maps onto the `Session` model — same question as TCP, not yet resolved for the datagram case.
- Whether a future version should support one session per accepted TCP connection (a real multi-peer listener) instead of v1's one-peer-at-a-time policy, and if so how that's surfaced (e.g., the session spawning child sessions per peer).
