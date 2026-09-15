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
  tests, serialization round-trips (`DevTerm.UiDefinitions`/`DevTerm.DeviceManifests`), and WPF
  `MainWindow` logic tests driven in-process against a `FakeTransport` (see
  `DevTerm.Wpf.Tests.MainWindowTests`) rather than a real connection.
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

## Not yet built

- Terminal.Gui (TUI) automation — Terminal.Gui v2.5.0 has internal test-support types
  (`TestInputSource`, `ITestableInput<T>`, `IOutputBuffer`) suggesting a headless driver is
  possible, but wiring one hasn't been investigated in depth yet.
- A user guide with real screenshots for CLI/TUI/WPF (`docs/user-guide/`) — the WPF harness here
  could double as a screenshot generator (`RenderTargetBitmap` against a real, laid-out
  `MainWindow`), not yet built.
