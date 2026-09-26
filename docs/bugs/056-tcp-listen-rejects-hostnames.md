# 056: TCP listen mode rejects hostnames and is IPv4-only

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
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
