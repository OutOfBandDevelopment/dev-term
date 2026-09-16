# User Guide

Task-oriented walkthroughs of dev-term, one file per user flow — not per front end. Where a flow
applies to more than one front end (CLI/TUI/WPF), that file shows all of them together, so you can
see how the same action looks/works everywhere at a glance. Unlike
[`docs/design/`](../design/README.md) (intent and rationale for people building dev-term) or
[`docs/specs/`](../specs/README.md) (the precise per-screen field/action/state reference), these
pages are about what you actually see and do when *using* dev-term.

Every screenshot/transcript on these pages is real captured output from the actual built app, not
hand-typed mockups or hand-drawn screenshots:

- **CLI**: a real stdin/stdout transcript from the built console app.
- **TUI**: a real Terminal.Gui screen buffer, headlessly rendered and captured to PNG (each cell's
  actual color) by `DevTerm.Console.Tests.ScreenshotTests`/`TuiScreenshot`.
- **WPF**: a real, off-screen-but-actually-shown `Window` rendered to PNG via `RenderTargetBitmap`
  by `DevTerm.Wpf.Tests.ScreenshotTests`/`WpfScreenshot`.

See [`docs/design/testing.md`](../design/testing.md) for how these harnesses work. Where a device is
shown responding, it's either a real device or a small throwaway stand-in server/fake transport that
speaks the exact reply the real device gives (e.g. `ID TEK/2230,V81.1,VERS:14` — the project's own
Tektronix 2230 test device's real reply). **Re-run the relevant `ScreenshotTests` class and re-embed
its output whenever a screen's layout changes** — see `.claude/skills/docs-sync/SKILL.md` and that
test class's own doc comment.

## Flows

- [Connecting to a device](connecting.md) — CLI flags/discovery/errors, and the shared Connection
  Editor screen (TUI/WPF) shown at startup when no valid connection is configured.
- [Managing connection profiles](managing-profiles.md) — save, load, delete, refresh, import,
  export, from the same Connection Editor screen, any time via **File > Device Profiles...**.
- [Sending commands and viewing replies](sending-and-receiving.md) — the core interactive loop, once
  connected, across all three front ends.
- [Connecting and disconnecting without restarting](connect-disconnect.md) — the **File >
  Connect/Disconnect** toggle in TUI/WPF, distinct from switching profiles.

For the precise field-by-field/action-by-action reference behind these screens, see
[`docs/specs/`](../specs/README.md).
