# 065: HP-GL plots with any pause longer than the idle timeout are split into multiple Stream Monitor captures

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Won't fix |
| **Confidence** | Confirmed (code read end to end; reproduced live against real hardware) |
| **Area** | DevTerm.Core (StreamContent: StreamContentEndFinder, StreamContentWatcher) |
| **Created** | 2026-10-02 |
| **Found at commit** | `d68b2ba297fdb2cb7e6501531ccb71a97e1e60eb` (`dev/hardware-review`) |
| **Found by** | User report, bench session against real hardware |

## Where
- `src/DevTerm.Core/StreamContent/StreamContentEndFinder.cs:13-14` (class doc comment) and `:58`
  (`For`'s final line) — HP-GL is explicitly given no end finder:
  > "HP-GL, TIFF, a bare-reset PCL job and unrecognized data have no reliable in-band end and get no
  > finder (`For` returns `null`); those end on idle instead."
  ```csharp
  return kind == StreamContentKind.Pcl ? new PclEndFinder() : null;
  ```
- `src/DevTerm.Core/StreamContent/StreamContentWatcher.cs:15` — `StreamContentWatcherOptions.IdleTimeout`
  defaults to `TimeSpan.FromSeconds(2)`.
- `src/DevTerm.Core/StreamContent/StreamContentWatcher.cs:363-382` (`OnIdleTimer`) — the only mechanism
  that ever closes a capture that has no in-band end finder; it fires `Complete(StreamCaptureEnd.IdleTimeout)`
  purely because no new bytes arrived for `IdleTimeout`, with no knowledge of whether the plot itself is
  actually finished.

## What happens
`StreamContentEndFinder.For(StreamContentKind.Hpgl)` returns `null` by design — HP-GL has no reliable
in-band terminator the way PNG has `IEND` or PostScript has `%%EOF`/Ctrl-D, so a capture of that kind can
only ever end by idle timeout (2 seconds of silence) or the size cap. A real plotter/scope HP-GL output,
though, is not one continuous burst: a pen-up/pen-down sequence, a multi-pass vector fill, or (as seen here)
a scope's own internal processing between plot segments can easily produce a legitimate pause longer than
2 seconds while the plot is still in progress. `StreamContentWatcher.OnIdleTimer` has no way to distinguish
"the device paused mid-plot" from "the plot is done" for a kind with no end finder, so it closes the capture
at the first pause past the threshold — and the *next* burst of HP-GL bytes starts a brand-new capture from
scratch (`StreamContentWatcher`'s normal sniff-and-begin path), rather than being appended to the one already
in progress.

## Failure scenario
1. Connect to a Tektronix 2230 (oscilloscope with HP-GL plot output) over TCP at `192.168.0.108`. This
   unit's serial-to-Ethernet bridge runs the link at **4800 baud** — roughly 480 bytes/sec at 8-N-1 — so
   even a modest amount of per-segment internal processing on the scope's side stretches real wall-clock
   gaps between bytes far more than it would at a typical 9600+ baud bench-instrument link.
2. Open the Stream Monitor and trigger a "Plot Start" on the scope (a single logical plot job).
3. The scope's HP-GL stream contains at least one internal pause longer than the 2-second idle timeout
   (observed live on this device/session) — plausibly *because of*, not just coincidentally alongside, the
   4800 baud rate: at this speed the generic 2s default is a much easier threshold to cross than it would
   be on a faster link, even with no unusual vendor-side delay.
4. Instead of one Stream Monitor capture for the one plot, the monitor shows **multiple separate HP-GL
   captures** — the plot was split wherever a pause happened to exceed the timeout, with no way for the
   user to tell from the UI that they're fragments of a single job rather than genuinely separate plots.

## Suggested fix
HP-GL has no universal single-byte/short-sequence terminator, but it does have idiomatic ending content
worth detecting instead of leaving every capture to timeout alone:
- Most HP-GL plot jobs end with a `PU;SP0;` (pen up, select pen 0) or a bare `SP;`/`SP0;` sequence, and/or
  a trailing device-control terminator (`\x1B.)` / `\x1B.Z` "end plot" escapes) that real plotters commonly
  emit — recognizing one of these in-band would let a HpGlEndFinder close the capture on real content the
  same way the other formats do, instead of unconditionally returning `null`.
- Short of a full in-band finder, a format-specific idle timeout (longer than the generic 2s default for
  `StreamContentKind.Hpgl` specifically) would at least reduce false splits without a code-level content
  parser, as a cheaper partial mitigation. Given this specific unit's 4800 baud link, a fixed-but-longer
  HP-GL timeout (e.g. 5-10s) would help but is still just a bigger version of the same guess; a timeout
  that scales with the *transport's* actual baud rate (available from `SerialTransportOptions`/
  `Rfc2217TransportOptions` where the link is serial-like — not meaningful for a plain TCP/USBTMC/HID
  session with no baud concept) would track "how long is a plausible processing gap at this link speed"
  more directly than any single fixed constant, generic or HP-GL-specific.
- Either fix needs real-world HP-GL samples from more than one device (this Tek 2230, plus whatever
  plotters/profiles already exist) before picking a terminator sequence to trust, since HP-GL dialects vary
  by vendor.

## Tests to add
- `StreamContentEndFinderTests`: if a terminator-based `HpGlEndFinder` is added, round-trip tests for the
  chosen terminator sequence(s), including one arriving split across two reads (matching every other
  finder's incremental-call contract).
- `StreamContentWatcherTests`: an HP-GL capture with an internal pause shorter than the new HP-GL-specific
  idle timeout (or past a real in-band terminator) stays one capture; one genuinely idle past the timeout
  still closes as `StreamCaptureEnd.IdleTimeout` as today.

## Resolution
Won't fix, 2026-10-02: no longer reproduces. With the Tektronix 2230 bridge at 9600 baud, a 175 ms write delay and a 512-character read buffer ([068](068-tek2230-bridge-runs-at-4800-baud.md)), the user captured a complete HP-GL plot as a single Stream Monitor capture and converted it to SVG (it renders correctly in the WPF preview; sample kept at `tests/DevTerm.Wpf.Tests/Samples/tek2230-plot.hpgl`). The split was a symptom of the 4800 baud link, as the report suspected, not of a wrong default. The 2 s `IdleTimeout` and the lack of an in-band HP-GL end finder are unchanged, so a genuinely slower link or a plot with a long internal pause could still split; reopen with a new capture if that happens.

**Mitigation worth knowing (from the user, 2026-10-02):** the idle gap the Stream Monitor sees depends on how often the bridge forwards bytes. A bridge that buffers up to N bytes before sending a packet shows long silences on a slow link; shrinking that buffer (the .107 read buffer is now 512) makes packets arrive sooner, so the gaps stay under the 2 s `IdleTimeout` without changing dev-term. If a plot splits again, check the bridge's packet length/gap settings before touching the timeout.

**Update, 2026-10-03:** it did happen again, on the Tektronix 2230 bridge at 192.168.0.108 left at 4800 baud; tracked and fixed in [070](070-hpgl-split-by-idle-timer-at-4800-baud.md).
