# Playback Window

## Purpose

Replays a session log (see [`docs/design/session-logging.md`](../design/session-logging.md))
through a chosen set of presenters, with transport controls, a position indicator, trimming and
notes. It's opened from **File > Open Log for Playback...** in the TUI (`DevTerm.Console.PlaybackMode`,
a nested modal) and WPF (`DevTerm.Wpf.PlaybackWindow`, non-modal like the control panels). Both
draw the same `DevTerm.Logging.Playback.PlaybackController`, so their behavior is identical, and
this spec covers both. Playback never touches a transport or the main window's connection: it can
be used with no device, or while the main window is connected to one.

The CLI's equivalent is non-interactive: `--playback <log>` (see **Per-front-end notes**).

## Opening

| Front end | How the log is picked | Default |
|---|---|---|
| TUI | A one-line path prompt | The newest `*.jsonl` in `~/.dev-term/logs`, or that directory |
| WPF | A standard Open File dialog, filtered to `*.jsonl` | Starts in `~/.dev-term/logs` |

A file that isn't a session log (wrong first line, a newer format version, a bad record before the
last line, or a missing or unreadable file) isn't opened. The TUI shows an error dialog. WPF adds an
`Error` line to the main window's output. A torn *last* line is tolerated: the window opens, and the
warning is its first output line.

## Fields

| Field | Type | Notes |
|---|---|---|
| Title | Window title | `dev-term — Playback: {file name}` |
| Description | One line at the top | `{profile} ({connection}), {capture time, local}, {n} records`, then `, trimmed from {file}` for a trimmed log |
| Presenters | TUI: text field (comma/space separated), with the installed names listed under it. WPF: one check box per installed presenter | Starts as the log header's `presenters`, limited to installed ones, or `hex` if none are left. Unknown names are ignored. At least one always stays selected (WPF puts the last check back) |
| Output | TUI: read-only `Editor`, last 300 lines. WPF: `ListBox` of `PlaybackLine`, last 1000 | `{mm:ss.fff} [{source}] {text}` per line, where the offset is from the first record. `[ascii]`/`[hex]`/… is received data as that presenter rendered it. `[tx]` is sent bytes, escaped (`\r`, `\n`, `\t`, `\\`, `\xNN`). `[dev-term]` is an event (`Connected.`, `Disconnected.`, `Logging {connection}, connection open` or `closed`). `[error]` is a lost connection or a presenter failure. `[note]` is a note. WPF colors them: sent in steel blue, events dimmed italic, errors dark red, notes bold gold |
| Position | Label | `{Playing/Paused/End}  {position}/{count}  {elapsed} / {duration}  {speed}  Selection {in}–{out}`. The position is how many records have played |
| Seek slider | WPF only, `0`–`count` | Dragging or clicking seeks (see **Seek**) |
| Speed | TUI: **Slower**/**Faster** buttons stepping through the list. WPF: a combo box | 0.25x, 0.5x, **1x** (default), 2x, 10x, Max |
| Note | WPF only: a text box next to **Add Note** | Enter adds it. The TUI prompts instead |

## Actions

| Action | Behavior | On failure |
|---|---|---|
| **Play / Pause** | Plays in real time at the chosen speed, and toggles to Pause. At the end, Play starts again from the beginning (clearing the output). Playback pauses itself when the last record plays | n/a |
| **Step** | Pauses and plays exactly one record | Nothing at the end |
| **Rewind** | Clears the output and goes back to record 0 with fresh presenters. It keeps playing if it was | n/a |
| **+10s** (fast-forward) | Plays everything in the next 10 s of log time instantly, and always at least one record | n/a |
| **End** | Plays everything that's left instantly, then pauses | n/a |
| **Seek** (WPF slider) | Forward plays the records in between instantly. Backward clears the output and replays from 0. Either way, stateful presenters end up exactly where live capture left them | n/a |
| **Presenters** (TUI: edit + Enter; WPF: tick/untick) | Clears the output and replays up to the current position through the new set | Unknown names are ignored. An empty choice keeps the current set |
| **Speed** | Applies from now on. Progress through the current gap is kept | n/a |
| **Mark In** | The trim selection starts at the current position (the next record to play) | n/a |
| **Mark Out** | The trim selection ends at the current position (after the last record played) | n/a |
| **Save Selection...** | TUI: a path prompt. WPF: a Save File dialog. Both default to `{name}.trim-{in}-{out}.jsonl` next to the log. Writes records `[in, out)` as a new log: same header plus `trimmedFrom`, original sequence numbers and timestamps | An empty selection, or the log's own path, is refused with a message (TUI dialog, WPF `[error]` line). A write failure is reported the same way |
| **Add Note...** | Inserts a `note` record at the current position with the previous record's timestamp, shows it at once, and **saves the log file in place** (atomic replace). The selection end moves with the records after it | An empty note does nothing. A save failure, such as the log still being recorded by this process, is reported (TUI dialog, WPF `[error]` line) |
| **Close** (TUI button; WPF window close) | Closes the window. The TUI returns to the main screen | n/a |

## States

- Only `rx` records go through the presenters. `tx` is displayed but never rendered, which is what
  presenters saw live.
- A presenter that throws during playback shows `[error] A presenter failed on record {n}: …`, and
  playback continues.
- The timer ticks only while the window is open (TUI 40 ms, WPF 30 ms). It does nothing while paused.
- Timing follows the log's timestamps scaled by the speed. Pausing part-way through a long gap resumes
  with only the rest of it.

## Per-front-end notes

- **TUI**: a nested `Application.Run` modal over the main window. The timer is `IApplication.AddTimeout`,
  and it's removed when the window closes. Buttons have hotkeys (Alt+letter): **R**ewind, S**t**ep,
  **P**lay, +10**s**, **E**nd, Slo**w**er, **F**aster, Mark **I**n, Mark **O**ut, Sa**v**e Selection,
  Add **N**ote, **C**lose. The output pane is plain text, so line kinds are told apart by their tags,
  not color. Buttons have no shadow, so both button rows fit an 80-column terminal.
- **WPF**: non-modal (`Show`), owned by the main window when it's really shown. Uses a
  `DispatcherTimer`. Big batches (a seek to the end) only add the last 1000 lines.
- **CLI**: `dev-term --playback <log> [--presenter a,b] [--playbackspeed n]` prints the same lines to
  stdout. A one-line summary, and any warnings, go to stderr. `--playbackspeed 0` (the default) is as
  fast as possible, and `1` is realtime. It exits 0 at the end, 1 for a bad file, an unknown presenter
  or a negative speed. It never connects.

## Open items

- Notes can be added but not edited or deleted here.
- No "skip silence" (capping long idle gaps) yet.
- Backward seeks replay from the start, so they get slower as the log grows.
- The TUI has no seek control besides Rewind/+10s/End/Step.
