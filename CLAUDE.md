# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

`dev-term` is a modular, extensible development terminal for talking to devices over serial, TCP,
and (eventually) other transports — decoding/presenting whatever comes back and sending commands.
Built on .NET 10. Full design intent lives in [`docs/design/`](docs/design/README.md); this file
covers only what's actually built and the conventions that matter for working in this codebase.

## Commands

```bash
dotnet build                          # whole solution (DevTerm.slnx)
dotnet test                           # whole solution — currently fast (well under a second per project)
dotnet test tests/DevTerm.Core.Tests  # one project
dotnet test --filter "FullyQualifiedName~AsciiPresenterTests"   # one class, any project
dotnet run --project src/DevTerm.Console -- --listports true    # list serial ports
dotnet run --project src/DevTerm.Console -- --listhiddevices true    # list USB HID devices
dotnet run --project src/DevTerm.Console -- --transport serial --port COM3 --presenter ascii --lineending Cr
dotnet run --project src/DevTerm.Console -- --transport hid --hidvendorid 6421 --hidproductid 45018
```

There is no separate lint step; `dotnet build` surfaces analyzer warnings (MSTest analyzers included).

A personal, untracked `appsettings.Local.json` next to `DevTerm.Console`'s output (copied there on
build if present in `src/DevTerm.Console/`) holds a saved connection profile, so `dotnet run
--project src/DevTerm.Console` with **no arguments** connects using whatever's saved there — see
"Configuration" below. `src/DevTerm.Console/Properties/launchSettings.json` has named profiles for
real devices used during development (IDE "Debug" dropdown, or `dotnet run --launch-profile "<name>"`).

## Architecture

**Core abstractions** (`DevTerm.Core`): `ITransport` (byte-stream connection lifecycle + I/O),
`IPresenter` (bytes → zero or more rendered strings — see "Presenters" below), `Session` (binds one
transport to a `Pipeline` of presenters), `Pipeline` (fans a byte chunk out to every presenter and
flattens their outputs into `PresenterOutput`s). Everything is resolved through DI
(`Microsoft.Extensions.DependencyInjection`) — nothing `new`s up a transport or presenter directly;
each plugin-ish project exposes an `AddXyz(IServiceCollection)` extension.

**Project layout**:
- `DevTerm.Core` — the abstractions above, plus `AddDevTermCore()`.
- `DevTerm.Transports.Serial` / `DevTerm.Transports.Tcp` / `DevTerm.Transports.Hid` — `ITransport`
  implementations. Each is independently testable via a fake stream (see "Testing" below), never
  real hardware/sockets.
- `DevTerm.Presenters.Text` — ASCII (line-buffered), UTF-8, hex, decimal, octal, binary.
- `DevTerm.UiDefinitions` — a framework-agnostic, JSON/XML-serializable model for declaring a
  device control panel once (`UiDefinition` → `UiSection`s → `UiControl`s: button/toggle/slider/
  numeric/choice/textField/indicator) so every front end can render it generically instead of
  hand-coding a UI per device per front end. See docs/design/ui-definitions.md. Model + round-trip
  serialization only so far — nothing yet reads this model to actually produce Terminal.Gui/WPF
  controls, and it isn't wired to a live device (`IControlSurface` itself is still design-only).
- `DevTerm.DeviceManifests` — a no-code, JSON/XML `DeviceManifest` (identity, a transport hint, the
  declarative command/response schema from device-control-modules.md, and a `UiDefinition`) plus
  `DeviceManifestLoader`, which loads one from a single file, a folder (`device.json` at its root),
  or a `.zip` of one (extracted then loaded as a folder — no separate zip-handling logic anywhere
  else). See docs/design/device-manifests.md. Referenced `.ksy`/UI files are resolved relative to
  the manifest's own location. Model + loader only — not wired to anything that opens a real
  connection or reads a `.ksy` file yet.
- `DevTerm.Configuration` — shared front-end bootstrapping: `CliOptions`/`CliOptionsValidator`,
  `DevTermConfiguration` (config layering), `LineEnding`, `ConnectionErrorMessages`,
  `ConnectionDescription`, and `AddDevTermFrontEnd` (the one place that wires core + text
  presenters + the selected transport from `CliOptions`) — every front end below calls this instead
  of duplicating the wiring, which is what makes one saved `appsettings.Local.json` profile work
  from any of them.
- `DevTerm.Console` — the console front end: `Program.cs` builds the DI host (`Host.CreateDefaultBuilder`)
  and dispatches to `CliMode` (the scriptable/interactive line-based loop) or `TuiMode`
  (a full-screen Terminal.Gui UI) based on `--tui <bool>`.
- `DevTerm.Wpf` — the GUI front end (WPF, `net10.0-windows`, Windows-only). `App.xaml.cs` builds the
  same kind of DI host itself (a WPF app has no `Main`/host-builder entry point), then hands the
  resolved `Session` to `MainWindow`.

**Read path**: transports read via `System.IO.Pipelines` (`ITransport.Input` is a `PipeReader`) —
a shared `StreamToPipePump` (`DevTerm.Core.Transports`) pumps a `Stream` into a `PipeWriter`'s
pooled buffers, avoiding a per-chunk array allocation. `Session` runs its own pull loop
(`PipeReader.ReadAsync`/`AdvanceTo`) rather than an event-based push model.

**Presenters return a list, not a single string** (`IReadOnlyList<string> Render(ReadOnlySequence<byte>)`):
a presenter can buffer internally and emit nothing until it has something complete (the ASCII
presenter buffers until a line terminator or a max length), or several items if more than one
boundary arrived in one read. `Pipeline.Render` flattens this into zero or more `PresenterOutput`s.

**Configuration**: every configurable component uses the Options pattern
(`Microsoft.Extensions.Options`) — a component takes `IOptions<TOptions>` directly in its
constructor (not a primitive parameter + factory lambda), so a plain
`services.AddSingleton<TInterface, TImpl>()` resolves it correctly. `DevTermConfiguration`
(currently in `DevTerm.Console`, moving to `DevTerm.Configuration`) layers config sources:
`appsettings.json` → `appsettings.<environment>.json` → `appsettings.Local.json` (untracked,
per-machine profile) → environment variables (`DEVTERM_` prefix) → command-line args (highest
precedence) — via the standard `Microsoft.Extensions.Configuration` extensions, not a hand-rolled
parser. `CliOptions` property names double as CLI flag names (case-insensitive).

Full architecture/rationale, including what's designed but not yet built (dynamic plugin loading,
protocol decoders, rendering presenters, device control modules, RFC 2217, UDP/HID/BLE transports,
TUI, WPF): see [`docs/design/`](docs/design/README.md). Current backlog/in-progress state:
[`TODO.md`](TODO.md). Daily change log: `docs/changes/YYYY-MM-DD.md`.

## Non-obvious constraints worth knowing before touching related code

- **Moq/Castle cannot mock a method taking `Span<T>`/`ReadOnlySpan<T>`** (a ref struct can't back
  a mocked generic parameter). Interface members meant to be mockable use `ReadOnlyMemory<T>` or
  `ReadOnlySequence<T>` instead, converting to `.Span` only inside the implementation.
- **`SerialPort.BaseStream.ReadAsync(Memory<byte>, CancellationToken)` does not reliably honor
  cancellation** on real hardware (verified, not theoretical) — `DevTerm.Transports.Serial` reads
  via the `SerialPort.DataReceived` event instead (a cancelable `TaskCompletionSource`, not a
  polling loop). Don't reintroduce a direct `ReadAsync`/`Task.Run`-wrapped-sync-`Read` on
  `SerialPort` without re-verifying against real hardware — both were tried and both had real
  cancellation/lifetime hazards.
- **`SerialPort` defaults `DtrEnable`/`RtsEnable` to `false`**, unlike most terminal tools (and
  pyserial) — plenty of devices stay silent without DTR asserted. dev-term defaults both to `true`.
- **`Microsoft.Extensions.Configuration.CommandLine` has no bare-boolean-flag support** — a
  standalone `--listen` is not a valid boolean `true`; it must be `--listen true`.
- **Terminal.Gui v2 is not source-compatible with v1** — namespaces are split
  (`Terminal.Gui.App`, `.Views`, `.ViewBase`, `.Input`, `.Drivers`, ... instead of one flat
  `Terminal.Gui`), and `View.KeyDown` hands out a `Key` directly rather than a `KeyEventEventArgs`
  wrapper. v1-era examples/docs don't apply. When the installed version's actual API shape is in
  doubt, check it directly (e.g. reflect over the installed package's DLL) rather than guessing.
- **HidSharp's `HidStream` has the same cancellation hazard as `SerialPort.BaseStream`** — no event
  analogous to `DataReceived`, and its `Read`/`Write` are `BeginRead`/`EndRead`-backed under the
  default `Stream.ReadAsync`, which doesn't meaningfully honor a `CancellationToken` mid-read.
  `DevTerm.Transports.Hid`'s `HidReadStream` uses a dedicated background thread (owned, joined on
  close, bounded by `ReadTimeout`) handing reports to a `Channel<byte[]>` instead — never reintroduce
  a `Task.Run`-wrapped blocking read per call here for the same reason it was rejected for serial.
- **A zero-length `Stream.Write` is invalid on Windows HID, unlike serial/TCP** — a HID report is a
  fixed, non-zero, device-defined length; writing zero bytes throws a raw Win32 `IOException`
  instead of being a no-op (verified against real hardware: an empty typed line crashed the whole
  app before this was fixed). `HidTransport.WriteAsync` no-ops on empty data; a wrong *non-zero*
  length is a real, expected device-specific framing failure and still throws — see the next point.
- **`CliMode`/`TuiMode`'s send path catches any device I/O failure, not just `TimeoutException`** —
  a generic text presenter's typed input has no way to guarantee it matches a specific device's
  framing requirements (a HID report's exact length, for one), so a send can legitimately fail for
  reasons that aren't a timeout; catch broadly (`ConnectionErrorMessages.IsConnectionFailure`) and
  report it instead of letting it crash the process, the same way `OpenAsync` failures already are.
- **`System.Xml.Serialization.XmlSerializer` cannot serialize `Dictionary<TKey,TValue>`** — it
  throws `NotSupportedException` ("implements IDictionary") at first-use reflection time, not at
  compile time. Any type meant to round-trip through both `System.Text.Json` and `XmlSerializer`
  (see `DevTerm.UiDefinitions`, `DevTerm.DeviceManifests`) uses a plain `List<T>` of a small
  Key/Value class instead of a dictionary, which both serializers handle natively with no
  special-casing.
- Verify against real hardware before trusting a fix, when hardware is available — several bugs in
  this codebase (all of the above) were only caught by testing against actual devices, not by unit
  tests alone. `docs/changes/` records what was verified this way.
