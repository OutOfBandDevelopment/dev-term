# 057: A serial read may never notice an unplugged adapter

| | |
|---|---|
| **Severity** | Low |
| **Status** | Fixed |
| **Confidence** | Plausible |
| **Area** | DevTerm.Transports.Serial |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Transports.Serial/SerialPortReadStream.cs:31-47`

## What happens
On a USB-serial unplug, `DataReceived` never fires, so the pump waits forever; the loss only surfaces on the next write.

## Suggested fix
Also listen for `SerialPort.ErrorReceived`/`PinChanged`, or poll `IsOpen` on an interval while waiting. Verify
against real hardware first (CLAUDE.md's serial cancellation note).

## Resolution
Fixed on 2026-09-29 on `dev/fix-bugs`:

- **Plausible → confirmed by code reading, then verified against real hardware.** Reading
  `SerialPortReadStream.ReadAsync` (`src/DevTerm.Transports.Serial/SerialPortReadStream.cs`)
  end to end confirmed the report's claim exactly: the wait had only one wake source
  (`port.DataReceived`), with no `ErrorReceived`/`PinChanged` subscription and no polling fallback
  of any kind, so a `DataReceived` that never fires — a physical unplug with no bytes already in
  flight is exactly that case — waits forever. A real Radex One is attached to this machine on
  COM8 (the same device `RealHardwareRadexOneTests` already exercises), which made it possible to
  verify the fix against real hardware rather than relying on code-reading alone — see the
  regression test below.
- **Fix.** `ReadAsync` now also subscribes `SerialPort.ErrorReceived` as a second, cheap wake
  source alongside `DataReceived`, and — the fix that actually matters for a silent physical
  unplug — polls `port.BytesToRead` on a 1-second interval while it waits, via
  `Task.WhenAny(tcs.Task, Task.Delay(...))` in a loop instead of a single unconditional
  `await tcs.Task`. `PinChanged` was left out: it fires for control-line transitions (CTS/DSR/etc.),
  not for the port disappearing.
- **A caveat found while designing the fix, not in the original report: `IsOpen` (the report's own
  other suggested poll target) would not actually have detected this.** `SerialPort.IsOpen` only
  reflects whether `Close()`/`Dispose()` was called on the object — it does not flip to `false` when
  the underlying device is physically removed while still "open" from the .NET object's point of
  view. `BytesToRead` is the poll target that works: it's a property access that touches the real
  device handle, so it throws (the same way the eventual `Read()` call already would) once the
  device is genuinely gone, which is what actually ends the pump's wait instead of leaving it silent.
- **Regression test, run against real hardware (not just reasoned about):**
  `RealHardwareSerialPortReadStreamTests.RealDevice_PortGoesAwayWhileWaitingForData_ReadAsyncNoticesInsteadOfHangingForever`
  (`tests/DevTerm.Transports.Serial.Tests/`, tagged `Integration`/`Serial`/`Hardware`/`BugRegression`,
  opt-in via `dotnet test --settings devterm.runsettings`, `Assert.Inconclusive` otherwise). It opens
  the real COM8 port directly (bypassing the Radex One's own protocol entirely — only the port
  itself matters here), starts `SerialPortReadStream.ReadAsync` with nothing queried so no bytes are
  pending, waits 300ms to confirm the read is genuinely still waiting, then calls `port.Close()` to
  simulate the device going away with no data in flight (the same "no `DataReceived`/`ErrorReceived`
  ever fires" gap a physical unplug leaves) and asserts `ReadAsync` ends within 5 seconds instead of
  hanging. Run against the pre-fix code (stashed just the fix, kept the test), it failed exactly as
  expected — `ReadAsync` was still waiting past the 5-second bound. Restored and re-run against the
  fix, it passed in ~1 second, matching the 1-second poll interval. This is the first real-hardware
  serial test in the repo (`docs/design/testing.md` previously noted none existed yet for serial/COM
  — its `RealDeviceReachability.IsSerialPortAvailable` preflight already existed and needed no
  changes).
- Full solution `dotnet build` and `dotnet test --filter "TestCategory=Unit"` both clean afterward —
  no regressions.

Regression test: `RealHardwareSerialPortReadStreamTests.RealDevice_PortGoesAwayWhileWaitingForData_ReadAsyncNoticesInsteadOfHangingForever`.
