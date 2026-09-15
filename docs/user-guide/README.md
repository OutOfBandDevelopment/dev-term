# User Guide

Task-oriented walkthroughs of dev-term's front ends, one file per user flow. Unlike
[`docs/design/`](../design/README.md) (intent and rationale for people building dev-term), these
describe what you actually see and type when *using* it.

Every screenshot/transcript on these pages is real captured output from the actual built app — a
real Terminal.Gui screen buffer read back through `DevTerm.Console.Tests.TuiTestRunner.DumpBuffer()`
for the TUI, and a real stdin/stdout transcript for the CLI (see
[`docs/design/testing.md`](../design/testing.md) for how those harnesses work) — not hand-typed
mockups. Where a device is shown responding, it's either a real device or a small throwaway stand-in
server that speaks the exact reply the real device gives (e.g. `ID TEK/2230,V81.1,VERS:14` — the
project's own Tektronix 2230 test device's real reply).

- [CLI](cli.md) — the scriptable/interactive command-line mode.
- [TUI](tui.md) — the full-screen terminal UI, the console app's default mode.
- [WPF](wpf.md) — the graphical Windows desktop app. **Not written yet** — deferred, since its
  screenshot generation is basically the same `RenderTargetBitmap`-against-a-real-window approach
  already proven for the TUI (just against WPF's own render target instead of a Terminal.Gui screen
  buffer), not a new investigation.
