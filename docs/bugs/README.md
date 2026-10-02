# Bug reports

One file per bug: `NNN-short-slug.md`, numbered in the order filed. **File and maintain them with the
[`bug-report` skill](../../.claude/skills/bug-report/SKILL.md)**, which has the template, numbering, severity and
confidence rules, and the Fixed/Resolution lifecycle. Each report records the date and the full git commit it was
found at (`git show <hash>:<path>` shows the code its line numbers refer to), plus the failure scenario, a suggested
fix, and the tests that should come with the fix.

**Keeping them current:**
- **Status** is `Open`, `In progress`, `Fixed` or `Won't fix`. When a fix lands, set `Fixed`, add a `## Resolution`
  section naming the commit and the regression test, and log the detail in `docs/changes/YYYY-MM-DD.md` as usual.
  Any closed report (`Fixed`, `Won't fix`, or a duplicate) then moves into `docs/bugs/resolved/` (same file name,
  number kept, never reused), so `docs/bugs/` holds only open and in-progress work. Update its row below and
  every other link to it accordingly.
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
| 001 | [Opening an already-open session starts a second read loop](resolved/001-session-double-open.md) | DevTerm.Core (Session), TUI, WPF | Fixed |
| 002 | [USBTMC Close hangs forever while a reply over 64 KB is being read](resolved/002-usbtmc-close-hang-large-reply.md) | DevTerm.Transports.Usbtmc | Fixed |
| 003 | [Loopback transport can't reconnect after Disconnect](resolved/003-loopback-cannot-reconnect.md) | DevTerm.Transports.Loopback | Fixed |
| 004 | [One saved profile with a bad value crashes TUI startup and blocks the WPF connect](resolved/004-bad-profile-value-crashes-title.md) | DevTerm.Configuration (ConnectionProfileStore), TUI, WPF | Fixed |
| 005 | [DE-5000 negative phase angle and D readings decode as huge positives](resolved/005-de5000-negative-secondary.md) | DevTerm.Devices.De5000 | Fixed |
| 006 | [One missing SCPI or manifest reply shifts every later reply onto the wrong field](resolved/006-reply-queue-desync.md) | DevTerm.Core (LineReplyPresenter), DevTerm.Devices.Scpi, DevTerm.DeviceManifests | Fixed |
| 007 | [Closed TUI windows are never disposed: Page Up/Down stop working and handlers leak](resolved/007-tui-modal-windows-not-disposed.md) | TUI (DevTerm.Console) | Fixed |
| 061 | [The Radex One never replies because its serial connection uses the wrong baud rate](resolved/061-radexone-wrong-baud-rate.md) | DevTerm.Devices.RadexOne | Fixed |
| 063 | [A terminatorless reply over 4096 bytes burns extra entries off the SCPI/manifest reply-id queue](063-line-reply-overflow-flush-desyncs-pending-ids.md) | DevTerm.Core (LineReplyPresenter), DevTerm.Devices.Scpi, DevTerm.DeviceManifests | Open |

## Medium

| # | Bug | Area | Status |
|---|---|---|---|
| 008 | [Real read failures are reported as a clean hang-up with no error](resolved/008-pump-swallows-read-errors.md) | DevTerm.Core (StreamToPipePump), Serial/TCP/HID | Fixed |
| 009 | [Closing while the pipe is full throws part-way and leaks the port or socket](resolved/009-pump-flush-cancel-close-leak.md) | DevTerm.Core (StreamToPipePump, Session), Serial/TCP/HID | Fixed |
| 010 | [A manifest's UiFile/KaitaiFile path can make Save write, and Load read, anywhere on disk](resolved/010-manifest-path-traversal.md) | DevTerm.DeviceManifests (loader, writer, validator) | Fixed |
| 011 | [Manifest zips extract with no size limit and leave a temp folder behind on every open](resolved/011-manifest-zip-unbounded-temp-leak.md) | DevTerm.DeviceManifests (loader) | Fixed |
| 012 | [Profile names are never validated](resolved/012-profile-names-not-validated.md) | DevTerm.Configuration (ConnectionProfileStore, DevTermUserDataPaths) | Fixed |
| 013 | [Save Profile and Export crash on a bad name or path](resolved/013-save-export-no-error-handling.md) | DevTerm.Configuration (ConnectionEditorViewModel), TUI, WPF | Fixed |
| 014 | [Saving "bench" silently overwrites "Bench"](resolved/014-save-overwrites-different-case.md) | DevTerm.Configuration (ConnectionEditorViewModel) | Fixed |
| 015 | [A mistyped baud rate or data bits silently connects with defaults](resolved/015-mistyped-numbers-silently-default.md) | DevTerm.Configuration (ConnectionEditorViewModel, CliOptionsValidator) | Fixed |
| 016 | [WPF control panels stay bound to the old session after a profile switch](resolved/016-wpf-panels-bound-to-old-session.md) | WPF (MainWindow, control panels) | Fixed |
| 017 | [A WPF profile switch can't be superseded by a second switch](resolved/017-wpf-profile-switch-no-supersede.md) | WPF (MainWindow) | Fixed |
| 018 | [Quitting the TUI after a profile switch never closes the live session](resolved/018-tui-exit-leaves-switched-session-open.md) | TUI (TuiMode) | Fixed |
| 019 | [A Stream Monitor capture in progress is lost when the TUI quits](resolved/019-tui-stream-monitor-capture-lost-on-quit.md) | TUI (TuiMode, Stream Monitor) | Fixed |
| 020 | [Each Zoom H4n panel open adds a pipeline presenter that is never removed](resolved/020-zoomh4n-wake-watcher-leak.md) | DevTerm.Devices.ZoomH4n, TUI, WPF | Fixed |
| 021 | [Radex One readings aren't checksum-verified, although the comments say they are](resolved/021-radexone-extension-checksum-unverified.md) | DevTerm.Devices.RadexOne | Fixed |
| 022 | [One false Radex One header can stall decoding for minutes](resolved/022-radexone-false-header-stall.md) | DevTerm.Devices.RadexOne | Fixed |
| 023 | [One malformed SCPI profile file breaks all SCPI features for the rest of the run](resolved/023-scpi-profile-catalog-bad-file.md) | DevTerm.Devices.Scpi (ScpiProfileCatalog) | Fixed |
| 024 | [A comma inside a text parameter shifts every later parameter](resolved/024-comma-in-text-parameter.md) | DevTerm.Devices.Scpi, DevTerm.DeviceManifests (control surfaces) | Fixed |
| 025 | [BLE Disconnect doesn't actually drop the link](resolved/025-ble-service-not-disposed.md) | DevTerm.Transports.Ble.Windows | Fixed |
| 026 | [BLE writes aren't split to the packet size](resolved/026-ble-writes-not-mtu-chunked.md) | DevTerm.Transports.Ble.Windows | Fixed |
| 027 | [BLE connect and write timeouts are documented but never used](resolved/027-ble-timeouts-unused.md) | DevTerm.Transports.Ble, DevTerm.Configuration | Fixed |
| 028 | [UTF-8 characters split across two reads come out garbled](resolved/028-utf8-split-across-reads.md) | DevTerm.Presenters.Text (Utf8Presenter) | Fixed |
| 029 | [TCP writes block with no timeout and ignore cancellation](resolved/029-tcp-write-blocks-no-timeout.md) | DevTerm.Transports.Tcp | Fixed |
| 030 | [Adding a note to a log that's still being recorded fails and leaves memory and disk out of step](resolved/030-playback-addnote-live-log.md) | DevTerm.Logging (PlaybackController, SessionLog) | Fixed |
| 031 | [The TUI output pane has no backpressure](resolved/031-tui-output-no-backpressure.md) | TUI (TuiMode) | Fixed |
| 032 | [A bad value on the command line or in the saved default crashes startup instead of opening the editor](resolved/032-startup-bind-failure-crash.md) | DevTerm.Console (Program), DevTerm.Wpf (App) | Fixed |
| 062 | [WPF Up/Down history recall stops working on SendBox after clicking "Send"](resolved/062-sendbox-arrow-keys-lose-focus-after-send.md) | DevTerm.Wpf (MainWindow) | Fixed |
| 065 | [HP-GL plots with any pause longer than the idle timeout are split into multiple Stream Monitor captures](resolved/065-hpgl-no-end-detection-splits-one-plot.md) | DevTerm.Core (StreamContent) | Won't fix |

## Low

| # | Bug | Area | Status |
|---|---|---|---|
| 068 | [The Tektronix 2230 bridge runs at 4800 baud; it can probably run at 9600 if dev-term paces its writes](068-tek2230-bridge-runs-at-4800-baud.md) | `tektronix-2230.json`, DevTerm.Transports.Tcp | Open |
| 067 | [HP 34401A over RS-232 is not driven with write pacing, and probably needs a per-byte delay to be reliable](resolved/067-hp34401a-rs232-no-write-pacing.md) | DevTerm.Transports.Serial, `hp-agilent-keysight-34401a.json` | Fixed |
| 066 | [DS1102E omits the terminating zero-length packet when a reply ends on a 64-byte boundary (pyvisa-py #472)](resolved/066-ds1102e-usbtmc-missing-zlp-at-packet-boundary.md) | DevTerm.Transports.Usbtmc | Won't fix |
| 064 | [View > Echo Sent Commands doesn't echo a device-profile control panel's button/field sends](064-control-panel-sends-skip-echo.md) | DevTerm.Console (ControlPanelMode), DevTerm.Wpf (ControlPanelWindow) | Open |
| 033 | [Profiles, the saved default and preferences are written non-atomically](resolved/033-non-atomic-writes.md) | DevTerm.Configuration | Fixed |
| 034 | [Export All deletes the existing zip before checking the profile names](resolved/034-exportzip-deletes-target-first.md) | DevTerm.Configuration (ConnectionProfileStore) | Fixed |
| 035 | [Typing an export path marks the editor as having unsaved changes](resolved/035-dirty-tracking-non-connection-fields.md) | DevTerm.Configuration (ConnectionEditorViewModel) | Fixed |
| 036 | [A session-log write failure is silent and can leave a torn line that makes the whole log unloadable](resolved/036-log-write-failure-silent.md) | DevTerm.Logging (SessionLogWriter, SessionLogger), DevTerm.Core (Session) | Fixed |
| 037 | [A line exactly at the max length is followed by a spurious empty line](resolved/037-ascii-maxlength-spurious-empty-line.md) | DevTerm.Presenters.Text (AsciiPresenter) | Fixed |
| 038 | [Session logging does blocking file I/O on the read loop for every chunk](resolved/038-logging-blocking-io-read-loop.md) | DevTerm.Logging (SessionLogger, SessionLogWriter) | Fixed |
| 039 | [Calling SendAsync from the read-loop thread deadlocks if the send fails](resolved/039-sendasync-from-read-loop-deadlock.md) | DevTerm.Core (Session) | Fixed |
| 040 | [Playback offsets over 24 hours wrap](resolved/040-playback-offset-over-24h.md) | DevTerm.Logging (PlaybackText) | Fixed |
| 041 | [An unknown first record with no timestamp makes 1x playback wait effectively forever](resolved/041-unknown-record-min-timestamp.md) | DevTerm.Logging (SessionLogFormat) | Fixed |
| 042 | [A hinted #0 indefinite-length block keeps its header bytes in the capture](resolved/042-stream-watcher-indefinite-block.md) | DevTerm.Core (StreamContentSniffer, StreamContentWatcher) | Fixed |
| 043 | [A typed value containing {OtherParam} is itself substituted](resolved/043-template-substitution-not-single-pass.md) | DevTerm.Devices.Scpi, DevTerm.DeviceManifests (control surfaces) | Fixed |
| 044 | [A profile's numeric parameter limits can throw or force every value to 0](resolved/044-scpi-clamp-bad-limits.md) | DevTerm.Devices.Scpi (ScpiControlSurface) | Fixed |
| 045 | [Device control surfaces parse numbers with throwing Parse](resolved/045-control-surface-parse-throws.md) | DevTerm.Devices.Busylight, K8055, RadexOne | Fixed |
| 046 | [Manifest numbers can go out as NaN, or rounded past Max](resolved/046-manifest-formatnumber-nan.md) | DevTerm.DeviceManifests (ManifestControlSurface) | Fixed |
| 047 | [The Radex One panel describes the device as USB HID](resolved/047-radexone-description-says-hid.md) | DevTerm.Devices.RadexOne (RadexOneUiDefinition) | Fixed |
| 048 | [SCPI *IDN? matching runs profile regexes with no timeout](resolved/048-scpi-idn-regex-no-timeout.md) | DevTerm.Devices.Scpi (ScpiProfileCatalog) | Fixed |
| 049 | [UI definition XML is parsed without prohibiting DTDs](resolved/049-uidefinition-xml-dtd.md) | DevTerm.UiDefinitions (UiDefinitionSerializer) | Fixed |
| 050 | [A manifest can make strip-chart history grow without limit](resolved/050-strip-chart-history-unbounded.md) | DevTerm.UiDefinitions / DeviceManifests (LiveDisplayState) | Fixed |
| 051 | [LF followed by CR counts as two lines](resolved/051-line-reply-lf-cr-two-lines.md) | DevTerm.Core (LineReplyPresenter) | Fixed |
| 052 | [BLE notifications arriving right after subscribe are dropped](resolved/052-ble-notifications-before-pipe.md) | DevTerm.Transports.Ble | Fixed |
| 053 | [BLE pipe writes from Bluetooth threads aren't synchronized](resolved/053-ble-pipe-writes-unsynchronized.md) | DevTerm.Transports.Ble | Fixed |
| 054 | [A cancelled BLE connect leaves its ValueChanged handler attached](resolved/054-ble-cancelled-connect-handler-leak.md) | DevTerm.Transports.Ble.Windows | Fixed |
| 055 | [HID read thread can crash the process; Close blocks the calling thread](resolved/055-hid-read-thread-and-close-blocking.md) | DevTerm.Transports.Hid | Fixed |
| 056 | [TCP listen mode rejects hostnames and is IPv4-only](resolved/056-tcp-listen-rejects-hostnames.md) | DevTerm.Transports.Tcp | Fixed |
| 057 | [A serial read may never notice an unplugged adapter](resolved/057-serial-unplug-not-detected.md) | DevTerm.Transports.Serial | Fixed |
| 058 | [Two close requests during a slow cleanup run OnClosing twice](resolved/058-wpf-onclosing-reentry.md) | WPF (MainWindow) | Fixed |
| 059 | [CLI Ctrl+C may not interrupt a pending read on Linux/macOS](resolved/059-cli-ctrl-c-non-windows.md) | CLI (CliMode) | Fixed |
| 060 | [A TUI profile switch can close/reassign the wrong session under overlap](resolved/060-tui-profile-switch-session-race.md) | TUI (Console) | Fixed |

