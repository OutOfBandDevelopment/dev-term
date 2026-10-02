# 064: View > Echo Sent Commands doesn't echo a device-profile control panel's button/field sends

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
| **Confidence** | Confirmed (code read end to end, both front ends) |
| **Area** | DevTerm.Console (ControlPanelMode), DevTerm.Wpf (ControlPanelWindow) |
| **Created** | 2026-10-01 |
| **Found at commit** | `2d7c640a83540f5c5255e2b47dca1a00a940ccf1` (`dev/hardware-review`) |
| **Found by** | User report, investigated interactively |

## Where
- `src/DevTerm.Console/ControlPanelMode.cs:1065-1082` (`Invoke`) — the TUI control panel's button/field send path
- `src/DevTerm.Wpf/ControlPanelWindow.xaml.cs:682-699` (`Invoke`) — the WPF equivalent
- Contrast with the typed-SendBox path, which *does* echo:
  - `src/DevTerm.Console/TuiMode.cs:192-199` (`echoSentCommands` local + doc comment), `:710-714` (menu toggle),
    `:1172` (`onSending` callback passed into `SendAsync`), `:1339-1346` (`SendAsync`'s doc comment: "echo for
    View > Echo Sent Commands")
  - `src/DevTerm.Wpf/MainWindow.xaml.cs:38-41` (`_echoSentCommands` field + doc comment), `:304`
    (`EchoSentCommandsMenuItem_Click`), `:722-730` (the guarded `AppendOutput(..., "Out> ...", OutputKind.Sent)`
    call)
  - `src/DevTerm.Wpf/OutputLine.cs:15` (`OutputKind.Sent`'s doc comment: "An echoed sent command (`Out> text`),
    shown only when enabled via View > Echo Sent Commands" — not scoped to "typed commands only")

## What happens
`View > Echo Sent Commands` is a per-window toggle (`_echoSentCommands` in `MainWindow.xaml.cs`,
`echoSentCommands` in `TuiMode.BuildWindow`) documented as echoing every sent command as an `Out> text` line
(`OutputLine.cs:15`). In practice it is wired into exactly one send path: the typed SendBox. `TuiMode.cs:1172`
passes an `onSending` callback into the shared `SendAsync` helper only when `echoSentCommands` is true;
`MainWindow.xaml.cs:722-730`'s typed-send handler does the same inline. Both call `AppendOutput` with the
formatted `Out> ...` text right after a successful encode.

A device-profile control panel's button/toggle/field controls send through a completely different path:
`ControlPanelMode.Invoke` (TUI) and `ControlPanelWindow.Invoke` (WPF) call `IControlSurface.InvokeAsync(commandId,
value)` directly (which itself calls `Session.SendAsync` inside `ScpiControlSurface`/`ManifestControlSurface`).
Neither `Invoke` method takes or calls anything resembling an echo callback — each only reports a *failure*
(TUI: `MessageBox.ErrorQuery`; WPF: a `StatusText.Text` line), with no output-pane line at all on the success
path. So the echo toggle has zero effect on anything sent from a control panel, regardless of whether it's on
or off.

## Failure scenario
1. Enable `View > Echo Sent Commands`.
2. Open a device profile's control panel (SCPI or manifest-backed) and click any button, e.g. an "Identify"
   command mapped to `*IDN?`.
3. No `Out> *IDN?` line appears in the output pane — only the device's reply (if any) shows.
4. Close the panel, type `*IDN?` into the SendBox and press Enter: the expected `Out> *IDN?` line does appear.

The same toggle produces inconsistent behavior for the same outgoing command depending only on which UI
surface sent it — a user who enabled the setting to get a full send/receive transcript silently loses every
control-panel-triggered line from that transcript.

## Suggested fix
Thread an echo callback into the control-panel send path, mirroring `TuiMode.SendAsync`'s `onSending` parameter:
- Give `ControlPanelMode.Invoke`/`ControlPanelWindow.Invoke` access to the owning window's echo flag and an
  append-output delegate (both are already available to their callers — `ControlPanelMode.BuildWindow` and
  `ControlPanelWindow`'s constructor — just not passed through to `Invoke`).
- Format the echoed text from the same source the "Sends: <preview>" footer already uses
  (`ICommandPreview.PreviewCommand(commandId, value)` — `ControlPanelMode.cs`'s `DescribeSends`/
  `ControlPanelWindow.xaml.cs`'s equivalent), so the echoed line matches exactly what was actually put on the
  wire, consistent with how `TypedInput.FormatForEcho` does it for the SendBox path.
- Call it right after `InvokeAsync` is confirmed to have started sending (same point the preview text is
  already computed), not before — a validation failure during encode should not produce a misleading echo line.

## Tests to add
- TUI: a control-panel button invoke with `Echo Sent Commands` enabled asserts an `Out> ...` line is appended
  to the window's output; with it disabled, asserts no such line appears.
- WPF: the same two cases against `ControlPanelWindow`.
