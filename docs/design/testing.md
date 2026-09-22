# Testing

## Purpose

Describes how dev-term's tests are organized and why — three tiers, each with a real, different
cost/hardware-dependency profile, distinguished by MSTest `[TestCategory]` so they can be run as
separate subsets (`dotnet test --filter "TestCategory=..."`), which matters more as soon as any
CI/CD pipeline exists (a pipeline can run `UNIT` and `INTEGRATION` on every push, and never attempt
`DEV-LOCAL` at all — it depends on specific hardware on a specific home network).

## Three categories

- **`UNIT`** — fast, hardware-free, no process/OS boundary crossed. The large majority of the
  suite: transport tests against a fake port/device (a real `Pipe`, not a real socket/serial
  port/USB device — see `SerialTransportTests`/`TcpTransportTests`/`HidTransportTests`), presenter
  tests, serialization round-trips (`DevTerm.UiDefinitions`/`DevTerm.DeviceManifests`), WPF
  `MainWindow` logic tests driven in-process against a `FakeTransport` (see
  `DevTerm.Wpf.Tests.MainWindowTests`) rather than a real connection, and scripted-response tests
  against `LoopbackTransport` (see "Scripted responses without a real device" below).

## Scripted responses without a real device: `LoopbackTransport`

`FakeTransport` (`DevTerm.Console.Tests`/`DevTerm.Wpf.Tests`) is deliberately dumb — a bare
`Pipe`-backed `ITransport` a test drives by hand (`WriteAsync` just records the payload;
`PushIncomingAsync` is a separate manual call to simulate a reply). That's fine for tests that only
need one or two canned exchanges, but is tedious for anything wanting realistic request/response or
multi-line "event stream" behavior — and a real `TcpListener` loopback socket (the pattern
`TuiModeSwitchProfileTests`/`ConsoleAppCliTests` use) is `INTEGRATION`-tier overkill when the actual
network stream isn't what's under test.

`DevTerm.Console.Tests.LoopbackTransport` fills that gap: an in-process `ITransport`, same
`Pipe`-backed shape as `FakeTransport`, but backed by a script (`IReadOnlyList<LoopbackRule>`) of
regex-matched commands, each producing zero or more deterministic response lines (no `Random` — a
given input always produces the same output, so tests can assert exact text). A command with no
matching rule gets a visible `? Unrecognized: ...` reply rather than silence, so a test with an
incomplete script fails on a wrong/missing line instead of hanging on a reply that never comes. Each
response line is pushed back as its own write, so a line-buffering presenter (`AsciiPresenter`) sees
separate lines/events rather than one blob — this is what makes an "N discrete events" style reply
possible at all. `LoopbackScript.Default()` provides three example rules (an exact-match greeting, a
parameterized "send me a stream of N bytes" command via `LoopbackGenerators.AsciiStream`, and a
parameterized "send me N events" command via `LoopbackGenerators.Events`); a test can pass its own
rule list instead when it needs different behavior. See `LoopbackTransportTests` for usage through a
real `Session`/`AsciiPresenter` pair — the same shape `TuiMode`/`MainWindow` use, just without
`TuiTestRunner`/`StaTestRunner`'s UI-thread machinery, since nothing here touches Terminal.Gui or
WPF. `UNIT`, not `INTEGRATION`: no socket, no process boundary, just a `Pipe`.
- **`INTEGRATION`** — crosses a real process or OS boundary, but no real external hardware: spawns
  the actual built `DevTerm.Console.dll` as a child process and drives it over real stdin/stdout,
  against a real local TCP socket the test itself opens (loopback, not a real device) — see
  `DevTerm.Console.Tests.ConsoleAppCliTests`. Slower than `UNIT`, still fully self-contained.
- **`DEV-LOCAL`** — needs actual physical hardware reachable from wherever the test runs (a
  specific home-network IP, a real instrument powered on). Never meaningful in CI. See
  `RealHardwareCliTests`/`RealHardwareMainWindowTests` below.

## Real-hardware tests: opt-in via `.runsettings`, not hardcoded

`RealHardware*Tests` classes read their target (host/port, currently) from
`TestContext.Properties`, populated from a `.runsettings` file's `<TestRunParameters>` — see
`devterm.runsettings` at the repo root. Run without one, `dotnet test` alone, they report
`Assert.Inconclusive` (shown as "Skipped", not a failure) — the default test run never depends on
any particular device being online. Run *with* one (`dotnet test` plus a settings-file option
pointing at `devterm.runsettings`), they exercise the real device — this is the same real
Tektronix 2230 (over a serial-to-Ethernet bridge) already used for manual real-hardware
verification throughout this project's `docs/changes/` history, now automated the same way rather
than only ever checked by hand.

## WPF automation: in-process, not OS-level UI Automation

`DevTerm.Wpf.Tests` drives a real `MainWindow` — real XAML, real controls — but through its
testable async entry points (`ConnectAsync`, `SendCurrentInputAsync`, exposed as `internal` methods
an `[assembly: InternalsVisibleTo]` opens to the test assembly) rather than a heavier OS-level UI
Automation library (e.g. FlaUI). This is deliberately narrower than true end-to-end UI Automation —
it doesn't click a rendered pixel or drive real OS input events — but it exercises the actual
production logic and real control state (`OutputList.Items`, `SendBox.Text`, `Title`) with no new
dependency, and no need for a real display/window manager.

Two non-obvious things this needed, both confirmed by real failures, not anticipated up front:

- **A dedicated STA thread with a real message loop, not a blocking wait.** WPF objects need a
  `Dispatcher` on an STA thread, and MSTest provides neither. A naive
  `thread.Start(() => asyncAction().GetAwaiter().GetResult())` works for a `FakeTransport` (whose
  "async" methods complete synchronously and never truly suspend) but deadlocks or misbehaves for
  real I/O: without an installed `DispatcherSynchronizationContext`, an `await` continuation after
  genuine async work resumes on an arbitrary thread-pool thread instead of the STA thread that owns
  the UI objects, throwing "The calling thread cannot access this object because a different thread
  owns it." — confirmed against the real device, not a synthetic case. The fix (see
  `StaTestRunner.Run`): install a `DispatcherSynchronizationContext`, then keep a
  `Dispatcher.PushFrame` message loop running for the whole duration of the async test body, so
  posted continuations are actually serviced instead of queued forever.
- **Never call `Show()` in a test that also calls `ConnectAsync()` directly.** `Show()` fires the
  window's real `Loaded` event, which — exactly like the production app — calls `ConnectAsync` on
  its own. Calling it a second time from the test opens the session twice concurrently, and two
  concurrent readers on one `PipeReader` is explicitly unsupported — confirmed the hard way: it
  corrupts the pipe's internal state and throws "Writing is not allowed after writer was completed"
  from a completely unrelated call several lines later, not from the double-open itself. Tests that
  want deterministic control call `ConnectAsync()` directly and never call `Show()`.
- **WPF UI tests run sequentially (`[DoNotParallelize]`), not in parallel with the rest of the
  assembly.** Multiple concurrent WPF dispatchers/STA threads had real, observed cross-test
  interference (a test that passed in isolation intermittently failed when run alongside others) —
  confirmed by re-running the same suite several times before and after adding the attribute.

## CLI automation: spawn the real process

`ConsoleAppCliTests` locates the real built `DevTerm.Console.dll` next to the test assembly's own
output (both live under parallel `tests/X.Tests/bin/...` / `src/X/bin/...` paths, so the sibling
path is a simple string substitution — see the test file) and drives it via `Process.Start` with
redirected stdin/stdout/stderr. The one true end-to-end case (`CliMode_OverTcp_...`) opens a real
local `TcpListener` the test controls, so the whole path — process → `CliOptions` → `TcpTransport`
→ `Session` → `AsciiPresenter` → stdout — runs for real without needing an actual device.

## TUI automation: Terminal.Gui's own official testing API, two run modes

Terminal.Gui v2.5.0 ships a whole `Terminal.Gui.Testing` namespace (`IInputInjector`,
`InputInjector`, `TestInputSource`) plus a public `IOutputBuffer`/`Cell[,]` screen-buffer contract —
found by reflecting over the installed package (its docs/samples are still v1-flavored; the actual
shape had to be checked against the DLL, same approach `CLAUDE.md` already calls for). Confirmed
working with a real, throwaway console app before committing to the approach (not just from
signatures): `Application.Init("dotnet")` (the managed, cross-platform driver — confirmed to work
even with fully redirected/piped stdout, no real terminal needed), building a real `Window`/
`TextView`/`TextField`, injecting keys via `IApplication.GetInputInjector()`, and reading back the
exact rendered character grid via `Application.Driver.GetOutputBuffer().Contents[row, col].Grapheme`.

`DevTerm.Console.Tests.TuiModeTests` drives a real `TuiMode` window — via `TuiMode.BuildWindow`,
split out from `TuiMode.RunAsync` the same way `MainWindow.xaml.cs` exposes `ConnectAsync`/
`SendCurrentInputAsync` for WPF — through `TuiTestRunner`, which needed **two different run modes**
for two things that turned out not to work together, both found by actually running code, not
suspected from docs:

- **Headless (`TuiTestRunner.RunHeadless`)** — single-threaded: `Application.Init` → `Begin` →
  manual `LayoutAndDraw`, no `Application.Run()` loop at all. Key injection
  (`GetInputInjector().InjectKey`/`ProcessQueue()`) only works reliably here. Use for anything that
  types into the send field, and for reading back the screen buffer as a plain-text "screenshot".
- **Looped (`TuiTestRunner.RunWithLoop`)** — a dedicated background thread runs the real, blocking
  `Application.Run()`; the test thread marshals in via `Application.Invoke` (mirrors
  `DevTerm.Wpf.Tests.StaTestRunner`'s dedicated STA thread + pumped dispatcher). Needed because
  `Application.Invoke` silently queues forever and is never drained unless a real `Run()` loop is
  actively pumping somewhere — confirmed by calling it from a background thread with no loop running
  and finding neither `LayoutAndDraw` nor `RaiseIteration` ever flush it. Since `TuiMode`'s own
  `AppendOutput` uses `Application.Invoke` to marshal `Session.Output` events (raised on `Session`'s
  own background read-loop `Task`) onto the UI, exercising that specific wiring needs a real loop.

The two modes don't compose: injecting through `IInputInjector` while a real `Application.Run()`
loop is simultaneously pumping on another thread was tried and the injected keys never reached the
focused view, even marshaled onto the loop thread via `Application.Invoke` with `ProcessQueue()`
called explicitly right there — so a headless test never starts a loop, and a looped test never
injects keys, rather than trying to make one mode do both.

Two more real gotchas found landing the TUI's menu bar and Ctrl+Q shortcut, both confirmed by
reflecting over a real running window's own `KeyBindings`, not assumed:

- **A `MenuItem`'s `Key` constructor argument only labels the shortcut in the menu's display text —
  it doesn't register a live key binding by itself.** After building a menu with a Ctrl+Q
  `MenuItem`, neither the `Window`'s nor the `MenuBar`'s own `KeyBindings.GetBindings()` contained a
  Ctrl+Q entry at all. `TuiMode.BuildWindow` instead subscribes an explicit handler to it.
- **A per-view `KeyDown` handler on the `Window` doesn't reliably see a key already routed to a
  focused child first.** A `window.KeyDown` handler for Ctrl+Q fired when nothing else had focus,
  but not once the send field (which normally has focus) did. The fix was the global,
  static `Application.KeyDown` event instead, which fires ahead of per-view focus routing.
- Testing this needed yet another input-injection variant: `IInputInjector`/`ProcessQueue()`
  (reliable for typed text and button clicks elsewhere in this same headless mode) proved unreliable
  specifically for this global-event case once several other tests' Init/Shutdown cycles had already
  run earlier in the same test process — passed reliably alone, failed once run after the others.
  `Application.RaiseKeyDownEvent(key)` (a direct, documented dispatch call) didn't show the same
  degradation and is what `TuiModeTests.CtrlQ_RequestsStop` uses.
- Button clicks needed their own fix along the way, for a related reason: `View.SetFocus()` makes
  `HasFocus` report `true` without fully registering the view for command routing (a button focused
  this way then sent an injected Enter/Space did nothing), and Tab-navigating focus onto a button
  worked alone but not once several tests ran in the same process. `View.InvokeCommand(Command.Accept)`
  — a direct, documented way to invoke a view's command — is what `DevTerm.Console.Tests.ConfigureModeTests`
  uses instead; see its own `Click` helper for the full account.

## User guide

[`docs/user-guide/`](../user-guide/README.md) has task-oriented walkthroughs, one file per user
flow (not per front end) — real captured output, never hand-typed mockups. CLI transcripts are real
stdin/stdout capture; TUI screenshots come from `DevTerm.Console.Tests.ScreenshotTests`/
`TuiScreenshot`, which renders the real Terminal.Gui screen buffer to a PNG (each cell's actual
color, via `System.Drawing.Common`) rather than the plain-text `TuiTestRunner.DumpBuffer()` capture
used for earlier assertions-only tests; WPF screenshots come from `DevTerm.Wpf.Tests.ScreenshotTests`/
`WpfScreenshot`, a `RenderTargetBitmap` capture of a real, off-screen-but-actually-shown `Window` —
confirmed empirically that a `Window` only ever `Measure`d/`Arrange`d (never shown, even off-screen)
renders as a blank image, so `WpfScreenshot.ShowOffScreen` moves the window off any real monitor and
calls `Show()` rather than skipping it. See `.claude/skills/docs-sync/SKILL.md` for the "screenshots
are tests, not manual chores" workflow this implies.
