# 016: WPF control panels stay bound to the old session after a profile switch

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Fixed |
| **Confidence** | Confirmed |
| **Area** | WPF (MainWindow, control panels) |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Wpf/MainWindow.xaml.cs:343-420, 554-569`; `SwitchProfileAsync` at `:631-642`

## What happens
Panels are non-modal and hold `new XControlSurface(_session)` plus the old catalog's presenter. `SwitchProfileAsync`
disposes that session and never closes or rebinds the open panels. The panel window also keeps the old catalog alive.

Related: `DetectAndOpenScpiInstrumentAsync` captures `structuredSource` from the old catalog but opens the panel with
the new `_session` if a switch happens during the `*IDN?` wait, so replies aren't correlated.

## Failure scenario
Open a control panel, then switch profile in Device Profiles. The panel's buttons fail with "Command failed"
(disposed transport) and its indicators freeze silently.

## Suggested fix
Track open panels and close them on a switch (or rebind them to the new session and catalog).

## Tests to add
After a profile switch, an open panel is closed (or works against the new session).

## Resolution
Fixed in `dev/fix-bugs` on 2026-09-26: `MainWindow` now tracks every open control panel window
(`_openControlPanels`, populated by a `TrackControlPanel` helper wired into all 7 panel-opening call
sites: K8055, Busylight, RadexOne, ZoomH4n, De5000, the SCPI instrument panel, and a device manifest
panel) and closes each of them from `SwitchProfileAsync` right before the old session is disposed,
rather than leaving them bound to a session that's about to go away. The related SCPI auto-detect
race is fixed too: `DetectAndOpenScpiInstrumentAsync` now captures the session it started against and
bails out without opening a panel if `_session` no longer matches by the time the `*IDN?` wait
completes, instead of pairing the new `_session` with the old catalog's `structuredSource`.
Regression tests: `MainWindowSwitchProfileTests.SwitchProfileAsync_WithAnOpenControlPanel_ClosesItInsteadOfLeavingItBoundToTheOldSession`,
`SwitchProfileAsync_DuringAnInFlightScpiAutoDetect_PreventsOpeningAPanelAgainstTheNewSession`.
