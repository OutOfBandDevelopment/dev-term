# 056: TCP listen mode rejects hostnames and is IPv4-only

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
| **Confidence** | Confirmed |
| **Area** | DevTerm.Transports.Tcp |
| **Found** | 2026-09-26 code review of `main` @ `42758db` (branch `dev/review-code`); static review, not yet reproduced |

## Where
`src/DevTerm.Transports.Tcp/SystemTcpConnectionSource.cs:30`

## What happens
`IPAddress.Parse(options.Host)` throws `FormatException` for `--listen true --host localhost`, and the validator
doesn't catch it. `IPAddress.Any` also makes the listener IPv4-only.

## Suggested fix
Resolve hostnames (or validate the host as an IP address up front), and listen dual-mode (`IPAddress.IPv6Any` with
`DualMode`).
