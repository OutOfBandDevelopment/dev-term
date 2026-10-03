# 069: TDS2024 `HARDCopy:FORMat LASERJET` and `PCX` produce no Stream Monitor capture

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
| **Confidence** | Reproduced (real TDS2024, two runs; cause not yet isolated) |
| **Area** | `StreamContentWatcher` / `StreamContentSniffer` (Stream Monitor), TDS2024 serial bridge |
| **Created** | 2026-10-02 |
| **Found at commit** | `795b54c2ea6465d85af225c91c2b5df53adfde90` (`dev/hardware-review`) |
| **Found by** | failing test `RealHardwareStreamMonitorTests.Tds2024_HardCopy_OtherFormats_AreCaptured` ("LASERJET", "PCX") |

## Where
- `tests/DevTerm.Console.Tests/RealHardwareStreamMonitorTests.cs` (`Tds2024_HardCopy_OtherFormats_AreCaptured`).
- Stream Monitor detection: `StreamContentWatcher`, `StreamContentSniffer`.

## What happens
After `HARDCopy:FORMat <fmt>` then `HARDCopy START` (19200 baud bridge, TCP 192.168.0.110:23, 50 ms write pacing):

| Format | Result |
|---|---|
| EPSIMAGE | Captured: 25,526 bytes, "PostScript document", complete. Ghostscript renders it correctly. |
| TIFF | Captured: 13,260 bytes, "TIFF image" (`MM\0*`), ended by idle timeout. |
| LASERJET | No `CaptureAdded` within 5 minutes. |
| PCX | No `CaptureAdded` within 5 minutes. |

The scope itself stays healthy: it answers `*IDN?` afterwards and `HARDCopy:FORMat?` reads back `BMP`.

## Failure scenario
Selecting LASERJET (PCL) or PCX on the scope and starting a hardcopy yields nothing in the Stream Monitor. It is not known whether the scope sent no bytes, or sent bytes the sniffer does not classify (PCL starts with an ESC sequence; PCX with `0x0A`, which is weak to sniff), so the watcher never starts a capture.

## Suggested fix
First isolate the cause: capture the raw bytes for LASERJET/PCX with a `RawPresenter` and no monitor (or the Playback/log path) to see whether anything arrives. If bytes arrive, add PCL (`ESC E` / `ESC %-12345X`) and PCX signatures to the sniffer, or let the user force a kind. If nothing arrives, it is a scope limitation to document in `docs/devices/`.

## Tests to add
A sniffer unit test for the real LASERJET/PCX heads once captured; keep the real-hardware case, which then asserts the kind.
