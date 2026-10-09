# 027: BLE connect and write timeouts are documented but never used

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Fixed |
| **Confidence** | Confirmed |
| **Area** | DevTerm.Transports.Ble, DevTerm.Configuration |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Transports.Ble/BleTransportOptions.cs:25, 28`

## What happens
`ConnectTimeoutMs` and `WriteTimeoutMs` are documented as bounding a hung connect or write with a `TimeoutException`.
Nothing reads them, and `AddDevTermFrontEnd` doesn't bind them from `CliOptions`.

## Failure scenario
A peripheral that stops responding hangs Connect or a write indefinitely.

## Suggested fix
Wrap `ConnectAsync`/`WriteAsync` in a linked `CancelAfter` and bind the options; or remove them.

## Tests to add
A fake adapter that never completes: connect and write time out.

## Resolution
Fixed in `dev/fix-bugs` on 2026-09-26: `BleTransport` (`src/DevTerm.Transports.Ble/BleTransport.cs`) now wraps both
`IBleAdapter.ConnectAsync` (in `OpenAsync`) and `IBleAdapter.WriteAsync` (in `WriteAsync`) with a new private
`RunWithTimeoutAsync` helper - a `CancellationTokenSource` started with `CancelAfter(timeoutMs)`, linked with the
caller's own token, converting the resulting `OperationCanceledException` into a `TimeoutException` only when the
timeout (not the caller's token) is what fired. `OpenAsync` uses `BleTransportOptions.ConnectTimeoutMs`;
`WriteAsync` uses `WriteTimeoutMs`. `AddDevTermFrontEnd`'s `ble` branch
(`src/DevTerm.Configuration/ServiceCollectionExtensions.cs`) now binds `o.WriteTimeoutMs = cliOptions.WriteTimeoutMs;`
from the existing generic `CliOptions.WriteTimeoutMs` field, matching the Serial/Usbtmc branches. `ConnectTimeoutMs`
is left bound only to its 10000ms default, not wired to any CLI flag: it's the only software-enforced *connect*
timeout concept in the codebase (no other transport has one, and no `CliOptions` field corresponds to it), so adding
one is a separate, larger scope decision than fixing the two documented-but-inert fields this report identifies.
Confirmed with two new regression tests in `BleTransportTests`
(`OpenAsync_ConnectNeverCompletes_ThrowsTimeoutExceptionAfterConnectTimeoutMs`,
`WriteAsync_WriteNeverCompletes_ThrowsTimeoutExceptionAfterWriteTimeoutMs`, both tagged `BugRegression`), using a
mocked `IBleAdapter` whose `ConnectAsync`/`WriteAsync` never complete
(`Task.Delay(Timeout.Infinite, cancellationToken)`); confirmed to hang indefinitely against the pre-fix code (killed
manually rather than left to run) before the fix, and to fail fast with `TimeoutException` after it. `BleTransport`
depends only on the mockable `IBleAdapter` interface, unlike `WindowsBleAdapter` (see [025](025-ble-service-not-disposed.md),
[026](026-ble-writes-not-mtu-chunked.md)), so this fix is directly and fully unit-tested with no hardware-dependent
gap.

Resolution recorded in commit `babdf36` (backfilled 2026-10-09 from git history).
