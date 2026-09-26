# Bug reports

One file per bug: `NNN-short-slug.md`, numbered in the order filed. **File and maintain them with the
[`bug-report` skill](../../.claude/skills/bug-report/SKILL.md)**, which has the template, numbering, severity and
confidence rules, and the Fixed/Resolution lifecycle. Each report records the date and the full git commit it was
found at (`git show <hash>:<path>` shows the code its line numbers refer to), plus the failure scenario, a suggested
fix, and the tests that should come with the fix.

**Keeping them current:**
- **Status** is `Open`, `In progress`, `Fixed` or `Won't fix`. When a fix lands, set `Fixed`, add a `## Resolution`
  section naming the commit and the regression test, and log the detail in `docs/changes/YYYY-MM-DD.md` as usual.
  Keep the file (so the number isn't reused); update its row below.
- **Confidence** says whether a finding was confirmed by reading the code, reproduced, or only plausible. Reproduce
  a `Plausible` one (ideally as a failing test) before fixing it.
- Line numbers go stale as code moves; they refer to the report's **Found at commit**, and the symbol names are the
  durable part.

Bugs 001-059 came from a static, read-only review of `main` @ `42758db` on 2026-09-26 (five parallel reviewers:
Core/Logging/presenters, transports, devices/manifests/UI model, Configuration, TUI/WPF front ends). None has been
reproduced by running it yet. Six were found independently by two reviewers (001, 006, 007, 008, 009, 020).

## High

| # | Bug | Area | Status |
|---|---|---|---|
| 001 | [Opening an already-open session starts a second read loop](001-session-double-open.md) | DevTerm.Core (Session), TUI, WPF | Fixed |
| 002 | [USBTMC Close hangs forever while a reply over 64 KB is being read](002-usbtmc-close-hang-large-reply.md) | DevTerm.Transports.Usbtmc | Fixed |
| 003 | [Loopback transport can't reconnect after Disconnect](003-loopback-cannot-reconnect.md) | DevTerm.Transports.Loopback | Fixed |
| 004 | [One saved profile with a bad value crashes TUI startup and blocks the WPF connect](004-bad-profile-value-crashes-title.md) | DevTerm.Configuration (ConnectionProfileStore), TUI, WPF | Fixed |
| 005 | [DE-5000 negative phase angle and D readings decode as huge positives](005-de5000-negative-secondary.md) | DevTerm.Devices.De5000 | Open |
| 006 | [One missing SCPI or manifest reply shifts every later reply onto the wrong field](006-reply-queue-desync.md) | DevTerm.Core (LineReplyPresenter), DevTerm.Devices.Scpi, DevTerm.DeviceManifests | Open |
| 007 | [Closed TUI windows are never disposed: Page Up/Down stop working and handlers leak](007-tui-modal-windows-not-disposed.md) | TUI (DevTerm.Console) | Open |

## Medium

| # | Bug | Area | Status |
|---|---|---|---|
| 008 | [Real read failures are reported as a clean hang-up with no error](008-pump-swallows-read-errors.md) | DevTerm.Core (StreamToPipePump), Serial/TCP/HID | Open |
| 009 | [Closing while the pipe is full throws part-way and leaks the port or socket](009-pump-flush-cancel-close-leak.md) | DevTerm.Core (StreamToPipePump, Session), Serial/TCP/HID | Open |
| 010 | [A manifest's UiFile/KaitaiFile path can make Save write, and Load read, anywhere on disk](010-manifest-path-traversal.md) | DevTerm.DeviceManifests (loader, writer, validator) | Open |
| 011 | [Manifest zips extract with no size limit and leave a temp folder behind on every open](011-manifest-zip-unbounded-temp-leak.md) | DevTerm.DeviceManifests (loader) | Open |
| 012 | [Profile names are never validated](012-profile-names-not-validated.md) | DevTerm.Configuration (ConnectionProfileStore, DevTermUserDataPaths) | Open |
| 013 | [Save Profile and Export crash on a bad name or path](013-save-export-no-error-handling.md) | DevTerm.Configuration (ConnectionEditorViewModel), TUI, WPF | Open |
| 014 | [Saving "bench" silently overwrites "Bench"](014-save-overwrites-different-case.md) | DevTerm.Configuration (ConnectionEditorViewModel) | Open |
| 015 | [A mistyped baud rate or data bits silently connects with defaults](015-mistyped-numbers-silently-default.md) | DevTerm.Configuration (ConnectionEditorViewModel, CliOptionsValidator) | Open |
| 016 | [WPF control panels stay bound to the old session after a profile switch](016-wpf-panels-bound-to-old-session.md) | WPF (MainWindow, control panels) | Open |
| 017 | [A WPF profile switch can't be superseded by a second switch](017-wpf-profile-switch-no-supersede.md) | WPF (MainWindow) | Open |
| 018 | [Quitting the TUI after a profile switch never closes the live session](018-tui-exit-leaves-switched-session-open.md) | TUI (TuiMode) | Open |
| 019 | [A Stream Monitor capture in progress is lost when the TUI quits](019-tui-stream-monitor-capture-lost-on-quit.md) | TUI (TuiMode, Stream Monitor) | Open |
| 020 | [Each Zoom H4n panel open adds a pipeline presenter that is never removed](020-zoomh4n-wake-watcher-leak.md) | DevTerm.Devices.ZoomH4n, TUI, WPF | Open |
| 021 | [Radex One readings aren't checksum-verified, although the comments say they are](021-radexone-extension-checksum-unverified.md) | DevTerm.Devices.RadexOne | Open |
| 022 | [One false Radex One header can stall decoding for minutes](022-radexone-false-header-stall.md) | DevTerm.Devices.RadexOne | Open |
| 023 | [One malformed SCPI profile file breaks all SCPI features for the rest of the run](023-scpi-profile-catalog-bad-file.md) | DevTerm.Devices.Scpi (ScpiProfileCatalog) | Open |
| 024 | [A comma inside a text parameter shifts every later parameter](024-comma-in-text-parameter.md) | DevTerm.Devices.Scpi, DevTerm.DeviceManifests (control surfaces) | Open |
| 025 | [BLE Disconnect doesn't actually drop the link](025-ble-service-not-disposed.md) | DevTerm.Transports.Ble.Windows | Open |
| 026 | [BLE writes aren't split to the packet size](026-ble-writes-not-mtu-chunked.md) | DevTerm.Transports.Ble.Windows | Open |
| 027 | [BLE connect and write timeouts are documented but never used](027-ble-timeouts-unused.md) | DevTerm.Transports.Ble, DevTerm.Configuration | Open |
| 028 | [UTF-8 characters split across two reads come out garbled](028-utf8-split-across-reads.md) | DevTerm.Presenters.Text (Utf8Presenter) | Open |
| 029 | [TCP writes block with no timeout and ignore cancellation](029-tcp-write-blocks-no-timeout.md) | DevTerm.Transports.Tcp | Open |
| 030 | [Adding a note to a log that's still being recorded fails and leaves memory and disk out of step](030-playback-addnote-live-log.md) | DevTerm.Logging (PlaybackController, SessionLog) | Open |
| 031 | [The TUI output pane has no backpressure](031-tui-output-no-backpressure.md) | TUI (TuiMode) | Open |
| 032 | [A bad value on the command line or in the saved default crashes startup instead of opening the editor](032-startup-bind-failure-crash.md) | DevTerm.Console (Program), DevTerm.Wpf (App) | Open |

## Low

| # | Bug | Area | Status |
|---|---|---|---|
| 033 | [Profiles, the saved default and preferences are written non-atomically](033-non-atomic-writes.md) | DevTerm.Configuration | Open |
| 034 | [Export All deletes the existing zip before checking the profile names](034-exportzip-deletes-target-first.md) | DevTerm.Configuration (ConnectionProfileStore) | Open |
| 035 | [Typing an export path marks the editor as having unsaved changes](035-dirty-tracking-non-connection-fields.md) | DevTerm.Configuration (ConnectionEditorViewModel) | Open |
| 036 | [A session-log write failure is silent and can leave a torn line that makes the whole log unloadable](036-log-write-failure-silent.md) | DevTerm.Logging (SessionLogWriter, SessionLogger), DevTerm.Core (Session) | Open |
| 037 | [A line exactly at the max length is followed by a spurious empty line](037-ascii-maxlength-spurious-empty-line.md) | DevTerm.Presenters.Text (AsciiPresenter) | Open |
| 038 | [Session logging does blocking file I/O on the read loop for every chunk](038-logging-blocking-io-read-loop.md) | DevTerm.Logging (SessionLogger, SessionLogWriter) | Open |
| 039 | [Calling SendAsync from the read-loop thread deadlocks if the send fails](039-sendasync-from-read-loop-deadlock.md) | DevTerm.Core (Session) | Open |
| 040 | [Playback offsets over 24 hours wrap](040-playback-offset-over-24h.md) | DevTerm.Logging (PlaybackText) | Open |
| 041 | [An unknown first record with no timestamp makes 1x playback wait effectively forever](041-unknown-record-min-timestamp.md) | DevTerm.Logging (SessionLogFormat) | Open |
| 042 | [A hinted #0 indefinite-length block keeps its header bytes in the capture](042-stream-watcher-indefinite-block.md) | DevTerm.Logging (StreamContentWatcher) | Open |
| 043 | [A typed value containing {OtherParam} is itself substituted](043-template-substitution-not-single-pass.md) | DevTerm.Devices.Scpi, DevTerm.DeviceManifests (control surfaces) | Open |
| 044 | [A profile's numeric parameter limits can throw or force every value to 0](044-scpi-clamp-bad-limits.md) | DevTerm.Devices.Scpi (ScpiControlSurface) | Open |
| 045 | [Device control surfaces parse numbers with throwing Parse](045-control-surface-parse-throws.md) | DevTerm.Devices.Busylight, K8055, RadexOne | Open |
| 046 | [Manifest numbers can go out as NaN, or rounded past Max](046-manifest-formatnumber-nan.md) | DevTerm.DeviceManifests (ManifestControlSurface) | Open |
| 047 | [The Radex One panel describes the device as USB HID](047-radexone-description-says-hid.md) | DevTerm.Devices.RadexOne (RadexOneUiDefinition) | Open |
| 048 | [SCPI *IDN? matching runs profile regexes with no timeout](048-scpi-idn-regex-no-timeout.md) | DevTerm.Devices.Scpi (ScpiProfileCatalog) | Open |
| 049 | [UI definition XML is parsed without prohibiting DTDs](049-uidefinition-xml-dtd.md) | DevTerm.UiDefinitions (UiDefinitionSerializer) | Open |
| 050 | [A manifest can make strip-chart history grow without limit](050-strip-chart-history-unbounded.md) | DevTerm.UiDefinitions / DeviceManifests (LiveDisplayState) | Open |
| 051 | [LF followed by CR counts as two lines](051-line-reply-lf-cr-two-lines.md) | DevTerm.Core (LineReplyPresenter) | Open |
| 052 | [BLE notifications arriving right after subscribe are dropped](052-ble-notifications-before-pipe.md) | DevTerm.Transports.Ble | Open |
| 053 | [BLE pipe writes from Bluetooth threads aren't synchronized](053-ble-pipe-writes-unsynchronized.md) | DevTerm.Transports.Ble | Open |
| 054 | [A cancelled BLE connect leaves its ValueChanged handler attached](054-ble-cancelled-connect-handler-leak.md) | DevTerm.Transports.Ble.Windows | Open |
| 055 | [HID read thread can crash the process; Close blocks the calling thread](055-hid-read-thread-and-close-blocking.md) | DevTerm.Transports.Hid | Open |
| 056 | [TCP listen mode rejects hostnames and is IPv4-only](056-tcp-listen-rejects-hostnames.md) | DevTerm.Transports.Tcp | Open |
| 057 | [A serial read may never notice an unplugged adapter](057-serial-unplug-not-detected.md) | DevTerm.Transports.Serial | Open |
| 058 | [Two close requests during a slow cleanup run OnClosing twice](058-wpf-onclosing-reentry.md) | WPF (MainWindow) | Open |
| 059 | [CLI Ctrl+C may not interrupt a pending read on Linux/macOS](059-cli-ctrl-c-non-windows.md) | CLI (CliMode) | Open |

