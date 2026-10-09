# Multiple sessions per window

Both front ends' main windows now hold a tab strip of `Session`s instead of exactly one — see
[`docs/specs/tui-main-screen.md`](../specs/tui-main-screen.md) and
[`docs/specs/wpf-main-window.md`](../specs/wpf-main-window.md) for the shipped behavior. This doc
designed the change: opening more than one connection at once, side by side, in the same window, in
both front ends. Queued last in `TODO.md`'s UI batch on purpose — it restructures both main windows
and touches more shared front-end code than any single item before it. All four implementation steps,
including the Open questions below (deliberately deferred to Step 4 rather than resolved up front),
are complete — see **Status**.

## Why this was blocked until now

Per-session isolation had to land first, or a second session would corrupt or leak into the first
one's state. That happened 2026-09-25, [presenters.md](presenters.md) §"Stateful presenter lifetime
vs. DI registration": every `IPresenter` and `PresenterCatalog` registration is transient, one
catalog is resolved per session, and two sessions in one process can't interleave partial frames
through a shared presenter instance any more. `DevTermSessionBuilder.Build(CliOptions)`
(`src/DevTerm.Configuration/DevTermSessionBuilder.cs`) already builds one fully independent
`(Session, PresenterCatalog, IServiceProvider)` triple from a throwaway DI container — today it's used
for live profile switching (replace the one session), and this design reuses it unchanged to *add* a
session alongside existing ones instead.

What's missing is entirely in the front ends: both `TuiMode`/`MainWindow` still hard-code one
`Session`/`PresenterCatalog`/output-list/send-history/status-line as fields on the window itself.
Multi-session support is a matter of extracting that bundle into a per-session unit and giving each
front end a way to hold several and switch between them — no core/config-layer change.

## The per-session unit: `SessionTab`

A new type (`DevTerm.Configuration`, shared by both front ends) bundles everything that's per-session
today:

```plantuml
@startuml
!include https://raw.githubusercontent.com/plantuml-stdlib/C4-PlantUML/master/C4_Component.puml

Container_Boundary(config, "DevTerm.Configuration") {
  Component(tab, "SessionTab", "record/class", "Owns one connection's Session, PresenterCatalog, IServiceProvider, SendHistory, and display state (title, output lines, status)")
  Component(builder, "DevTermSessionBuilder", "static class", "Builds an independent (Session, Catalog, Services) triple from CliOptions — unchanged")
}

Container_Boundary(tui, "DevTerm.Console") {
  Component(tuimode, "TuiMode", "Terminal.Gui window", "Owns a Tabs strip of SessionTabs; routes menu actions to the active one")
}

Container_Boundary(wpf, "DevTerm.Wpf") {
  Component(mainwindow, "MainWindow", "WPF window", "Owns a TabControl of SessionTabs; routes menu actions to the active one")
}

Rel(tab, builder, "created by")
Rel(tuimode, tab, "holds N, one active")
Rel(mainwindow, tab, "holds N, one active")
@enduml
```

`SessionTab` holds exactly what today's `TuiMode`/`MainWindow` fields already hold per connection —
this is a rename/extraction, not new state:

- `Session`, `PresenterCatalog`, `IServiceProvider` (from `DevTermSessionBuilder.Result`, or from the
  app's original startup `CliOptions`-bound host for the first/only tab — see Open questions).
- `SendHistory` (currently shared process-wide in both front ends; becomes per-tab — see Open
  questions).
- Output lines / the output pane's backing list (`OutputLine` records in WPF; the `Editor`'s text in
  TUI — each tab gets its own).
- The tab's display title, derived the same way the window title is today
  (`ConnectionDescription.Definition`, or the saved profile's name — see
  `docs/specs/tui-main-screen.md`'s **States** section) and reused as the tab label.
- Its own `Session.Disconnected` subscription, wired once at creation and disposed when the tab
  closes.

Neither `Session` nor `PresenterCatalog` themselves change — this is purely a front-end-side
grouping.

## UI shape

**WPF**: a stock `TabControl` docked above the send row, one `TabItem` per `SessionTab`, its header
bound to the tab's title (live-updating on profile switch/connect state the same way the window title
does today), `SendBox`/`ParserBox`/status bar rebuilt (or re-bound) per active tab. `OutputList` moves
from a window-level field to per-`TabItem` content.

**TUI**: Terminal.Gui v2.5.0 ships a real tab-strip control for exactly this,
`Terminal.Gui.Views.Tabs` (confirmed present via reflection against the installed 2.5.0 package,
distinct from a nonexistent "TabView" this doc initially assumed) — `View Value { get; set; }` is the
selected tab, `ValueChanged` fires on switch, `InsertTab(index, View)` adds one, and each "tab" is
itself a plain `View` (so a `SessionTab`'s output `Editor` + status line container becomes one
`Tabs`-managed subview). No hand-rolled tab strip is needed.

```plantuml
@startsalt
{
  {* File | Device | View | Help}
  {/ tek2230 | k8055 | *scope-1 }
  {
    [Connected — tcp://192.168.0.107:23                                    ]
  }
  {SI
  "[HP34401A] +1.23456E+00 VDC"
  "[dev-term] Connected to tcp://192.168.0.107:23"
  ""
  }
  Send: | "                                                              " | [Send]
}
@endsalt
```

```plantuml
@startsalt
{
  {* File | Device | Help}
  {
    { tek2230 | k8055 | *scope-1* | [+] }
  }
  {
    Output list for the active tab (scope-1) fills here
  }
  Send: | "                                    " | [Send] | Send as: [ascii|v]
   Connected — tcp://192.168.0.107:23
}
@endsalt
```

(First mockup: TUI, tabs as a row of menu-bar-adjacent labels with the active one marked. Second:
WPF, a `[+]` "New Session" tab per the common browser-tab convention. Both are illustrative, not
final — see Open questions on exact chrome.)

## Menu changes (both front ends)

- **File > New Session...** — opens the Connection Editor / Configure screen exactly as it already
  does today for a fresh startup, but the result adds a tab instead of replacing the window's only
  session. Reuses `DevTermSessionBuilder.Build` exactly as profile switching does.
- **File > Close Session** (or a tab's own close control, WPF's usual `TabItem` `×`) — closes that
  tab's `Session` and removes the tab. Closing the last remaining tab is an open question (below).
- **File > Connect/Disconnect**, **Device > ...*** menu items, **Device Profiles...** (as a live
  switch), **Start/Stop Logging**, **Stream Monitor...** all keep their current per-session behavior
  but now act on **the active tab's** session rather than the window's only one — the active-tab
  routing is the only real behavioral change to each of these; none of their own internal logic
  changes.

## Sequence: opening a second session

```plantuml
@startuml
participant "Front end window" as W
participant "New tab's SessionTab" as T
participant DevTermSessionBuilder as B
participant Session as S

W -> W : File > New Session...
W -> W : run Connection Editor, get CliOptions
W -> B : Build(cliOptions)
B --> W : (Session, Catalog, Services)
W -> T : new SessionTab(session, catalog, services, title)
W -> T : Session.AddObserver / Disconnected += ...
W -> S : OpenAsync
S --> T : Output events -> append to this tab's output list
W -> W : add tab to Tabs/TabControl, make it active
@enduml
```

## Open questions

**Decided 2026-10-03 (owner interview):** `SendHistory` is **per tab**. Closing the last tab leaves the window open and empty (no
forced exit). Logging and the Stream Monitor are **both** per tab by default **and** available as one merged, time-ordered view
across tabs (the shared timecode). Tab switching uses **Alt+Left / Alt+Right** in TUI and WPF (built 2026-10-03, alongside Ctrl+Tab). Per-tab `SendHistory`, per-tab logging and the empty window after the last tab closes were already built; the merged view is built (2026-10-03): the Stream Monitor already lists every tab's captures in one list, and View > All Sessions Log shows `MergedSessionLog`'s time-ordered raw traffic of every tab (WPF live, TUI a refreshable snapshot).
 the bullets below keep the original reasoning. New/Close tab shortcuts are built as **Ctrl+T / Ctrl+W** in both front ends (the 2026-10-08 note chose Ctrl+Shift+T/W for the TUI; the code uses plain Ctrl+T/W).

- **What stays window-scoped vs. becomes per-tab.** Candidates that are almost certainly
  window-scoped regardless of session count: theme (`View > Theme` already applies to "every open
  dev-term window", per both specs — one session tab isn't a window), the app preference file. Less
  obvious:
  - **`SendHistory`**: shared across all tabs today (one process-wide list). Per-tab is more
    consistent with "each tab is its own independent connection" but loses the convenience of recalling
    a line typed in another tab. Recommend per-tab, matching per-tab `PresenterCatalog`/output — but
    flagging since it's a visible behavior change, not just internal plumbing.
  - **Session logging**: today one log file follows one session across a live profile switch
    ([session-logging.md](session-logging.md)). With multiple tabs open at once, does "Start
    Logging..." log only the active tab, or could a user want several tabs logged to separate files
    concurrently? Recommend: logging stays a per-tab action (menu state reflects the active tab's
    logging status), so two tabs can log to two files independently — no design conflict with the
    existing per-session `SessionLogger`, just needs each tab to own its own logger instance instead
    of the window owning one.
  - **Stream Monitor**: currently one non-modal window watching "the current session," open across
    profile switches. With N tabs, does it watch only the tab active when it was opened, or follow tab
    switches, or need one Stream Monitor per tab? Recommend: scope it to the tab it was opened from
    (closing that tab closes its monitor), simplest and consistent with "one thing per session."
- **Closing the last tab.** Does the window close (matches "the window represents one connection,
  session count zero means nothing to show"), or does it stay open with zero tabs and an empty/
  disabled state (matches "File > New Session..." always being available, no forced reconnect)?
  Recommend the latter — it's what a startup connect failure already does today (window opens
  disconnected rather than exiting, per both specs' **Errors** sections) — a zero-tab window is a
  natural extension of that existing "never exit on connection state" rule, not a new exception to
  it.
- **Which tab a startup connection becomes.** `CliOptions`/a saved default profile/`--listen` etc.
  still describe exactly one connection at process start. That becomes the first tab, unchanged from
  today's single-session behavior; New Session is the only way to add more. No ambiguity here, listed
  for completeness since it's the one path that does *not* go through `DevTermSessionBuilder.Build`
  today (the startup session is built by the app's own long-lived host) — needs confirming that path
  still constructs a `SessionTab` the same shape a `DevTermSessionBuilder.Build` result does, or
  wrapping it in one.
- **Keyboard shortcuts** for New/Close/next-tab/prev-tab — not designed yet; needs picking
  conventions consistent with existing Ctrl+Q (quit)/Ctrl+T isn't used elsewhere in dev-term yet, so
  no existing collision, but should match whatever's idiomatic for each front end (WPF: Ctrl+T/Ctrl+W
  is a strong existing convention from browsers; TUI: Terminal.Gui has no strong precedent to match,
  so pick something and document it in the spec once decided).
- **Test automation impact.** Every existing `TuiMode`/`MainWindow` test that currently reaches into
  window-level `Session`/`OutputList`/etc. fields needs updating to go through the active tab instead
  — this is likely the single largest source of mechanical (not design) work in implementation, not
  called out further here since it's execution, not a design decision.

## Status

**Step 1 done.** `SessionTab` (`src/DevTerm.Configuration/SessionTab.cs`) is extracted and both
`TuiMode.BuildWindow` and `MainWindow` hold exactly one `SessionTab` (`tab`/`_tab`) instead of
separate `Session`/`PresenterCatalog`/`CliOptions` fields — a pure, behavior-preserving refactor
(verified: full solution build + full Unit test suite, 0 failures, both before and after).

**Step 2 done.** `MainWindow` now holds a list of `WindowTab` (a `SessionTab` plus its own `TabItem`,
output `ListBox`, header `TextBlock`, and event-handler delegates) instead of one; `SessionTabs`
(`TabControl`) replaces the old window-level `OutputList`, with each tab's `Session.Output`/
`Disconnected` routed to that tab's own output list rather than "whichever tab is active." File >
New Session opens the Connection Editor and adds a tab via `DevTermSessionBuilder.Build`; File >
Close Session (enabled only when more than one tab is open) closes the active tab, its session, and
any control panels it owns (tracked via `Window.Tag`). `SendHistory`/`ParserBox` are per-tab, as
recommended above. Deliberately narrowed for this step, deferred to Step 4 below rather than fully
resolved: closing the last tab is a no-op (can't reach zero tabs yet); session logging and the
Stream Monitor both stay single, window-level instances that follow "whichever tab was active when
started/opened," not yet one-per-tab. `DarkControls.xaml` gained `TabControl`/`TabItem` styles (the
stock Aero2 chrome was hard-coded light, same class of gap as every other stock control themed
there) — required for `UiLayoutReviewTests`' dark-theme cases to stay green with the new tab strip.
Verified: full solution build + full Unit test suite, 0 failures.

**Step 3 done.** `TuiMode` now holds a list of `TuiWindowTab` (a `SessionTab` plus its own `Editor`
output pane, `TextField` `SendField`, and per-tab `SendHistory`) instead of one; a
`Terminal.Gui.Views.Tabs` control replaces the old window-level `output` `Editor`, with each tab's
`Session.Output`/`Disconnected` routed to that tab's own output pane rather than "whichever tab is
active." File > New Session... opens `ConfigureMode`'s nested dialog and adds a tab via
`SessionTab.Build`; File > Close Session (enabled only when more than one tab is open) closes the
active tab, unsubscribes its handlers, and reassigns `Tabs.Value` to a remaining tab. Shared chrome
(status line, Device menu items, connection state) rebinds to whichever tab is active via
`RefreshConnectionUi(TuiWindowTab)`/`ActiveTab()`. Deliberately narrowed for this step, matching
Step 2's own narrowing and deferred to Step 4 below rather than resolved here: closing the last tab
is a no-op (can't reach zero tabs yet); session logging and the Stream Monitor both stay single,
window-level instances that follow "whichever tab was active when started/opened," not yet
one-per-tab; the "Send as" parser menu is built once rather than rebuilt per tab switch. No
`MainWindow`-style `Window.Tag`-tracked-control-panels equivalent was needed — TUI control panels
(K8055/Busylight/SCPI) are modal via a nested `app.Run`, so they block the tab they were opened
from and need no separate per-tab bookkeeping to close alongside their tab. `TuiWindowParts` gained
`TabsView`, `NewSessionMenuItem`, `CloseSessionMenuItem`, and `AllSessions` (a
`Func<IReadOnlyList<Session>>`, mirroring how `RunAsync` closes every open tab's session at
shutdown). New test coverage (`TuiModeMultiSessionTests`) drives the real menu actions end-to-end —
opening the nested `ConfigureMode` dialog via `app.Run` and clicking its Connect button, the same
nested-modal-driving technique already established for the K8055 control panel's own tests — rather
than a copy of the menu items' logic; no equivalent WPF Step 2 test file existed to model it after
(`git show --stat 447ebab` shows only `ConnectionDescriptionTests.cs` was added for that step), so
this test shape was designed independently, grounded in this codebase's own conventions. Verified:
full solution build + full Unit test suite, 0 failures.

**Step 4 done** (2026-09-30), in both front ends. Every Open question above is resolved:

- **(Superseded 2026-10-03: the Stream Monitor is now one window-level monitor watching every tab; logging stays per-tab.)** **Session logging and Stream Monitor are per-tab**, not window-level. WPF: `WindowTab.Logger`/
  `WindowTab.Monitor`; TUI: `TuiWindowTab.Logger`/`TuiWindowTab.Monitor`. Two tabs can log to two
  different files, or run two Stream Monitors, at once; closing one tab's log/monitor never touches
  another's. `TuiLogging` was rewritten from an instance-based class holding one running logger to a
  static class of pure helpers (`StatusSuffixFor(SessionLogger?)`, `PromptForPath`) that any tab can
  call; `MainWindow.Logging.cs` already took the per-tab shape reading `ActiveWindowTabOrNull?.Logger`.
- **Zero tabs is reachable and the window stays open**, per the recommended resolution. Close Session
  is enabled at any tab count ≥ 1 (was: only > 1), and closing the last tab leaves the window in a
  disabled, neutral state rather than exiting or crashing: connection/device menu items disabled, the
  send field disabled and cleared, window title reset, status line reading "No sessions open — use
  File > New Session... to start one." File > New Session... remains available and reachable from
  this state (seeded from the last-closed tab's `CliOptions`, tracked as `_lastCliOptions`/
  `lastCliOptions`) — it's the only way back to one tab. WPF: `MainWindow.HandleZeroTabs()`. TUI:
  `TuiMode.HandleZeroTabs()`, gated on the existing `ActiveTabOrNull()`/`ActiveTab()` split (the
  latter still throws when genuinely called with no tab active, matching `ActiveWindowTab`'s WPF
  counterpart).
- **Keyboard shortcuts**: Ctrl+T (new session), Ctrl+W (close active tab), Ctrl+Tab/Ctrl+Shift+Tab
  (next/previous tab, wrapping, no-op below two tabs) — the same bindings in both front ends, matching
  the browser-tab convention this doc's Open questions section called out for WPF and extending it to
  TUI too, since there was no existing TUI precedent to conflict with. Each needed an explicit handler
  beyond the menu item's own label (`MenuItem.InputGestureText` in WPF, a Terminal.Gui `MenuItem`'s
  `Key` argument in TUI — neither registers a live accelerator by itself, the same gotcha noted
  elsewhere for Ctrl+Q). WPF: `MainWindow.HandleGlobalKeyDown`, wired off `PreviewKeyDown`. TUI: a
  `sessionShortcuts` handler on `Application.KeyDown`, gated on `app.TopRunnableView == window` so it
  doesn't fire while a nested dialog (New Session's `ConfigureMode`, a control panel) is on top —
  mirrors the existing `quitOnCtrlQ` handler's own pattern exactly, including unsubscribing on
  `window.Disposing`.
- **SendHistory and the starting tab** were already resolved and unchanged by Step 4: SendHistory has
  been per-tab since Steps 2/3, and the startup connection becomes the first tab exactly as designed,
  with no remaining ambiguity.

Verified: full solution build + full Unit test suite in both `DevTerm.Console.Tests` and
`DevTerm.Wpf.Tests`, 0 failures. `docs/specs/tui-main-screen.md` and `docs/specs/wpf-main-window.md`
no longer carry the Step-4-deferred Open items — both are fully implemented now.

Recommended implementation order, each step independently testable (all done):

1. ~~Extract `SessionTab` in `DevTerm.Configuration`, with `TuiMode`/`MainWindow` each still using
   exactly one (a pure refactor — behavior unchanged, but proves the extraction is clean before the
   harder multi-tab UI work).~~ Done.
2. ~~WPF: wrap the single `SessionTab` in a one-`TabItem` `TabControl`, then wire File > New Session
   to add a second. WPF's `TabControl` is a known, low-risk quantity.~~ Done.
3. ~~TUI: same shape using `Terminal.Gui.Views.Tabs`, once WPF has proven the `SessionTab`
   extraction is solid — this is the front end where the tab control itself is the less-proven part,
   so sequencing it second reduces risk.~~ Done.
4. ~~Resolve the Open questions above (SendHistory scope, logging scope, Stream Monitor scope,
   zero-tab behavior, shortcuts) as part of implementing New/Close Session, not before — each is
   small enough to decide in its own step rather than blocking the whole feature on a
   design-doc-only decision.~~ Done.
5. ~~Update `docs/specs/tui-main-screen.md` and `docs/specs/wpf-main-window.md`'s **Open items**
   sections and add the new Fields/Actions/States entries for tab-related UI, in the same change that
   implements each piece — not deferred to the end.~~ Done (2026-09-30): both specs now document the
   tab strip in full, including Step 4's per-tab logging/monitor, zero-tab state, and keyboard
   shortcuts.
