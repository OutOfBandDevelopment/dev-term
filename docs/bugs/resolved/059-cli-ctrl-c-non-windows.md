# 059: CLI Ctrl+C may not interrupt a pending read on Linux/macOS

| | |
|---|---|
| **Severity** | Low |
| **Status** | Fixed |
| **Confidence** | Plausible |
| **Area** | CLI (CliMode) |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Console/CliMode.cs:75-78`

## What happens
`Console.In` is a `SyncTextReader`, whose `ReadLineAsync(ct)` checks the token once and then reads synchronously, so
it doesn't interrupt a pending read. On Windows `ReadLine` returns null after Ctrl+C anyway. On Linux/macOS, Ctrl+C
with `e.Cancel = true` probably leaves the loop blocked until Enter. The code comment says otherwise.

## Suggested fix
Verify on Linux; if confirmed, don't cancel the default Ctrl+C handling, or read stdin on a dedicated thread.

## Resolution
Reproduced first (per the fixing lifecycle for a `Plausible` report), and confirmed against a real Linux kernel,
not just theorized: installed a .NET SDK inside a WSL2 Ubuntu environment and ran a minimal program isolating
exactly this mechanism (`Console.CancelKeyPress` + `Console.In.ReadLineAsync(CancellationToken)`), driven with a
real `kill -INT` against a job-control-enabled (`set -m`) backgrounded foreground process reading from a
`mkfifo`-backed stdin, closely matching genuine interactive-terminal Ctrl+C delivery. Result: `CancelKeyPress`
fires promptly (well before any input was sent), but the concurrently pending `ReadLineAsync(cts.Token)` call does
**not** throw `OperationCanceledException` - it stays blocked until a real line of input arrives. This confirms the
report's core claim and disproves the code comment it quoted ("actually interrupts a pending interactive console
read"). Only Linux (via WSL2) was actually tested this way; macOS was not verified directly, though CoreCLR's
`SyncTextReader` is the same implementation on both.

Fixed in `src/DevTerm.Console/CliMode.cs` (`RunAsync`) on 2026-09-29: the loop no longer awaits
`stdin.ReadLineAsync(cts.Token)` directly. It now races that task against a genuinely cancellable
`Task.Delay(Timeout.Infinite, cts.Token)` via `Task.WhenAny`, and treats the delay task winning as a cancellation -
breaking the loop immediately regardless of whether the real read task itself ever observes the token. This
follows the suggested fix's second option ("read stdin on a dedicated thread") in spirit: rather than adding a raw
background `Thread`, it uses a standard idiom for making an uncancellable `await` behave as if it were cancellable,
without changing the injectable `TextReader` contract the existing tests already depend on (so `ScriptedStdin`,
which correctly honors cancellation via a `Channel<T>`, is unaffected - the race is a no-op for a well-behaved
reader). The abandoned real read task, if the underlying reader never completes it, is left running on a
background thread-pool wait; this doesn't block process exit, the same tradeoff already accepted elsewhere in this
codebase for `SerialPort`/`HidStream`'s own uncancellable blocking reads.

To make this testable without needing to raise a real, internally-constructed `ConsoleCancelEventArgs`, the
internal `RunAsync` overload gained a seventh, `CancellationTokenSource shutdownRequested` parameter (the existing
six-argument overload now delegates to it with a fresh one, unchanged for existing callers) - a test can call
`.Cancel()` on it directly to simulate Ctrl+C.

Regression test: `CliModeCancellationTests.CtrlC_WhileStdinNeverObservesCancellation_StillExitsPromptly`, using a
new `UncancellableStdin` fake `TextReader` whose `ReadLineAsync(CancellationToken)` mirrors `SyncTextReader`'s real
defect (never observes the token once called). Confirmed failing (5s timeout) against the pre-fix direct-await
code, passing (in ~30ms) after the fix. Full `TestCategory=Unit` run green across the whole solution (no
regressions); full solution `dotnet build` clean (0 warnings, 0 errors).

Resolution recorded in commit `babdf36` (backfilled 2026-10-09 from git history).
