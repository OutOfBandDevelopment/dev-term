# 056: TCP listen mode rejects hostnames and is IPv4-only

| | |
|---|---|
| **Severity** | Low |
| **Status** | Fixed |
| **Confidence** | Confirmed |
| **Area** | DevTerm.Transports.Tcp |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Transports.Tcp/SystemTcpConnectionSource.cs:30`

## What happens
`IPAddress.Parse(options.Host)` throws `FormatException` for `--listen true --host localhost`, and the validator
doesn't catch it. `IPAddress.Any` also makes the listener IPv4-only.

## Suggested fix
Resolve hostnames (or validate the host as an IP address up front), and listen dual-mode (`IPAddress.IPv6Any` with
`DualMode`).

## Resolution
Fixed on 2026-09-29 on `dev/fix-bugs`:

- **Hostname rejection (Confirmed → reproduced with a regression test):**
  `SystemTcpConnectionSource.AcceptAsync` (`src/DevTerm.Transports.Tcp/SystemTcpConnectionSource.cs`) now tries
  `IPAddress.TryParse(host, ...)` first and falls back to `Dns.GetHostAddressesAsync(host, cancellationToken)`,
  taking the first resolved address, instead of calling `IPAddress.Parse(options.Host)` directly. A value like
  `"localhost"` no longer throws `FormatException` — it resolves like any other hostname a client would use.
- **IPv4-only listening (Confirmed → reproduced with a regression test):** the no-host (wildcard) case now binds
  `IPAddress.IPv6Any` instead of `IPAddress.Any`, with `listener.Server.DualMode = true` set explicitly before
  `Start()` — the `TcpListener(IPAddress, int)` constructor does not enable dual-mode on its own the way the
  static `TcpListener.Create(port)` helper does, so this has to be set by hand. An IPv4 loopback peer still
  connects exactly as before; an IPv6 loopback peer, which used to be refused outright, now connects too.
- **Resolved-hostname caveat found while writing the regression test, not in the original report:** a hostname
  that resolves to more than one address (`"localhost"` commonly resolves to both `127.0.0.1` and `::1`) only
  binds whichever address `Dns.GetHostAddressesAsync` returns first — that address's family, not both. This
  isn't dual-mode the way the no-host case is; a caller wanting to accept both address families still needs to
  leave `--host` unset. Documented in `ResolveBindAddressAsync`'s doc comment rather than solved further, since
  picking a single bind address for an explicit, non-wildcard hostname is the same trade-off any TCP listener
  makes.
- The validator wasn't changed: `CliOptionsValidator`/`TcpTransportOptionsValidator` only ever required `Host` to
  be non-empty in the modes where it's meaningful, never that it parse as an IP address — correct on its own,
  since a hostname was always meant to be a legal value; the bug was `AcceptAsync` not actually resolving one.

Regression tests: `SystemTcpConnectionSourceTests.AcceptAsync_WithAHostname_ResolvesItInsteadOfThrowingFormatException`,
`SystemTcpConnectionSourceTests.AcceptAsync_WithNoHost_AcceptsAnIPv6LoopbackPeer` (plus
`AcceptAsync_WithNoHost_StillAcceptsAnIPv4LoopbackPeer` and `AcceptAsync_WithAnIpLiteralHost_StillBindsToThatAddress`
confirming no regression for the two paths that already worked).

Resolution recorded in commit `babdf36` (backfilled 2026-10-09 from git history).
