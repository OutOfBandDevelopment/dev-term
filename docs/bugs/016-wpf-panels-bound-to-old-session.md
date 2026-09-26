# 016: WPF control panels stay bound to the old session after a profile switch

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Open |
| **Confidence** | Confirmed |
| **Area** | WPF (MainWindow, control panels) |
| **Found** | 2026-09-26 code review of `main` @ `42758db` (branch `dev/review-code`); static review, not yet reproduced |

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
