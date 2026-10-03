# 070: HP-GL from the Tektronix 2230 at 4800 baud is still split into several captures

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Fixed |
| **Confidence** | Confirmed (saved fragments inspected; idle-timer path read end to end); not yet re-run on the .108 |
| **Area** | DevTerm.Core (StreamContent: StreamContentWatcher, StreamContentEndFinder), Stream Monitor |
| **Created** | 2026-10-03 |
| **Found at commit** | `898bdf947c1d2cae33be9e0dee6bb67112d8007b` (`dev/hardware-review`) |
| **Found by** | User report with the saved `.hpgl` pieces from 192.168.0.108 |

## Where
- `StreamContentWatcher` idle timer (`OnIdleTimer`, 2 s `IdleTimeout`) and `StreamContentEndFinder.For(StreamContentKind.Hpgl)` (no finder).

## What happens
Recurrence of [065](065-hpgl-no-end-detection-splits-one-plot.md), which was closed as Won't fix because a 9600 baud link no longer split. The .108 bridge still runs at 4800 baud, where a stall between bytes longer than 2 s is routine. The user's saved pieces break in the middle of a command (`...PD648,4` then `32;PU684,...`), and they are "really one continuous file just broken into segments".

## Failure scenario
Start a plot on the 2230 through the 4800 baud bridge. The idle timer fires during a stall, closes the capture mid-command, and the rest of the plot becomes a new capture, each saved and converted separately.

## Suggested fix
Give HP-GL an in-band end (its own pen-stow `SP0;`), a longer HP-GL-specific idle wait, and make the general idle configurable per profile. These were the options 065's report already listed.

## Tests to add
`StreamContentWatcherTests`: an HP-GL capture ends at `SP0;` and excludes what follows (`READY;`); one without `SP0;` survives a gap longer than the general idle and is cut after the HP-GL idle; `StreamIdleTimeoutMs` is honoured.

## Related
[065](065-hpgl-no-end-detection-splits-one-plot.md), [068](068-tek2230-bridge-runs-at-4800-baud.md).

## Resolution
Fixed in `afaf02e06b030e1d1843baf8599ac2773fe0e865` on 2026-10-03: HP-GL ends at `SP0;`, an HP-GL capture without it waits `HpglIdleTimeout` (10 s, never below the general idle), and `StreamIdleTimeoutMs` makes the general idle configurable (default 2000). Regression tests: the HP-GL cases in `StreamContentWatcherTests`. Not verified against the .108 yet. Details: `docs/changes/2026-10-03.md`.
