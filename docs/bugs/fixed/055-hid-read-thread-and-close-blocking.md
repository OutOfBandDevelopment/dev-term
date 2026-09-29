# 055: HID read thread can crash the process; Close blocks the calling thread

| | |
|---|---|
| **Severity** | Low |
| **Status** | Fixed |
| **Confidence** | Plausible (thread crash); confirmed (blocking) |
| **Area** | DevTerm.Transports.Hid |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Transports.Hid/HidReadStream.cs:50`; `SystemHidDevice.Close()`

## What happens
- `GetMaxInputReportLength()` runs before the `try` on a raw `Thread`, so any exception there kills the process.
- `SystemHidDevice.Close()` calls `Stop()`, which joins the read thread (up to `ReadTimeoutMs` + 1 s, about 2 s) on
  whichever thread called `CloseAsync`, usually the UI thread. `OpenAsync` also enumerates and opens synchronously.

## Suggested fix
Move the call inside the thread's `try`; run the blocking close/open work off the UI thread.

## Resolution
Fixed on 2026-09-29 on `dev/fix-bugs`:

- **Thread crash (Plausible → reproduced by code reading, not by a runnable test — see below):**
  `HidReadStream.ReadLoop()` (`src/DevTerm.Transports.Hid/HidReadStream.cs`) now wraps the whole
  method body, including the `GetMaxInputReportLength()` call that used to run outside the `try`, in
  a `try`/`catch (Exception)`/`finally`. Any exception on this raw background `Thread` — not just a
  `Read` failure — now ends the read loop gracefully (same as end-of-stream) instead of going
  unhandled and crashing the process.
- **`OpenAsync` blocking (Confirmed):** `HidTransport.OpenAsync` (`src/DevTerm.Transports.Hid/HidTransport.cs`)
  called `device.Open()` directly in its own synchronous body with no `Task.Run` — reproduced with a
  regression test (`HidTransportTests.OpenAsync_DoesNotBlockTheCallingThreadOnTheDevicesSynchronousOpen`)
  that gates a mocked `Open()` and measures how long the `OpenAsync(...)` call expression itself takes
  to return a `Task`: it blocked for the full ~5s gate before the fix. `OpenAsync` now runs `Open()`
  via `Task.Run(device.Open, CancellationToken.None)` and awaits it with `ConfigureAwait(false)`, so
  the caller (often the UI thread) is never blocked on the real, synchronous HidSharp open.
- **`CloseAsync` blocking (this report's "confirmed" claim needed correction, not a silent fix):**
  reading the current code showed `CloseAsync` already awaits `_pumpTask` with `ConfigureAwait(false)`
  before its blocking `_device.Close()` call — which, when a real suspension happens, resumes on a
  pool thread, not the caller's. The report's claim still held for one real, narrower case, though:
  if `_pumpTask` was *already complete* by the time that `await` runs (e.g. the device had already
  disconnected), `await`ing an already-completed `Task` continues synchronously on the caller's own
  thread regardless of `ConfigureAwait(false)` — that flag only chooses where a continuation resumes
  after a genuine suspension, not what happens when there wasn't one. `CloseAsync`'s `_device.Close()`
  call (which blocks on `HidReadStream.Stop()`'s bounded ~2s thread `Join`) now also runs via
  `Task.Run(device.Close, CancellationToken.None)`, removing that edge case rather than leaving it
  timing-dependent. No regression test was added for this specific edge case: reproducing it
  deterministically needs controlling exactly when the mocked pump's underlying pipe completes
  relative to `CloseAsync`'s own `await`, which is timing-sensitive to assert reliably; the fix
  removes the caller-thread-blocking risk unconditionally regardless of that timing, which is what
  matters here.
- No regression test targets `HidReadStream.ReadLoop()`'s crash path directly: it's an `internal`
  class (no `InternalsVisibleTo` exists for `DevTerm.Transports.Hid` — checked directly, unlike
  `DevTerm.Transports.Serial`, which has one for its own tests project) that wraps a real HidSharp
  `HidStream`, obtainable only from a real, already-open HID device with no interface seam — the
  same class of constraint as [054](054-ble-cancelled-connect-handler-leak.md)'s WinRT types.

Regression test: `HidTransportTests.OpenAsync_DoesNotBlockTheCallingThreadOnTheDevicesSynchronousOpen`.
