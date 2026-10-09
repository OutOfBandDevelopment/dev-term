# 062: WPF Up/Down history recall stops working on SendBox after clicking "Send"

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Fixed |
| **Confidence** | Confirmed (read end-to-end; follows directly from WPF's standard focus-on-click behavior for a focusable `Control`, which `MainWindow.xaml`'s "Send" `Button` does not opt out of) |
| **Area** | DevTerm.Wpf (MainWindow) |
| **Created** | 2026-09-30 |
| **Found at commit** | `9a3814420d823a553eec5001375cfba7ac697cbb` (`dev/hardware-review`) |
| **Found by** | User report ("the up/down keys do not always work on the send message combo box") |

## Where
- `src/DevTerm.Wpf/MainWindow.xaml:79-80` — the "Send" `Button` (`IsDefault="True" Click="Send_Click"`), a plain
  focusable `Control` with no `Focusable="False"` override.
- `src/DevTerm.Wpf/MainWindow.xaml.cs` (`Send_Click`, `SendCurrentInputAsync`, before this fix) — neither returned
  keyboard focus to `SendBox` after a send.
- `src/DevTerm.Wpf/MainWindow.xaml.cs` (`SendBox_PreviewKeyDown` / `HandleSendBoxKey`) — the Up/Down history recall
  this bug silently defeats; attached to `SendBox`'s own `PreviewKeyDown`, so it only ever fires for a key press
  that tunnels through `SendBox` itself.
- `src/DevTerm.Configuration/SendHistory.cs` — unaffected; read in full while investigating and ruled out (no
  `ResetCursor()` call anywhere in `MainWindow.xaml.cs` that could be stomping the cursor instead).

## What happens
`SendBox_PreviewKeyDown` only runs for a `KeyEventArgs` that tunnels through `SendBox` on its way to whichever
element currently has keyboard focus — WPF only tunnels a `Preview*` event down the ancestor chain of the focused
element, never to an unrelated sibling. Clicking the "Send" button (`MainWindow.xaml:79`, a focusable `Control`
with no override) moves keyboard focus to that button, which is standard, undocumented-as-a-gotcha WPF behavior
for any focusable control clicked with the mouse. Before this fix, nothing in `Send_Click` → `SendCurrentInputAsync`
ever moved focus back to `SendBox` afterward (confirmed by reading every line of both methods and grepping the
whole file for `SendBox.Focus`/`Keyboard.Focus`/`FocusManager` — the only existing call was in `ConnectAsync`,
right after a successful connect). So after the *first* mouse-driven send, Up/Down presses tunnel through the
Send button (or wherever focus is) instead of `SendBox`, and the history recall silently does nothing — matching
the report precisely: it works right after connecting or after clicking into the box by hand, and stops working
right after clicking "Send."

Pressing Enter in `SendBox` instead of clicking "Send" doesn't trigger this, since Enter never moves focus away
from the box — which is why the bug is intermittent rather than constant, and why it was easy to read past.

## Failure scenario
1. Connect (focus starts on `SendBox`, set by `ConnectAsync`).
2. Type a line, click "Send" with the mouse (not Enter). Focus moves to the "Send" button.
3. Press Up, expecting the just-sent line back. Nothing happens — the key press never reaches `SendBox`.
4. Click back into `SendBox` by hand; Up/Down work again until the next mouse-driven send.

## Suggested fix
Return focus to `SendBox` whenever `SendCurrentInputAsync` finishes, regardless of which exit path it takes
(empty input, a rejected parse, not connected, a successful send, or a send that throws) — mirroring the existing
`ConnectAsync` → `SendBox.Focus()` precedent. Implemented as a `try`/`finally` wrapping the method's body.

## Tests to add
A regression test needs a really-shown window (`WpfScreenshot.ShowOffScreen`, not the `ConnectAsync`-direct-call
convention most of `MainWindowTests` uses) because `IsKeyboardFocused`/`IsKeyboardFocusWithin` only reflect
reality once there's a real `PresentationSource` — same reasoning as `UiLayoutReviewTests`'s own focus-visual
checks. Added `MainWindowSendBoxFocusTests`:
- `SendCurrentInputAsync_RestoresFocusToSendBox_SoArrowKeysKeepRecallingHistory` — moves focus to `ParserBox`
  (simulating the Send-button click's side effect), sends, asserts `SendBox.IsKeyboardFocusWithin`.
- `SendCurrentInputAsync_WithEmptyInput_StillRestoresFocusToSendBox` — same, for the empty-input early-return path.

Both failed before the fix (focus stayed on `ParserBox`) and pass after. Note: `SendBox.IsKeyboardFocused`
itself stays `false` even after a successful `SendBox.Focus()`, because `SendBox` is an editable `ComboBox` and
WPF delegates actual keyboard focus to its internal text-box part — confirmed empirically (both tests failed on
that property despite the production fix being correct) before switching the assertion to
`IsKeyboardFocusWithin`, which is also what matters for the real behavior being tested: `SendBox_PreviewKeyDown`
only needs focus somewhere inside `SendBox`'s own visual tree to tunnel through it.

## Resolution
Fixed on 2026-09-30 on `dev/hardware-review`: wrapped `MainWindow.SendCurrentInputAsync`'s body in a
`try`/`finally` that calls `SendBox.Focus()` in `finally`, so focus returns to the send box after every call
regardless of which path it takes. Regression tests:
`MainWindowSendBoxFocusTests.SendCurrentInputAsync_RestoresFocusToSendBox_SoArrowKeysKeepRecallingHistory` and
`...WithEmptyInput_StillRestoresFocusToSendBox`.

Resolution recorded in commit `babdf36` (backfilled 2026-10-09 from git history).
