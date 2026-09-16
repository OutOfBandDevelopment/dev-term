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
dotnet test --filter "TestCategory=UNIT"          # fast, hardware-free (the default CI-safe subset)
dotnet test --filter "TestCategory=INTEGRATION"   # spawns real processes/sockets, still no real hardware
dotnet test --filter "TestCategory=DEV-LOCAL" --settings devterm.runsettings   # needs real hardware — see "Testing" below
dotnet run --project src/DevTerm.Console -- --listports true    # list serial ports
dotnet run --project src/DevTerm.Console -- --listhiddevices true    # list USB HID devices
dotnet run --project src/DevTerm.Console -- --transport serial --port COM3 --presenter ascii --lineending Cr --cli true
dotnet run --project src/DevTerm.Console -- --transport hid --hidvendorid 6421 --hidproductid 45018 --cli true
```

**The full-screen TUI is the console app's default mode** — omitting `--cli true` above opens the
TUI instead of the scriptable REPL loop (useful interactively, but not what you want when piping
commands via a shell for a quick real-hardware check, which is what most of this file's examples
are for). `--tui false` is equivalent to `--cli true`.

There is no separate lint step; `dotnet build` surfaces analyzer warnings (MSTest analyzers, plus
`.editorconfig`'s built-in Roslyn code-style rules and StyleCop.Analyzers — see
[`docs/coding-standards.md`](docs/coding-standards.md) for what's actually declared/enforced and how
to add a new rule). StyleCop itself starts with every rule silenced except what's explicitly turned
on — it produced ~1,800 warnings against this codebase with its defaults, almost none of them a real
standard anyone declared, so don't re-enable a whole category to "see what's there."

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
TUI, WPF): see [`docs/design/`](docs/design/README.md). Current in-progress state:
[`TODO.md`](TODO.md); not-yet-started backlog/research: [`BACKLOG.md`](BACKLOG.md). Daily change
log: `docs/changes/YYYY-MM-DD.md`.

## Testing

Every test class carries a `[TestCategory]`, one of `UNIT` (fast, hardware-free, the default subset once
a CI pipeline exists — includes `DevTerm.Console.Tests.TuiModeTests`, which drives a real
`TuiMode` window headlessly via Terminal.Gui's own testing API), `INTEGRATION` (spawns a real
process and/or a real local socket, but no real external hardware — see
`DevTerm.Console.Tests.ConsoleAppCliTests`), or `DEV-LOCAL` (needs an actual physical device
reachable from wherever the test runs — see `RealHardwareCliTests`/`RealHardwareMainWindowTests`,
opt-in via `devterm.runsettings` at the repo root; without a settings file they report
`Assert.Inconclusive`, not a failure) — enforced, not just a convention: `tests/DevTerm.CodingStandards.Tests`
reflects over every test assembly and fails on a class with no category, or an unrecognized one (see
`docs/coding-standards.md`'s Testing section). Full rationale, including two real WPF/async gotchas found
building the `DEV-LOCAL` WPF tests (a missing `DispatcherSynchronizationContext` sends `await`
continuations to the wrong thread; showing a `MainWindow` that's already been connected manually
double-opens the session and corrupts the single-reader `PipeReader`) and the two Terminal.Gui
`Application.Invoke`/`IInputInjector` gotchas below: see
[`docs/design/testing.md`](docs/design/testing.md).

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
- **A WPF `Window` created on a manually-spun-up STA thread (no `Application.Run()`) needs a real
  `DispatcherSynchronizationContext` installed and a `Dispatcher.PushFrame` loop actually running**
  — without both, `await` continuations after genuine async I/O resume on an arbitrary thread-pool
  thread instead of the STA thread that owns the UI, throwing "The calling thread cannot access
  this object because a different thread owns it." Only shows up with real I/O, not a fake
  transport whose "async" calls complete synchronously — see `DevTerm.Wpf.Tests.StaTestRunner` and
  [`docs/design/testing.md`](docs/design/testing.md).
- **Don't both call `MainWindow.Show()` and `MainWindow.ConnectAsync()` from the same caller** —
  `Show()` fires the real `Loaded` event, which calls `ConnectAsync` on its own; calling it again
  opens the session twice concurrently, and two concurrent readers on one `PipeReader` corrupts its
  internal state (throws "Writing is not allowed after writer was completed" from a seemingly
  unrelated later call, not from the double-open itself).
- **Terminal.Gui v2.5.0's `Application.Invoke` silently queues forever unless a real
  `Application.Run()` loop is actively pumping somewhere** — calling it from a background thread
  with no loop running, and then calling `LayoutAndDraw`/`RaiseIteration` manually, never flushes
  it. Conversely, `IInputInjector.InjectKey`/`ProcessQueue()` only reliably reaches the focused view
  when there's *no* `Application.Run()` loop running concurrently on another thread — the two don't
  compose. See `DevTerm.Console.Tests.TuiTestRunner`'s two separate run modes
  (`RunHeadless`/`RunWithLoop`) and [`docs/design/testing.md`](docs/design/testing.md).
- **`Session` recreates its read-loop `CancellationTokenSource` on every `OpenAsync`, not once for
  the object's lifetime** — a `CancellationTokenSource` can only ever be cancelled once, so reusing
  the same one across a Close-then-reopen would start the new read loop with an already-cancelled
  token, ending it immediately and silently (found while adding a Connect/Disconnect menu item —
  see `DevTerm.Core.Tests.SessionTests.OpenAsync_AfterClose_RestartsTheReadLoopForRealIncomingData`,
  a regression test confirmed to fail without this fix). Both `TuiMode`/`MainWindow`'s
  `ToggleConnectionAsync` methods call `Session.OpenAsync`/`CloseAsync` repeatedly on the same
  `Session` instance, so this matters for any future code doing the same.
- **A WPF `{Binding ...}` doesn't populate a control synchronously from a constructor-assigned
  `DataContext` if the window is never `Show()`n** — checked directly: a `TextBox` bound to a
  view-model property that already had a value at construction time still read back empty
  immediately afterward, in a test that (deliberately, per the convention below) never calls
  `Show()`. A single `StaTestRunner.DoEvents()` pump after construction is enough; property-change-
  driven updates *after* that initial pump apply synchronously as normal. See
  `DevTerm.Wpf.Tests.DeviceProfilesWindowTests`.
- **`Window.DialogResult` throws `InvalidOperationException` unless the window was actually shown
  via `ShowDialog()`** — relevant because `DeviceProfilesWindow` sets it when its shared
  `ConnectionEditorViewModel.CloseRequested` fires, and tests drive that view model directly without
  ever calling `ShowDialog()` (the same "don't `Show()` a window under direct test" convention
  below, just hitting WPF's dialog-specific version of it). The window catches and ignores that
  specific exception there — `ConnectionEditorViewModel.Result` is already set correctly regardless
  of whether `DialogResult` could be set.
- **A `MenuItem`'s `Key`/`InputGestureText` argument (Terminal.Gui) or `InputGestureText` (WPF)
  only labels a keyboard shortcut for display — neither registers a live accelerator by itself.**
  `TuiMode`'s Ctrl+Q previously did nothing despite being advertised in the title bar; the real fix
  needed an explicit handler on the global `Application.KeyDown` event (a per-view `Window.KeyDown`
  handler doesn't reliably see a key already routed to a focused child first — the send field
  normally has focus). `MainWindow`'s WPF menu needed the equivalent: an explicit `PreviewKeyDown`
  check, not just `MenuItem.InputGestureText`.
- **Terminal.Gui v2.5.0's `OptionSelector<T>.Values` cannot be set directly** — it throws
  `InvalidOperationException` ("Setting Values directly is not allowed"); the selector derives its
  values from `Enum.GetValues<T>()` automatically, and `T` must be `struct, Enum` (a plain `string`
  choice list needs a purpose-built local enum, converted to/from the real option string at the
  call site — see `ConfigureMode.TransportChoice`/`PresenterChoice`).
- **A `private enum` nested in one class is invisible to a different class in the same file/assembly
  — `InternalsVisibleTo` does not help, since it only affects `internal` members, never `private`
  ones.** `ConfigureMode.TransportChoice`/`PresenterChoice` needed to be `internal enum`, not
  `private enum`, purely so `ConfigureWindowParts` (a separate class) and the test assembly could
  reference `OptionSelector<TransportChoice>` as a property type at all.
- **Terminal.Gui's headless "dotnet" driver reports `fg=(255,255,255) bg=(255,255,255)`
  (white-on-white, invisible) for any cell still on the default, unstyled color scheme** — there's
  no real terminal behind headless mode to resolve an actual theme's colors. Confirmed by reflecting
  `Cell.Attribute` (a real `Terminal.Gui.Drawing.Attribute` with true RGB `Foreground`/`Background`,
  not just a 16-color name) for a plain `Label` and the window border, both white-on-white, while a
  menu bar highlight or a focused field reported real, distinguishable colors. Only relevant once
  you try to render the buffer as an actual image (`DevTerm.Console.Tests.TuiScreenshot`) — a plain
  `Cell.Grapheme` text dump (`TuiTestRunner.DumpBuffer`) never touches color and was unaffected. Any
  future color-aware TUI capture should fall back to a sane default (black-on-white) when a cell's
  foreground equals its background, rather than trusting the driver's reported color literally.
- **A WPF `Window` that's only ever `Measure`d/`Arrange`d, never `Show()`n, renders as a blank image
  under `RenderTargetBitmap`** — confirmed by inspecting the actual output pixels, not assumed. It
  needs a real (if off-screen/invisible) `Show()` to get a `PresentationSource`/compositor target
  before `RenderTargetBitmap.Render()` produces real content. See `DevTerm.Wpf.Tests.WpfScreenshot`.
- **Don't call `Window.Close()` on a shown `MainWindow` from test automation** — its `OnClosing`
  handler's cancel-then-async-cleanup-then-reclose pattern (needed so `Session.CloseAsync`/
  `DisposeAsync` can be awaited before the window actually closes) raced against
  `StaTestRunner.Run`'s single manually-pumped `DispatcherFrame` and threw "Cannot ... Close ...
  while a Window is closing" — a real reentrancy edge case never exercised before because no
  existing test actually closed a shown window. Screenshot tests just leave the window open; the
  test process exits shortly after regardless.
- **The Terminal.Gui headless key injector (`IInputInjector`/`TuiTestRunner.TypeText`) degrades
  after enough `Application.Init`/`Shutdown` cycles have already run in the same test process** —
  this was already known for a single class (see the `Application.Invoke`/`IInputInjector` note
  above and `TuiModeTests.CtrlQ_RequestsStop`'s doc comment), but turned out to be cross-class too: a
  new screenshot test using `TypeText` left a *later, different* test's own `TypeText` call unable
  to reach the focused field, even though each test's own `Application.Init`/`Shutdown` cycle
  completed cleanly. When a test doesn't need to prove real key routing — e.g. a screenshot just
  wants a field showing some text — set the control's `.Text` directly and call
  `Application.LayoutAndDraw(true)` instead of injecting keys.
- Verify against real hardware before trusting a fix, when hardware is available — several bugs in
  this codebase (all of the above) were only caught by testing against actual devices, not by unit
  tests alone. `docs/changes/` records what was verified this way.
