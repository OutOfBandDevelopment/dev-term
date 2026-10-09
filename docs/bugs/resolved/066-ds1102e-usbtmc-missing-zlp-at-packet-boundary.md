# 066: DS1102E omits the terminating zero-length packet when a reply ends exactly on a 64-byte boundary (pyvisa-py #472)

| | |
|---|---|
| **Severity** | Low |
| **Status** | Won't fix |
| **Confidence** | Plausible (reported by an external project; not reproduced here) |
| **Area** | DevTerm.Transports.Usbtmc (UsbtmcTransport.ReadTransfer) |
| **Created** | 2026-10-02 |
| **Found at commit** | `5ce504b3e8072f6ca4e269755453c52346a6c251` (`dev/hardware-review`) |
| **Found by** | External report (pyvisa-py #472); bench sessions `docs/test/2026-09-25-18-03-06.md`, `docs/test/2026-09-29-18-06-54.md`, `docs/test/2026-10-02-07-08-35.md` |

## Where
- `src/DevTerm.Transports.Usbtmc/UsbtmcTransport.cs` (`ReadTransfer`'s continuation loop): a read that exactly fills
  the buffer keeps reading for the short packet that ends the transfer, so a missing ZLP would wait one
  `ReadTimeoutMs` and then raise a `TimeoutException`.

## What happens
pyvisa-py reports that the DS1102E sends no zero-length packet when a reply ends on a packet boundary. If true,
dev-term's read would time out on such a reply.

## Failure scenario
A reply whose total bulk-IN transfer is a multiple of 64 bytes. Not observed: normal-mode `:WAV:DATA?` is 610 bytes
and RAW/long mode 8202, and the transfer lengths those produce (N + 24) can never be a multiple of 64.

## Resolution
Won't fix, cannot reproduce (2026-10-02). Three bench sessions on a real DS1102E found no failure, and the reply
sizes the scope produces cannot land on a boundary (`docs/test/2026-10-02-07-08-35.md`). The report's other claim
(TransferSize 10 bytes short) did not match this unit either. Reopen with a captured reply that does end on a
boundary; research it then.

Resolution recorded in commit `f0fb6ff` (backfilled 2026-10-09 from git history).
