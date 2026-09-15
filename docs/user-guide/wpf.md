# WPF

**Not written yet.** The GUI front end — see [`docs/design/frontends.md`](../design/frontends.md)
for its role relative to the [CLI](cli.md)/[TUI](tui.md).

Deferred deliberately (2026-09-15): generating real screenshots for this page needs the same kind
of investigation already done for the TUI (see [`docs/design/testing.md`](../design/testing.md)),
but the answer is expected to be simpler, not harder — `DevTerm.Wpf.Tests.MainWindowTests` already
drives a real, laid-out `MainWindow` in-process; capturing it as an image just needs
`RenderTargetBitmap` against that same real window instead of a new automation approach. Picking
this back up should reuse `MainWindowTests`' existing `StaTestRunner`/`FakeTransport` harness
directly, the way `docs/user-guide/tui.md` reused `TuiTestRunner`.
