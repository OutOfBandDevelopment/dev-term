using System.Collections.ObjectModel;
using System.Text;
using DevTerm.Configuration;
using DevTerm.Core.Control;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;
using DevTerm.Devices.Busylight;
using DevTerm.Devices.De5000;
using DevTerm.Devices.K8055;
using DevTerm.Devices.Nmea;
using DevTerm.Devices.RadexOne;
using DevTerm.Devices.Scpi;
using DevTerm.Devices.ZoomH4n;
using DevTerm.Logging;
using DevTerm.Transports.Tcp;
using Terminal.Gui.App;
using Terminal.Gui.Editor;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace DevTerm.Console;

/// <summary>
/// The full-screen terminal UI: a scrolling output pane and a send line, backed by the same
/// <see cref="Session"/> the CLI uses. See docs/design/frontends.md.
/// </summary>
/// <remarks>
/// A first stub, not the full design (no live plugin selection, no rendering-presenter graphics) -
/// see docs/design/frontends.md's TUI section for the target. Multiple sessions are supported via
/// <see cref="Terminal.Gui.Views.Tabs"/> (see docs/design/multi-session-ui.md) - each open tab wraps
/// its own <see cref="DevTerm.Configuration.SessionTab"/> in a <see cref="TuiWindowTab"/>.
/// </remarks>
public static class TuiMode
{
    /// <summary>
    /// Caps the scrolling output pane the same way <c>MainWindow.MaxOutputLines</c> does for WPF, so
    /// a long-running session doesn't grow it without bound — but shorter than WPF's 1000, since this
    /// pane is a single concatenated <see cref="Editor.Text"/> string rebuilt on every trim, not a
    /// virtualized items list; keeping it smaller keeps that rebuild cheap. Oldest lines are dropped
    /// first. Applies per tab (see <see cref="TuiWindowTab.OutputLines"/>), not to the window overall.
    /// </summary>
    private const int _maxOutputLines = 300;

    /// <summary>
    /// One open tab's UI-side state — the <see cref="Terminal.Gui.Views.Tabs"/> equivalent of WPF's
    /// own private <c>WindowTab</c> (<c>MainWindow.xaml.cs</c>). <see cref="Output"/> IS the view
    /// inserted into the window's <see cref="Terminal.Gui.Views.Tabs"/> control (its <c>Title</c> is
    /// the tab header, and its <c>Data</c> points back to this instance so <c>ActiveTab()</c> can
    /// resolve it from <c>Tabs.Value</c>) — there's no separate header/content split the way WPF's
    /// <c>TabItem</c>/<c>ListBox</c> pair needed, since a plain <see cref="Editor"/> already serves
    /// both roles. <see cref="OutputHandler"/>/<see cref="DisconnectedHandler"/> are named (not inline
    /// lambdas) so a profile switch or a tab close can unsubscribe the exact right delegate from the
    /// exact right <see cref="Session"/>. <see cref="SwitchCts"/> replaces the old window-level
    /// <c>switchCts</c> field — one profile-switch attempt per tab can be in flight at a time, not one
    /// per window.
    /// </summary>
    private sealed class TuiWindowTab
    {
        public required SessionTab Tab { get; init; }

        public required Editor Output { get; init; }

        public List<string> OutputLines { get; } = [];

        public BatchedOutputQueue? OutputQueue { get; set; }

        public EventHandler<PresenterOutput>? OutputHandler { get; set; }

        public EventHandler<SessionDisconnectedEventArgs>? DisconnectedHandler { get; set; }

        public CancellationTokenSource? SwitchCts { get; set; }

        /// <summary>This tab's own running log, if any (docs/design/multi-session-ui.md's Step 4) — not a single window-level logger.</summary>
        public SessionLogger? Logger { get; set; }

        /// <summary>This tab's own Stream Monitor, if Device &gt; Stream Monitor... has been opened for it — not a single window-level monitor.</summary>
        public StreamMonitor? Monitor { get; set; }
    }

    public static async Task<int> RunAsync(Session session, PresenterCatalog catalog, CliOptions cliOptions, ConnectionProfileStore? profileStore = null)
    {
        // A failed first connect doesn't end the TUI: it opens disconnected with the error shown,
        // so the user can retry (File > Connect) or pick a different connection (File > Device
        // Profiles...) from there, rather than being dropped back to the shell.
        string? startupError = null;
        try
        {
            await session.OpenAsync();
        }
        catch (Exception ex)
        {
            startupError = $"{ConnectionErrorMessages.For(cliOptions.Transport, ex)} Use File > Connect to retry, or File > Device Profiles... to choose another connection.";
        }

        var app = Application.Create().Init();
        TuiTheme.Apply(ActiveTheme.Current);
        TuiWindowParts parts;
        try
        {
            parts = BuildWindow(app, session, catalog, cliOptions, profileStore, startupError);
            parts.SendField.SetFocus();

            // Application.Run's errorHandler is what WPF's DispatcherUnhandledException does for the
            // GUI: report whatever slips past every existing catch block (a genuine bug, not one of
            // the already-handled ConnectionErrorMessages.IsConnectionFailure cases) and resume the
            // loop rather than letting the whole TUI die. Per Terminal.Gui's own doc comment on this
            // overload, this only takes effect in RELEASE builds - a DEBUG build still rethrows so a
            // debugger can break on the original exception.
            app.Run(parts.Window, OnUnhandledException);

            // window.Disposing never fires once Run returns (the window is never disposed here -
            // see app.Dispose() below, which only tears down the driver), so every tab's own logger
            // and Stream Monitor (Step 4: per-tab, not window-level) must be stopped/flushed
            // explicitly here rather than relying on that event. Mirrors MainWindow.OnClosing's own
            // per-tab cleanup loop.
            parts.CleanupAllTabs();
        }
        finally
        {
            app.Dispose();
        }

        // A profile switch (or File > New Session) replaces/adds sessions this method never saw
        // directly - closing/disposing only this method's own (by-then-stale, possibly already-closed)
        // `session` parameter here would leave every other tab's session never closed or disposed.
        // TuiWindowParts.AllSessions always names every tab's current one, mirroring MainWindow's
        // OnClosing loop over every WindowTab.
        foreach (var currentSession in parts.AllSessions())
        {
            await currentSession.CloseAsync();
            if (!ReferenceEquals(currentSession, session))
            {
                await currentSession.DisposeAsync();
            }
        }

        return 0;

        bool OnUnhandledException(Exception ex)
        {
            MessageBox.ErrorQuery(
                app,
                "dev-term — unexpected error",
                $"An unexpected error occurred and has been ignored so dev-term can keep running:\n\n{ex}",
                "Ok");
            return true;
        }
    }

    /// <summary>
    /// Builds the window and wires it to <paramref name="session"/> as its first tab, without
    /// touching <c>Application.Init</c>/<c>Run</c>/<c>Shutdown</c> — split out so tests can drive the
    /// same production controls headlessly (see <c>DevTerm.Console.Tests.TuiModeTests</c>), the same
    /// seam <c>MainWindow.xaml.cs</c> exposes for WPF (<c>ConnectAsync</c>/<c>SendCurrentInputAsync</c>).
    /// </summary>
    internal static TuiWindowParts BuildWindow(IApplication app, Session session, PresenterCatalog catalog, CliOptions cliOptions, ConnectionProfileStore? profileStore = null, string? initialMessage = null)
    {
        // Also what "is this connection a saved profile?" (titles/tab headers) is answered against,
        // and what the Device Profiles screen edits - a test passes an isolated one rather than the
        // real user folder.
        profileStore ??= new ConnectionProfileStore();

        var tab = new SessionTab(session, catalog, cliOptions);

        // Every open tab, in strip order - resolved back from Tabs.Value via ActiveTab() below, the
        // same way MainWindow.xaml.cs's own _tabs/ActiveWindowTab work for WPF.
        var tabs = new List<TuiWindowTab>();

        // The Device menu items RefreshConnectionUi enables/disables, plus the Close Session item
        // UpdateCloseSessionAvailability enables/disables - assigned once the menu is built below,
        // declared up here so a closure (AddTab, which can run before the menu exists) can safely read
        // them as still-null rather than hit a definite-assignment error.
        MenuItem? k8055MenuItem = null;
        MenuItem? busylightMenuItem = null;
        MenuItem? scpiMenuItem = null;
        MenuItem? radexOneMenuItem = null;
        MenuItem? zoomH4nMenuItem = null;
        MenuItem? de5000MenuItem = null;
        MenuItem? nmea0183MenuItem = null;
        MenuItem? manifestMenuItem = null;
        MenuItem? streamMonitorMenuItem = null;
        MenuItem? newSessionMenuItem = null;
        MenuItem? closeSessionMenuItem = null;
        MenuItem? deviceProfilesMenuItem = null;
        MenuBarItem? sendAsMenuBarItem = null;

        // View > Echo Sent Commands / Software Flow Control / Clear Output - predeclared for the same
        // reason as the Device menu items above: their own click actions (and, for Software Flow
        // Control, RefreshConnectionUi) need to reference the MenuItem to update its own Title.
        MenuItem? echoSentCommandsMenuItem = null;
        MenuItem? clearOutputMenuItem = null;
        MenuItem? softwareFlowControlMenuItem = null;

        // Window-level (not per-tab), mirroring MainWindow.xaml.cs's own _echoSentCommands - echoing
        // typed commands is a display preference for this window session, not a property of any one
        // connection.
        var echoSentCommands = false;

        // The File menu's Start/Stop Logging item - one shared piece of UI chrome whose Title flips to
        // reflect whichever tab is active (see RefreshConnectionUi), the same way connectMenuItem does.
        // The running SessionLogger itself is per-tab (TuiWindowTab.Logger, Step 4) - two tabs can log
        // to two different files at once, and closing one tab's log never touches another's.
        var loggingMenuItem = new MenuItem(TuiLogging.StartTitle, string.Empty, () => { });

        // Seeds File > New Session... with a starting point once the window has zero tabs open (no
        // active tab's CliOptions to read) - kept up to date in CloseTabAsync right before a tab is
        // removed. Mirrors MainWindow.xaml.cs's own _lastCliOptions.
        var lastCliOptions = cliOptions;

        var window = new Window
        {
            Title = tab.Title(profileStore),
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };

        // Replaces the old single output Editor in the same region (X=0, Y=1, below the menu bar,
        // Height=Dim.Fill(2) to leave room for the Send: row and the status line) - each tab's own
        // Editor (see AddTab) is inserted as one of its child views.
        var tabsView = new Tabs
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(2),
        };

        // Mirrors MainWindow's ActiveWindowTabOrNull/ActiveWindowTab - View.Data (set in AddTab) is this
        // TUI's equivalent of WPF's TabItem.Tag. Null with zero tabs open (Step 4); ActiveTab() is the
        // throwing form for call sites that can only ever run with a tab active (they're disabled or
        // unreachable otherwise), ActiveTabOrNull() for the few reachable at zero tabs too.
        TuiWindowTab? ActiveTabOrNull() => tabsView.Value?.Data as TuiWindowTab;
        TuiWindowTab ActiveTab() => ActiveTabOrNull() ?? throw new InvalidOperationException("No session tabs are open.");

        // Tagged by tab (not a single window-level queue) so a fast-arriving background tab's output
        // can't block or get dropped by the active tab's own coalescing - see BatchedOutputQueue and
        // docs/bugs/fixed/031-tui-output-no-backpressure.md (the single-tab bug this originally fixed).
        void AppendOutput(TuiWindowTab windowTab, string line) => windowTab.OutputQueue!.Enqueue(line);

        // The output pane is one plain-text Editor (no per-line colors), so status and error lines
        // are told apart from device output by a source tag, the same "[source] text" shape device
        // lines already use ("[ascii] ...").
        void AppendStatus(TuiWindowTab windowTab, string text) => AppendOutput(windowTab, StatusLine(text));
        void AppendError(TuiWindowTab windowTab, string text) => AppendOutput(windowTab, ErrorLine(text));

        void UpdateCloseSessionAvailability()
        {
            closeSessionMenuItem?.Enabled = tabs.Count > 0;
        }

        // Builds one tab's Editor/state and inserts it into tabsView, making it the active tab -
        // directly modeled on MainWindow.xaml.cs's own AddTab. Called once for the startup connection
        // (from the bottom of this method, once every other control it might touch already exists) and
        // once per File > New Session... Never called before the rest of the window's chrome
        // (connectMenuItem, sendField, statusLabel, the Device menu items) has been built - RefreshConnectionUi,
        // which every AddTab call can reach (via tabsView.ValueChanged, or via StartLoggingForTab for
        // --log), reads all of them.
        TuiWindowTab AddTab(SessionTab sessionTab, string? startupMessage = null)
        {
            var outputLines = new List<string>();
            if (ManifestNameWarning.For(sessionTab.CliOptions) is { } startupWarning)
            {
                outputLines.Add(StatusLine(startupWarning));
            }

            if (tabs.Count == 0)
            {
                // Bad theme files, an unknown --theme, an unreadable preferences file - reported once,
                // against the very first tab, never fatal.
                outputLines.AddRange(ActiveTheme.StartupProblems.Select(StatusLine));
            }

            if (startupMessage is not null)
            {
                outputLines.Add(ErrorLine(startupMessage));
            }

            var editor = new Editor
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                ReadOnly = true,
                Text = string.Join('\n', outputLines),

                // Soft-wrapped: a long reply or error line used to run off the right edge, and moving
                // the caret to the end after each append scrolled the whole pane sideways to that
                // line's end, hiding the start of every line (the "[source]" tags included).
                WordWrap = true,

                // Colors [error]/[dev-term] lines apart from device output - see OutputHighlighting.
                HighlightingDefinition = OutputHighlighting.Definition,

                // The tab strip's own label for this tab - see ConnectionDescription.Subject.
                Title = ConnectionDescription.Subject(sessionTab.CliOptions, profileStore),
            };

            var windowTab = new TuiWindowTab { Tab = sessionTab, Output = editor };
            windowTab.OutputLines.AddRange(outputLines);
            editor.Data = windowTab;

            // Declared then assigned (not `var = new(...)` in one step) because the constructor's own
            // callback closes over this same variable to schedule its drain.
            windowTab.OutputQueue = new BatchedOutputQueue(() => app.Invoke(() => windowTab.OutputQueue!.Drain(lines =>
            {
                windowTab.OutputLines.AddRange(lines);
                var excess = windowTab.OutputLines.Count - _maxOutputLines;
                if (excess > 0)
                {
                    windowTab.OutputLines.RemoveRange(0, excess);
                }

                windowTab.Output.Text = string.Join('\n', windowTab.OutputLines);
                windowTab.Output.CaretOffset = windowTab.Output.Text.Length;
            })));

            // Named (not inline), closing over this specific windowTab (never ActiveTab()) so a
            // profile switch or Close Session can unsubscribe the exact right delegate later.
            windowTab.OutputHandler = (_, presenterOutput) => AppendOutput(windowTab, $"[{presenterOutput.PresenterName}] {presenterOutput.Text}");
            windowTab.DisconnectedHandler = (_, e) =>
            {
                AppendError(windowTab, $"{ConnectionErrorMessages.ForDisconnect(windowTab.Tab.CliOptions.Transport, e.Error)} Use File > Connect to reconnect.");
                app.Invoke(() => RefreshConnectionUi(windowTab));
            };
            sessionTab.Session.Output += windowTab.OutputHandler;
            sessionTab.Session.Disconnected += windowTab.DisconnectedHandler;

            tabs.Add(windowTab);
            tabsView.InsertTab(tabsView.TabCollection.Count(), editor);
            tabsView.Value = editor;
            UpdateCloseSessionAvailability();

            // --log starts logging straight away (the session may already be open - the log's first
            // record says so) for whichever tab it's set on, startup or a later File > New Session.
            if (sessionTab.CliOptions.Log is { Length: > 0 } logOption)
            {
                StartLoggingForTab(windowTab, SessionLogging.ResolveLogPath(logOption, sessionTab.CliOptions, profileStore.FindName(sessionTab.CliOptions), DateTimeOffset.Now));
            }

            return windowTab;
        }

        var sendLabel = new Label
        {
            Text = "Send:",
            X = 0,
            Y = Pos.Bottom(tabsView),
            Width = 6,
        };

        var sendField = new TextField
        {
            X = Pos.Right(sendLabel),
            Y = Pos.Bottom(tabsView),
            Width = Dim.Fill(),
            Enabled = tab.Session.State == ConnectionState.Open,
        };

        // The connection-state indicator: a full-width colored line under the send row (see
        // RefreshConnectionUi) - "● Connected — tcp://…" / "● Disconnected — …".
        var statusLabel = new Label
        {
            X = 0,
            Y = Pos.Bottom(sendLabel),
            Width = Dim.Fill(),
        };

#pragma warning disable IDE0017 // Simplify object initialization
        var connectMenuItem = new MenuItem(
            tab.Session.State == ConnectionState.Open ? "_Disconnect" : "_Connect",
            string.Empty,
            () => { });
        connectMenuItem.Action = () => Observe(ToggleAndRefreshAsync(), line => AppendOutput(ActiveTab(), line));
#pragma warning restore IDE0017 // Simplify object initialization

        async Task ToggleAndRefreshAsync()
        {
            var windowTab = ActiveTab();
            await ToggleConnectionAsync(app, windowTab.Tab.Session, windowTab.Tab.CliOptions, connectMenuItem, sendField, line => AppendOutput(windowTab, line));
            app.Invoke(() => RefreshConnectionUi(windowTab));
        }

        // A menu action that throws would otherwise escape into Application.Run - which only
        // swallows it in RELEASE builds (see RunAsync's errorHandler). Report it and carry on.
        Action Guarded(Action action) => () =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                MessageBox.ErrorQuery(app, "dev-term — error", ex.Message, "Ok");
            }
        };

        // Each tab owns its own SessionLogger (TuiWindowTab.Logger, Step 4) - starting a log on tab B
        // never touches whatever tab A is already logging to.
        bool StartLoggingForTab(TuiWindowTab windowTab, string path)
        {
            StopLoggingForTab(windowTab, report: false);
            try
            {
                windowTab.Logger = SessionLogging.Start(path, windowTab.Tab.Session, windowTab.Tab.CliOptions, windowTab.Tab.Parser, profileStore.FindName(windowTab.Tab.CliOptions), "tui");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                AppendError(windowTab, $"Could not start logging to '{path}': {ex.Message}");
                RefreshConnectionUi(windowTab);
                return false;
            }

            AppendStatus(windowTab, $"Logging to {SessionLogging.DisplayPath(windowTab.Logger.Path!)}.");
            RefreshConnectionUi(windowTab);
            return true;
        }

        void StopLoggingForTab(TuiWindowTab windowTab, bool report = true)
        {
            if (windowTab.Logger is null)
            {
                return;
            }

            var path = windowTab.Logger.Path;
            windowTab.Logger.Dispose();
            windowTab.Logger = null;
            if (report)
            {
                AppendStatus(windowTab, $"Stopped logging to {(path is null ? "the log" : SessionLogging.DisplayPath(path))}.");
            }

            RefreshConnectionUi(windowTab);
        }

        // TuiWindowParts.Logging exposes these two with no tab parameter (its shape predates multi-tab
        // and stays the same) - they act on whichever tab is active at the moment they're called.
        bool StartLogging(string path) => StartLoggingForTab(ActiveTab(), path);
        void StopLogging() => StopLoggingForTab(ActiveTab());

        loggingMenuItem.Action = Guarded(() =>
        {
            var windowTab = ActiveTab();
            if (windowTab.Logger is not null)
            {
                StopLoggingForTab(windowTab);
            }
            else if (TuiLogging.PromptForPath(app, windowTab.Tab.CliOptions, profileStore.FindName(windowTab.Tab.CliOptions)) is { } path)
            {
                StartLoggingForTab(windowTab, path);
            }
        });

        void SetParser(string name)
        {
            // Called from a menu item's action, already on the UI thread - no Application.Invoke
            // needed (and it would never flush under a headless test without a real run loop).
            var windowTab = ActiveTab();
            windowTab.Tab.Parser = name;
            window.Title = windowTab.Tab.Title(profileStore);
        }

        // View > Theme: switching re-applies live through OnThemeChanged below.
        var themeMenu = new TuiThemeMenu(text => AppendStatus(ActiveTab(), text));

        // Marker-in-Title convention for a checkable item - mirrors TuiThemeMenu's own
        // "●"/"  " marker (Terminal.Gui has no native checkable MenuItem in this v2 usage).
        static string ToggleTitle(string label, bool on) => (on ? "● " : "  ") + label;

        var menuBar = new MenuBar(
        [
            new MenuBarItem("_File",
            [
                connectMenuItem,
                deviceProfilesMenuItem = new MenuItem("_Device Profiles...", string.Empty, Guarded(() =>
                {
                    var windowTab = ActiveTab();
                    var configureParts = ConfigureMode.BuildWindow(app, windowTab.Tab.CliOptions, null, profileStore);
                    try
                    {
                        app.Run(configureParts.Window);
                    }
                    finally
                    {
                        configureParts.Window.Dispose();
                    }

                    if (configureParts.Result is { } chosen)
                    {
                        DevTermConfiguration.SaveLocalProfile(chosen);
                        Observe(SwitchProfileAsync(chosen), line => AppendOutput(windowTab, line));
                    }
                })),
                newSessionMenuItem = new MenuItem("_New Session...", string.Empty, Guarded(() =>
                {
                    // Reachable with zero tabs open (Step 4) - seeds from the active tab's options if
                    // there is one, otherwise from whatever the last tab to close was (lastCliOptions),
                    // mirroring MainWindow.xaml.cs's own NewSession_Click.
                    var activeTab = ActiveTabOrNull();
                    var seedOptions = activeTab?.Tab.CliOptions ?? lastCliOptions;
                    var configureParts = ConfigureMode.BuildWindow(app, seedOptions, null, profileStore);
                    try
                    {
                        app.Run(configureParts.Window);
                    }
                    finally
                    {
                        configureParts.Window.Dispose();
                    }

                    if (configureParts.Result is not { } chosen)
                    {
                        return;
                    }

                    DevTermConfiguration.SaveLocalProfile(chosen);
                    SessionTab newSessionTab;
                    try
                    {
                        newSessionTab = SessionTab.Build(chosen);
                    }
                    catch (Exception ex)
                    {
                        if (activeTab is not null)
                        {
                            AppendError(activeTab, $"Could not open a new session: {ex.Message}");
                        }

                        return;
                    }

                    var newTab = AddTab(newSessionTab);
                    Observe(ConnectNewTabAsync(newTab), line => AppendOutput(newTab, line));
                })),
                closeSessionMenuItem = new MenuItem("_Close Session", string.Empty, () =>
                {
                    var tabToClose = ActiveTab();
                    Observe(CloseTabAsync(tabToClose), line => AppendOutput(tabToClose, line));
                }),
                loggingMenuItem,
                new MenuItem("Open Log for _Playback...", string.Empty, Guarded(() => PlaybackMode.OpenAndRun(app, ActiveTab().Tab.CliOptions))),
                new MenuItem("_Quit", string.Empty, Quit, Key.Q.WithCtrl),
            ]),
            // One entry per presenter that can encode typed text; picking one applies from the next
            // line typed on (the title bar shows which is current). Built once, from whichever tab is
            // active at window-build time (the presenter set is the same across every tab a session
            // could realistically use here) rather than rebuilt live on every tab switch - Terminal.Gui
            // has no live-item-replacement story for a MenuBarItem the way WPF's ComboBox.ItemsSource
            // binding does.
            sendAsMenuBarItem = new MenuBarItem("_Send as", [.. tab.Catalog.InputNames.Select(name => new MenuItem(name, string.Empty, () => SetParser(name)))]),
            new MenuBarItem("_Device",
            [
                // Reuses the current, already-open session/connection rather than opening a second
                // competing one to the same physical device - reads the live ActiveTab() tab's
                // Session/Catalog, which SwitchProfileAsync reassigns on a profile switch and which
                // changes altogether on a tab switch.
                k8055MenuItem = new MenuItem("_K8055 Control Panel...", string.Empty, Guarded(() =>
                {
                    var windowTab = ActiveTab();
                    var structuredSource = windowTab.Tab.Catalog.TryGet("k8055", out var presenter) ? presenter : null;
                    var panelParts = ControlPanelMode.BuildWindow(
                        app,
                        K8055UiDefinition.Build(),
                        new K8055ControlSurface(windowTab.Tab.Session),
                        structuredSource,
                        "dev-term — K8055 Control Panel");
                    try
                    {
                        app.Run(panelParts.Window);
                    }
                    finally
                    {
                        panelParts.Window.Dispose();
                    }
                })),
                busylightMenuItem = new MenuItem("_Busylight Control Panel...", string.Empty, Guarded(() =>
                {
                    var windowTab = ActiveTab();
                    var structuredSource = windowTab.Tab.Catalog.TryGet("busylight", out var presenter) ? presenter : null;
                    var panelParts = ControlPanelMode.BuildWindow(
                        app,
                        BusylightUiDefinition.Build(),
                        new BusylightControlSurface(windowTab.Tab.Session),
                        structuredSource,
                        "dev-term — Busylight Control Panel");
                    try
                    {
                        app.Run(panelParts.Window);
                    }
                    finally
                    {
                        panelParts.Window.Dispose();
                    }
                })),
                radexOneMenuItem = new MenuItem("_Radex One Control Panel...", string.Empty, Guarded(() =>
                {
                    var windowTab = ActiveTab();
                    var structuredSource = windowTab.Tab.Catalog.TryGet("radexone", out var presenter) ? presenter : null;
                    var panelParts = ControlPanelMode.BuildWindow(
                        app,
                        RadexOneUiDefinition.Build(),
                        new RadexOneControlSurface(windowTab.Tab.Session),
                        structuredSource,
                        "dev-term — Radex One Control Panel");
                    try
                    {
                        app.Run(panelParts.Window);
                    }
                    finally
                    {
                        panelParts.Window.Dispose();
                    }
                })),
                zoomH4nMenuItem = new MenuItem("_Zoom H4n Remote...", string.Empty, Guarded(() =>
                {
                    var windowTab = ActiveTab();
                    var structuredSource = windowTab.Tab.Catalog.TryGet("zoomh4n", out var presenter) ? presenter : null;
                    var panelParts = ControlPanelMode.BuildWindow(
                        app,
                        ZoomH4nUiDefinition.Build(),
                        new ZoomH4nControlSurface(windowTab.Tab.Session),
                        structuredSource,
                        "dev-term — Zoom H4n Remote");
                    try
                    {
                        app.Run(panelParts.Window);
                    }
                    finally
                    {
                        panelParts.Window.Dispose();
                    }
                })),
                de5000MenuItem = new MenuItem("_DE-5000 LCR Meter...", string.Empty, Guarded(() =>
                {
                    var windowTab = ActiveTab();
                    var structuredSource = windowTab.Tab.Catalog.TryGet("de5000", out var presenter) ? presenter : null;
                    var panelParts = ControlPanelMode.BuildWindow(
                        app,
                        De5000UiDefinition.Build(),
                        new De5000ControlSurface(),
                        structuredSource,
                        "dev-term — DE-5000 LCR Meter");
                    try
                    {
                        app.Run(panelParts.Window);
                    }
                    finally
                    {
                        panelParts.Window.Dispose();
                    }
                })),
                nmea0183MenuItem = new MenuItem("_NMEA 0183...", string.Empty, Guarded(() =>
                {
                    var windowTab = ActiveTab();
                    var structuredSource = windowTab.Tab.Catalog.TryGet("nmea", out var presenter) ? presenter : null;
                    var panelParts = ControlPanelMode.BuildWindow(
                        app,
                        NmeaGpsUiDefinition.Build(),
                        new NmeaGpsControlSurface(),
                        structuredSource,
                        "dev-term — NMEA 0183");
                    app.Run(panelParts.Window);
                })),
                // One generic entry, not one per instrument, unlike the two above - the command set
                // is data (ScpiProfileCatalog), not a hardcoded per-device UiDefinition, so a new
                // instrument is a dropped-in JSON file, not a new menu item.
                scpiMenuItem = new MenuItem("_SCPI Instrument...", string.Empty, Guarded(() =>
                {
                    var windowTab = ActiveTab();
                    var structuredSource = ResolveActiveScpiPresenter(windowTab.Tab.Session, windowTab.Tab.Catalog);
                    var picked = ResolveSavedScpiProfileChoice(windowTab.Tab.CliOptions.ScpiProfile) ?? PickScpiProfileChoice(app);
                    if (picked is null)
                    {
                        return;
                    }

                    if (picked == _scpiAutoDetectChoice)
                    {
                        // *IDN? is a real send/await over the live transport - unlike the two panels
                        // above, this can't finish before the menu action returns, so it's fire-and-
                        // forget with the eventual window open marshaled back via Application.Invoke,
                        // the same pattern ToggleConnectionAsync/SwitchProfileAsync use for the same
                        // reason (real async I/O resumes off the UI thread).
                        Observe(DetectAndOpenScpiInstrumentAsync(app, windowTab.Tab.Session, structuredSource, windowTab.Tab.CliOptions.ScpiAutoDetectTimeoutMs, text => AppendStatus(windowTab, text), text => AppendError(windowTab, text)), line => AppendOutput(windowTab, line));
                        return;
                    }

                    var profile = picked == _scpiGenericChoice
                        ? ScpiProfileCatalog.Generic
                        : ScpiProfileCatalog.All.First(p => p.Name == picked);
                    OpenScpiInstrumentWindow(app, windowTab.Tab.Session, structuredSource, profile);
                })),
                manifestMenuItem = new MenuItem("Device _Manifest...", string.Empty, Guarded(() => OpenDeviceManifest(app, ActiveTab().Tab.Session))),

                // Always available: editing a manifest needs no connection (see ManifestEditorMode).
                new MenuItem("_Edit Device Manifest...", string.Empty, Guarded(() => ManifestEditorMode.Run(app))),
                streamMonitorMenuItem = new MenuItem("S_tream Monitor...", string.Empty, Guarded(OpenStreamMonitor)),
            ]),
            new MenuBarItem("_View",
            [
                themeMenu.ThemeMenuItem,

                // Always available: building/editing a theme needs no connection (see ThemeBuilderMode).
                new MenuItem("_Build/Edit Theme...", string.Empty, Guarded(() => ThemeBuilderMode.Run(app))),
                echoSentCommandsMenuItem = new MenuItem(ToggleTitle("_Echo Sent Commands", false), string.Empty, () =>
                {
                    echoSentCommands = !echoSentCommands;
                    echoSentCommandsMenuItem!.Title = ToggleTitle("_Echo Sent Commands", echoSentCommands);
                }),
                // Only meaningful for a TCP transport; RefreshConnectionUi enables/disables and
                // re-marks this per the active tab, the same way the Device menu items are gated.
                softwareFlowControlMenuItem = new MenuItem(ToggleTitle("_Software Flow Control (XON/XOFF)", false), string.Empty, () =>
                {
                    if (ActiveTab().Tab.Session.Transport is TcpTransport tcp)
                    {
                        tcp.SoftwareFlowControl = !tcp.SoftwareFlowControl;
                        softwareFlowControlMenuItem!.Title = ToggleTitle("_Software Flow Control (XON/XOFF)", tcp.SoftwareFlowControl);
                    }
                }),
                clearOutputMenuItem = new MenuItem("Clea_r Output", string.Empty, () =>
                {
                    var windowTab = ActiveTab();
                    windowTab.OutputLines.Clear();
                    windowTab.Output.Text = string.Empty;
                }),
            ]),
        ]);

        // A theme switch (View > Theme, from this window or any other) re-applies everything themed:
        // Terminal.Gui's schemes, every open tab's output pane highlighting (XSHD colors are fixed per
        // definition, so each is swapped for the new theme's), and the status line. Raised on the thread
        // that selected - the UI thread, from a menu action - so no app.Invoke (which would never
        // flush under a headless test). Selected from any other thread, it's marshaled over instead;
        // a window whose application has already shut down just unsubscribes.
        var uiThreadId = Environment.CurrentManagedThreadId;
        void OnThemeChanged(object? sender, EventArgs e)
        {
            if (app.Driver is null)
            {
                ActiveTheme.Changed -= OnThemeChanged;
                return;
            }

            if (Environment.CurrentManagedThreadId != uiThreadId)
            {
                app.Invoke(ReapplyTheme);
                return;
            }

            ReapplyTheme();
        }

        void ReapplyTheme()
        {
            TuiTheme.Apply(ActiveTheme.Current);
            foreach (var windowTab in tabs)
            {
                windowTab.Output.HighlightingDefinition = OutputHighlighting.Definition;
            }

            themeMenu.Refresh();
            RefreshConnectionUi(ActiveTab());
            window.SetNeedsDraw();
        }

        ActiveTheme.Changed += OnThemeChanged;
        window.Disposing += (_, _) => ActiveTheme.Changed -= OnThemeChanged;

        // Everything that depends on one tab's connection state: its own tab header, and - only if
        // it's still the active tab by the time this runs - the shared chrome that follows the active
        // tab (the File menu label, the send field, the window title, the status line, and which
        // Device panels make sense). Called on the UI thread - directly while building, via app.Invoke
        // after any connect/disconnect/fault/profile switch, or via tabsView.ValueChanged on a tab
        // switch. Mirrors MainWindow.xaml.cs's own two-purpose RefreshConnectionUi(WindowTab, ...).
        void RefreshConnectionUi(TuiWindowTab windowTab)
        {
            var state = windowTab.Tab.Session.State;
            var connected = state == ConnectionState.Open;

            windowTab.Output.Title = ConnectionDescription.Subject(windowTab.Tab.CliOptions, profileStore);

            if (!ReferenceEquals(windowTab, ActiveTab()))
            {
                return;
            }

            connectMenuItem.Title = connected ? "_Disconnect" : "_Connect";
            sendField.Enabled = connected;
            window.Title = windowTab.Tab.Title(profileStore);
            loggingMenuItem.Title = windowTab.Logger is null ? TuiLogging.StartTitle : TuiLogging.StopTitle;

            statusLabel.Text = $" ● {ConnectionDescription.StatusText(windowTab.Tab.CliOptions, state)}{TuiLogging.StatusSuffixFor(windowTab.Logger)}";
            statusLabel.SetScheme(TuiTheme.Solid(TuiTheme.StatusAttribute(ActiveTheme.Current, state)));

            // Re-enables chrome HandleZeroTabs disabled, for whenever a tab becomes active again
            // after the window was briefly empty (Step 4).
            deviceProfilesMenuItem!.Enabled = true;
            streamMonitorMenuItem!.Enabled = true;
            sendAsMenuBarItem!.Enabled = true;

            k8055MenuItem!.Enabled = DevicePanels.IsAvailable(DevicePanel.K8055, windowTab.Tab.CliOptions, connected);
            busylightMenuItem!.Enabled = DevicePanels.IsAvailable(DevicePanel.Busylight, windowTab.Tab.CliOptions, connected);
            scpiMenuItem!.Enabled = DevicePanels.IsAvailable(DevicePanel.Scpi, windowTab.Tab.CliOptions, connected);
            radexOneMenuItem!.Enabled = DevicePanels.IsAvailable(DevicePanel.RadexOne, windowTab.Tab.CliOptions, connected);
            zoomH4nMenuItem!.Enabled = DevicePanels.IsAvailable(DevicePanel.ZoomH4n, windowTab.Tab.CliOptions, connected);
            de5000MenuItem!.Enabled = DevicePanels.IsAvailable(DevicePanel.De5000, windowTab.Tab.CliOptions, connected);
            nmea0183MenuItem!.Enabled = DevicePanels.IsAvailable(DevicePanel.Nmea0183, windowTab.Tab.CliOptions, connected);
            manifestMenuItem!.Enabled = DevicePanels.IsAvailable(DevicePanel.Manifest, windowTab.Tab.CliOptions, connected);

            if (windowTab.Tab.Session.Transport is TcpTransport tcp)
            {
                softwareFlowControlMenuItem!.Enabled = true;
                softwareFlowControlMenuItem!.Title = ToggleTitle("_Software Flow Control (XON/XOFF)", tcp.SoftwareFlowControl);
            }
            else
            {
                softwareFlowControlMenuItem!.Enabled = false;
                softwareFlowControlMenuItem!.Title = ToggleTitle("_Software Flow Control (XON/XOFF)", false);
            }
        }

        // Quitting the whole app (as opposed to Ctrl+Q/Escape just closing a nested panel - see
        // quitOnCtrlQ below) confirms first, but only when some tab has a live connection to lose; if
        // every tab is already disconnected, there's nothing worth confirming.
        void Quit()
        {
            if (app.TopRunnableView == window
                && tabs.Any(t => t.Tab.Session.State == ConnectionState.Open)
                && MessageBox.Query(app, "dev-term", "Quit dev-term? This closes the current connection.", ["Yes", "No"]) != 0)
            {
                return;
            }

            app.RequestStop();
        }

        // The Quit MenuItem's own "Ctrl+Q" Key argument only labels the shortcut in the menu's
        // display text - it doesn't register a live, always-active key binding by itself (checked
        // directly: after building this exact menu, neither the Window's nor the MenuBar's own
        // KeyBindings contained a Ctrl+Q entry). A window-level KeyDown handler, the same pattern
        // sendField's own Enter handling already uses below, is what actually makes Ctrl+Q work
        // from anywhere in the window, not just while the menu itself is open.
        // Application.KeyDown (global) rather than window.KeyDown: a plain per-view KeyDown
        // handler on the window doesn't reliably see keys that were already routed to a focused
        // child first (sendField has focus in normal use) - checked directly, Ctrl+Q reached
        // window.KeyDown when nothing else had focus but not once sendField did. Application.KeyDown
        // fires ahead of per-view focus routing, so it works regardless of what's currently focused.
        //
        // app.RequestStop() with no argument only ever stops whatever run loop is currently
        // topmost - a nested app.Run(nestedWindow) for a device control panel, say - so Ctrl+Q
        // already behaves like Escape for those (closes just that panel) with no extra handling
        // here; Quit() only prompts once TopRunnableView is this main window, i.e. Ctrl+Q is
        // actually about to end the app.
        void quitOnCtrlQ(object? _, Key key)
        {
            // The Connection Editor (File > Device Profiles.../New Session...) quits itself on Ctrl+Q,
            // with its unsaved-changes prompt; stopping it from here would skip that prompt.
            if (key != Key.Q.WithCtrl || key.Handled || ConfigureMode.OwnsQuitKey(app.TopRunnableView))
            {
                return;
            }

            key.Handled = true;
            Quit();
        }

        app.Keyboard.KeyDown += quitOnCtrlQ;
        window.Disposing += (_, _) => app.Keyboard.KeyDown -= quitOnCtrlQ;

        // Tears down the active tab's current session/transport and opens a new one composed from
        // newOptions - live, without restarting the app, unlike the save-as-default-and-ask-for-a-
        // restart this replaced. Reassigns the active TuiWindowTab's Session/Catalog/CliOptions/Parser
        // properties directly (the TuiWindowTab itself is never replaced, only its Tab's properties) -
        // every other closure that resolves ActiveTab() afresh sees the switch without needing to be
        // individually re-wired. Must run under a real Application.Run() loop (RunWithLoop in tests,
        // never RunHeadless) - it calls Application.Invoke like ToggleConnectionAsync above, which
        // silently never flushes otherwise (see CLAUDE.md).
        // A slow-to-fail connect (an unreachable host that never actively refuses, so it sits on
        // the OS connect timeout) can still be pending when the user switches to a *different*
        // host, and without windowTab.SwitchCts, the earlier attempt's success/failure handler ran
        // anyway once it finally resolved - using the by-then-stale CliOptions - and stomped
        // connectMenuItem.Title/sendField.Enabled/output back over whatever the newer attempt had
        // already set. Real TCP connects honor cancellation (unlike SerialPort/HidStream - see
        // CLAUDE.md), so the superseded attempt now fails fast instead of leaking a connect in the
        // background.
        async Task<bool> SwitchProfileAsync(CliOptions newOptions)
        {
            var windowTab = ActiveTab();
            windowTab.SwitchCts?.Cancel();
            var cts = new CancellationTokenSource();
            windowTab.SwitchCts = cts;

            DevTermSessionBuilder.Result built;
            try
            {
                built = DevTermSessionBuilder.Build(newOptions);
            }
            catch (Exception ex)
            {
                AppendError(windowTab, $"Could not switch profile: {ex.Message}");
                return false;
            }

            var mySession = built.Session;

            // Captured now, before any await: a second, overlapping switch on this same tab reassigns
            // windowTab.Tab.Session below (once its own build/close/dispose completes) while this call
            // is still suspended closing/disposing its OWN old session. Reading windowTab.Tab.Session
            // again after that await - instead of this local - would tear down whatever the OTHER
            // call had already installed there (possibly its brand-new, just-opened session) rather
            // than the session this call actually meant to replace.
            var oldSession = windowTab.Tab.Session;

            oldSession.Output -= windowTab.OutputHandler;
            oldSession.Disconnected -= windowTab.DisconnectedHandler;
            await oldSession.CloseAsync();
            await oldSession.DisposeAsync();

            if (!ReferenceEquals(windowTab.SwitchCts, cts))
            {
                // Superseded while closing the old session, before ever adopting mySession as
                // current - a newer switch on this tab has already moved it on (possibly to its own,
                // by-now-open session). Never having been subscribed or assigned to the tab, mySession
                // just needs disposing.
                await mySession.DisposeAsync();
                return false;
            }

            windowTab.Tab.Session = mySession;
            windowTab.Tab.Catalog = built.Catalog;
            windowTab.Tab.CliOptions = newOptions;
            windowTab.Tab.Parser = newOptions.EffectiveParser;
            windowTab.Monitor?.SetSession(mySession, StreamMonitor.DeviceNameFor(newOptions, profileStore), newOptions.EffectiveExportDirectory);
            mySession.Output += windowTab.OutputHandler;
            mySession.Disconnected += windowTab.DisconnectedHandler;
            SessionLogging.Follow(windowTab.Logger, mySession, newOptions, profileStore.FindName(newOptions));

            app.Invoke(() =>
            {
                // Clear the backing list too - clearing only Output.Text brought the previous
                // connection's lines straight back on the next AppendOutput, which rebuilds the text
                // from the list.
                windowTab.OutputLines.Clear();
                windowTab.Output.Text = string.Empty;
                RefreshConnectionUi(windowTab);
            });

            if (ManifestNameWarning.For(windowTab.Tab.CliOptions) is { } manifestWarning)
            {
                AppendStatus(windowTab, manifestWarning);
            }

            try
            {
                await mySession.OpenAsync(cts.Token);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                // Superseded by a newer switch before this one finished connecting - that newer
                // attempt owns the UI now, so this stale one reports nothing.
                return false;
            }
            catch (Exception ex)
            {
                if (!ReferenceEquals(windowTab.SwitchCts, cts))
                {
                    // Superseded between the failure and this catch running - don't stomp the
                    // newer attempt's state with a stale one.
                    return false;
                }

                AppendError(windowTab, ConnectionErrorMessages.For(windowTab.Tab.CliOptions.Transport, ex));
                app.Invoke(() => RefreshConnectionUi(windowTab));
                return false;
            }

            if (!ReferenceEquals(windowTab.SwitchCts, cts))
            {
                // Connected, but superseded in the meantime - close it rather than adopting a
                // stray connection as current.
                mySession.Output -= windowTab.OutputHandler;
                mySession.Disconnected -= windowTab.DisconnectedHandler;
                await mySession.CloseAsync();
                await mySession.DisposeAsync();
                return false;
            }

            app.Invoke(() => RefreshConnectionUi(windowTab));
            AppendStatus(windowTab, $"Switched to {ConnectionDescription.For(windowTab.Tab.CliOptions)}.");
            return true;
        }

        // File > New Session... has already built and added the tab (AddTab, so the tab strip/output
        // pane exist immediately) by the time this runs - it only needs to actually open the
        // connection, mirroring what MainWindow.xaml.cs's NewSession_Click gets for free from
        // ConnectAsync() (already wired to WPF's Loaded event pre-multi-tab), which TUI has no
        // equivalent of since RunAsync opens the startup session before BuildWindow is ever called.
        async Task ConnectNewTabAsync(TuiWindowTab windowTab)
        {
            try
            {
                await windowTab.Tab.Session.OpenAsync();
            }
            catch (Exception ex)
            {
                AppendError(windowTab, ConnectionErrorMessages.For(windowTab.Tab.CliOptions.Transport, ex));
                app.Invoke(() => RefreshConnectionUi(windowTab));
                return;
            }

            AppendStatus(windowTab, $"Connected to {ConnectionDescription.For(windowTab.Tab.CliOptions)}.");
            app.Invoke(() => RefreshConnectionUi(windowTab));
        }

        // File > Close Session: a no-op with only one tab left, mirroring MainWindow.xaml.cs's own
        // Step 2 narrowing (docs/design/multi-session-ui.md's Open questions recommend the window
        // staying open with zero tabs; true zero-tab support is deferred to Step 4, same as WPF).
        // Unlike WPF, there's no _openControlPanels-equivalent tracking to close here: a TUI device
        // control panel is opened via a nested, blocking app.Run(panelParts.Window) from within a menu
        // action, so it's structurally modal to the whole app loop and can never outlive the Close
        // Session action running concurrently with it.
        async Task CloseTabAsync(TuiWindowTab windowTab)
        {
            if (!tabs.Contains(windowTab))
            {
                return;
            }

            StopLoggingForTab(windowTab, report: false);
            windowTab.Monitor?.Dispose();
            windowTab.Tab.Session.Output -= windowTab.OutputHandler;
            windowTab.Tab.Session.Disconnected -= windowTab.DisconnectedHandler;
            try
            {
                await windowTab.Tab.Session.CloseAsync();
                await windowTab.Tab.Session.DisposeAsync();
            }
            catch (Exception)
            {
            }

            // Kept as File > New Session...'s seed once this was the last tab open - see its own
            // comment above (mirrors MainWindow.xaml.cs's own _lastCliOptions).
            lastCliOptions = windowTab.Tab.CliOptions;

            tabs.Remove(windowTab);
            tabsView.Remove(windowTab.Output);
            if (tabs.Count > 0)
            {
                tabsView.Value = tabs[0].Output;
            }

            windowTab.Output.Dispose();
            UpdateCloseSessionAvailability();

            if (tabs.Count == 0)
            {
                HandleZeroTabs();
            }
        }

        // Step 4: the window stays open with zero tabs rather than exiting or crashing - every piece
        // of chrome that assumes an active tab (ActiveTab(), not ActiveTabOrNull()) gets disabled here,
        // mirroring what MainWindow.xaml.cs's own zero-tab state does for WPF (RefreshConnectionUi's
        // WPF equivalent no-ops, disabling the same set of controls).
        void HandleZeroTabs()
        {
            connectMenuItem.Title = "_Connect";
            deviceProfilesMenuItem!.Enabled = false;
            streamMonitorMenuItem!.Enabled = false;
            k8055MenuItem!.Enabled = false;
            busylightMenuItem!.Enabled = false;
            scpiMenuItem!.Enabled = false;
            radexOneMenuItem!.Enabled = false;
            zoomH4nMenuItem!.Enabled = false;
            de5000MenuItem!.Enabled = false;
            nmea0183MenuItem!.Enabled = false;
            manifestMenuItem!.Enabled = false;
            sendAsMenuBarItem!.Enabled = false;
            loggingMenuItem.Title = TuiLogging.StartTitle;
            sendField.Text = string.Empty;
            sendField.Enabled = false;
            window.Title = "dev-term";
            statusLabel.Text = " ○ No sessions open — use File > New Session... to start one.";
            statusLabel.SetScheme(TuiTheme.Solid(TuiTheme.StatusAttribute(ActiveTheme.Current, ConnectionState.Closed)));
        }

        // Device > Stream Monitor...: opening it starts monitoring the active tab's session (that's
        // what opening it is for); its Stop button stops it. The monitor is per-tab (TuiWindowTab.Monitor,
        // Step 4) and outlives the modal window so captures keep being auto-saved - each reported as a
        // status line here, against the tab it belongs to - while the user is back in this window
        // sending commands, possibly on a different tab by then. A later profile switch on that same
        // tab moves it via SwitchProfileAsync's own windowTab.Monitor.SetSession call.
        void OpenStreamMonitor()
        {
            var windowTab = ActiveTab();
            if (windowTab.Monitor is null)
            {
                var monitor = new StreamMonitor();
                monitor.CaptureAdded += (_, capture) => AppendStatus(windowTab, capture.Describe());
                windowTab.Monitor = monitor;
            }

            windowTab.Monitor.SetSession(windowTab.Tab.Session, StreamMonitor.DeviceNameFor(windowTab.Tab.CliOptions, profileStore), windowTab.Tab.CliOptions.EffectiveExportDirectory);
            windowTab.Monitor.Start();

            var monitorParts = StreamMonitorMode.BuildWindow(app, windowTab.Monitor);
            app.Run(monitorParts.Window);
            monitorParts.Window.Dispose();
        }

        sendField.KeyDown += (_, key) =>
        {
            var windowTab = ActiveTab();

            if (key == Key.CursorUp)
            {
                key.Handled = true;
                if (windowTab.Tab.SendHistory.Previous() is { } older)
                {
                    sendField.Text = older;
                    sendField.MoveEnd();
                }

                return;
            }

            if (key == Key.CursorDown)
            {
                key.Handled = true;
                if (windowTab.Tab.SendHistory.Next() is { } newer)
                {
                    sendField.Text = newer;
                    sendField.MoveEnd();
                }

                return;
            }

            if (key != Key.Enter)
            {
                return;
            }

            key.Handled = true;
            var line = sendField.Text;
            sendField.Text = string.Empty;
            windowTab.Tab.SendHistory.Add(line);

            if (line.Length == 0)
            {
                return;
            }

            if (windowTab.Tab.Session.State != ConnectionState.Open)
            {
                AppendError(windowTab, "Not connected — use File > Connect.");
                return;
            }

            if (!windowTab.Tab.Catalog.TryGetInput(windowTab.Tab.Parser, out var input))
            {
                AppendError(windowTab, $"Parser '{windowTab.Tab.Parser}' does not support sending.");
                return;
            }

            Observe(SendAsync(windowTab.Tab.Session, windowTab.Tab.CliOptions, input, line, l => AppendOutput(windowTab, l), windowTab.Tab.Parser, echoSentCommands ? sent => AppendOutput(windowTab, $"Out> {TypedInput.FormatForEcho(sent, windowTab.Tab.CliOptions.LineEnding)}") : null), l => AppendOutput(windowTab, l));
        };

        // Shared chrome (the File menu label, the send field, the window title, the status line, the
        // Device menu) rebinds to the newly active tab - mirrors MainWindow.xaml.cs's own
        // SessionTabs_SelectionChanged, simplified since there's no ParserBox/combobox to rebind here
        // (the "_Send as" menu is intentionally not rebuilt per tab - see its own comment above).
        tabsView.ValueChanged += (_, _) =>
        {
            sendField.Text = string.Empty;
            if (ActiveTabOrNull() is { } activeTab)
            {
                RefreshConnectionUi(activeTab);
            }
        };

        // Ctrl+Tab/Ctrl+Shift+Tab cycle the active tab; a no-op below two tabs.
        void SelectAdjacentTab(int direction)
        {
            if (tabs.Count < 2 || ActiveTabOrNull() is not { } activeTab)
            {
                return;
            }

            var currentIndex = tabs.IndexOf(activeTab);
            var nextIndex = ((currentIndex + direction) % tabs.Count + tabs.Count) % tabs.Count;
            tabsView.Value = tabs[nextIndex].Output;
        }

        // Ctrl+T/Ctrl+W/Ctrl+Tab/Ctrl+Shift+Tab (Step 4) - same Application.KeyDown pattern as
        // quitOnCtrlQ above (a per-view KeyDown handler on the window doesn't reliably see a key
        // already routed to the focused sendField first), gated the same way on this being the
        // topmost run loop so a nested device panel/dialog isn't hijacked by these shortcuts.
        void sessionShortcuts(object? _, Key key)
        {
            if (app.TopRunnableView != window || key.Handled)
            {
                return;
            }

            if (key == Key.T.WithCtrl)
            {
                key.Handled = true;
                newSessionMenuItem!.Action!.Invoke();
                return;
            }

            if (key == Key.W.WithCtrl)
            {
                if (ActiveTabOrNull() is { } activeTab)
                {
                    key.Handled = true;
                    Observe(CloseTabAsync(activeTab), line => AppendOutput(activeTab, line));
                }

                return;
            }

            if (key == Key.Tab.WithCtrl)
            {
                key.Handled = true;
                SelectAdjacentTab(1);
                return;
            }

            if (key == Key.Tab.WithCtrl.WithShift)
            {
                key.Handled = true;
                SelectAdjacentTab(-1);
            }
        }

        app.Keyboard.KeyDown += sessionShortcuts;
        window.Disposing += (_, _) => app.Keyboard.KeyDown -= sessionShortcuts;

        window.Add(menuBar, tabsView, sendLabel, sendField, statusLabel);

        // Everything AddTab might synchronously touch (RefreshConnectionUi, StartLoggingForTab) now
        // exists, so the startup connection can become the first tab, wrapped identically to every
        // other tab File > New Session... adds later.
        var firstTab = AddTab(tab, initialMessage);
        RefreshConnectionUi(firstTab);

        return new TuiWindowParts(
            window,
            firstTab.Output,
            sendField,
            connectMenuItem,
            SwitchProfileAsync,
            SetParser,
            statusLabel,
            k8055MenuItem!,
            busylightMenuItem!,
            scpiMenuItem!,
            ToggleAndRefreshAsync,
            new TuiLoggingParts(loggingMenuItem, StartLogging, StopLogging, () => ActiveTabOrNull()?.Logger),
            themeMenu,
            () => ActiveTab().Tab.Session,
            streamMonitorMenuItem!,
            () => ActiveTabOrNull()?.Monitor,
            tabsView,
            newSessionMenuItem!,
            closeSessionMenuItem!,
            () => tabs.Select(t => t.Tab.Session).ToList(),
            () =>
            {
                foreach (var t in tabs)
                {
                    t.Logger?.Dispose();
                    t.Monitor?.Dispose();
                }
            },
            echoSentCommandsMenuItem!,
            clearOutputMenuItem!,
            softwareFlowControlMenuItem!);
    }

    /// <summary>
    /// The File > Connect/Disconnect menu item's action: closes an open session, or reopens a
    /// closed one, updating the menu item's own label (Terminal.Gui's <c>MenuItem.Title</c> is
    /// mutable, unlike its <c>Key</c> shortcut argument — see the Ctrl+Q comment above) and the
    /// send field's enabled state to match. Exposed as a testable method (not just reachable
    /// through the menu item's <c>Action</c> delegate) the same way <see cref="SendAsync"/> is.
    /// </summary>
    internal static async Task ToggleConnectionAsync(IApplication app, Session session, CliOptions cliOptions, MenuItem connectMenuItem, TextField sendField, Action<string> appendOutput)
    {
        if (session.State == ConnectionState.Open)
        {
            await session.CloseAsync();
            app.Invoke(() =>
            {
                connectMenuItem.Title = "_Connect";
                sendField.Enabled = false;
            });
            appendOutput(StatusLine("Disconnected."));
            return;
        }

        try
        {
            await session.OpenAsync();
        }
        catch (Exception ex)
        {
            appendOutput(ErrorLine(ConnectionErrorMessages.For(cliOptions.Transport, ex)));

            // Unlike SwitchProfileAsync, this reuses the same session/transport rather than
            // building a fresh one - but the menu title/send field still need to reflect "not
            // connected" on a failed *retry*, not just a failed first attempt (BuildWindow already
            // set them correctly for that case before this was ever wired up).
            app.Invoke(() =>
            {
                connectMenuItem.Title = "_Connect";
                sendField.Enabled = false;
            });
            return;
        }

        app.Invoke(() =>
        {
            connectMenuItem.Title = "_Disconnect";
            sendField.Enabled = true;
        });
        appendOutput(StatusLine($"Connected to {ConnectionDescription.For(cliOptions)}."));
    }

    /// <summary>
    /// Encodes and sends one typed line. A line the parser rejects is reported and not sent (the
    /// connection is left alone); a device-side failure has already disconnected the session and
    /// been reported by its <see cref="Session.Disconnected"/> handler, so it isn't reported twice.
    /// <paramref name="onSending"/>, if given, runs with the raw typed line right after a successful
    /// encode (echo for View &gt; Echo Sent Commands) - never for a line the parser rejected or that
    /// encoded to nothing. Never throws.
    /// </summary>
    internal static async Task SendAsync(Session session, CliOptions cliOptions, IPresenterInput input, string line, Action<string> appendOutput, string? parserName = null, Action<string>? onSending = null)
    {
        if (!TypedInput.TryEncode(input, parserName ?? cliOptions.EffectiveParser, line, cliOptions.LineEnding, out var payload, out var error))
        {
            appendOutput(ErrorLine(error!));
            return;
        }

        if (payload.Length == 0)
        {
            return;
        }

        onSending?.Invoke(line);

        try
        {
            await session.SendAsync(payload);
        }
        catch (Exception ex)
        {
            if (session.State == ConnectionState.Open)
            {
                appendOutput(ErrorLine($"Send failed: {ex.Message}"));
            }
        }
    }

    /// <summary>
    /// Observes a fire-and-forget task (a menu action or key handler can't await): anything it
    /// throws is reported in the output pane instead of silently vanishing as an unobserved task
    /// exception.
    /// </summary>
    /// <summary>An app status line in the output pane - tagged so it can't be mistaken for device output.</summary>
    internal static string StatusLine(string text) => $"[dev-term] {text}";

    /// <summary>An error line in the output pane - see <see cref="StatusLine"/>.</summary>
    internal static string ErrorLine(string text) => $"[error] {text}";

    private static void Observe(Task task, Action<string> appendOutput) =>
        _ = task.ContinueWith(
            t => appendOutput(ErrorLine($"Unexpected error: {t.Exception!.GetBaseException().Message}")),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

    /// <summary>Centralized on <see cref="ScpiProfileCatalog.AutoDetectChoiceName"/> so a saved <c>CliOptions.ScpiProfile</c> choice and this picker always agree on the exact same literal.</summary>
    private const string _scpiAutoDetectChoice = ScpiProfileCatalog.AutoDetectChoiceName;

    private static readonly string _scpiGenericChoice = ScpiProfileCatalog.Generic.Name;

    /// <summary>
    /// Resolves the registered "scpi" presenter and binds it into <paramref name="session"/>'s live
    /// pipeline if it isn't there already. <see cref="PresenterCatalog.TryGet"/> alone resolves the
    /// DI-registered singleton regardless of whether the user selected "scpi" for this connection
    /// (the pipeline is normally fixed at session-build time from
    /// <see cref="CliOptions.EffectivePresenters"/>), which used to silently break query/reply
    /// correlation: a Measure-style button still sent and the device still beeped, but the reply was
    /// never routed through <c>ScpiReplyPresenter</c> so it never appeared anywhere — see
    /// docs/changes/2026-09-23.md's real-hardware report. The fix binds the presenter onto the
    /// session's existing <see cref="Session.Presenters"/>/<see cref="Pipeline"/> instance in place
    /// (<see cref="Session.AddPresenter"/>) rather than resolving/rebuilding a new pipeline: the read
    /// loop already holds a reference to this one, immutable-from-the-outside instance for the whole
    /// life of the session, so anything not mutated into that same instance would never be seen by
    /// it.
    /// </summary>
    private static IPresenter? ResolveActiveScpiPresenter(Session session, PresenterCatalog catalog)
    {
        if (!catalog.TryGet("scpi", out var presenter))
        {
            return null;
        }

        session.AddPresenter(presenter);
        return presenter;
    }

    /// <summary>
    /// Resolves a saved <see cref="CliOptions.ScpiProfile"/> choice to a picker-equivalent string,
    /// or <see langword="null"/> if it's unset/no longer resolvable — the latter falls back to
    /// <see cref="PickScpiProfileChoice"/> exactly as if nothing had been saved.
    /// </summary>
    private static string? ResolveSavedScpiProfileChoice(string? saved)
    {
        if (string.IsNullOrWhiteSpace(saved))
        {
            return null;
        }

        if (saved == _scpiAutoDetectChoice || saved == _scpiGenericChoice || ScpiProfileCatalog.All.Any(p => p.Name == saved))
        {
            return saved;
        }

        return null;
    }

    /// <summary>
    /// Runs the shared <see cref="ScpiAutoDetect"/> with the connection's configured timeout,
    /// reporting progress in the output pane while it waits and what it found afterward, then opens
    /// the matched profile's panel (or Generic).
    /// </summary>
    private static async Task DetectAndOpenScpiInstrumentAsync(IApplication app, Session session, IPresenter? structuredSource, int timeoutMs, Action<string> appendStatus, Action<string> appendError)
    {
        var timeout = TimeSpan.FromMilliseconds(timeoutMs);
        appendStatus(ScpiAutoDetect.ProgressMessage(timeout));

        ScpiAutoDetectResult result;
        try
        {
            result = await ScpiAutoDetect.DetectAsync(session, structuredSource, timeout);
        }
        catch (Exception ex)
        {
            // The *IDN? send failed - the session has disconnected itself and reported why, so
            // there's no connection to open a panel against.
            appendError($"SCPI auto-detect failed: {ex.Message}");
            return;
        }

        appendStatus(result.Describe(timeout));
        app.Invoke(() =>
        {
            try
            {
                OpenScpiInstrumentWindow(app, session, structuredSource, result.Profile ?? ScpiProfileCatalog.Generic);
            }
            catch (Exception ex)
            {
                MessageBox.ErrorQuery(app, "dev-term — error", ex.Message, "Ok");
            }
        });
    }

    private static void OpenScpiInstrumentWindow(IApplication app, Session session, IPresenter? structuredSource, ScpiInstrumentProfile profile)
    {
        if (structuredSource is ScpiReplyPresenter replyPresenter)
        {
            replyPresenter.ConfigureTerminator(profile.Terminator);
        }

        var panelParts = ControlPanelMode.BuildWindow(
            app,
            ScpiUiDefinitionBuilder.Build(profile),
            new ScpiControlSurface(session, profile, structuredSource as IScpiReplyTracker),
            structuredSource,
            $"dev-term — {profile.Name}");
        try
        {
            app.Run(panelParts.Window);
        }
        finally
        {
            panelParts.Window.Dispose();
        }
    }

    /// <summary>Device > Device Manifest...: pick a manifest and open its panel on the live session (see <see cref="ManifestPanelMode"/>).</summary>
    private static void OpenDeviceManifest(IApplication app, Session session) => ManifestPanelMode.PickAndRun(app, session);

    internal static string? PickScpiProfileChoice(IApplication app)
    {
        var items = new List<string> { _scpiAutoDetectChoice, _scpiGenericChoice };
        items.AddRange(ScpiProfileCatalog.All.Select(p => p.Name));
        return PickFromList(app, "Select SCPI Instrument", items);
    }

    /// <summary>
    /// A small nested modal picker, the same plain Dialog+ListView pattern <c>ConfigureMode</c>'s own
    /// local <c>PickFromList</c> uses for the same reason (no built-in combobox widget in the
    /// installed Terminal.Gui v2.5.0 — see docs/changes/2026-09-16.md).
    /// </summary>
    private static string? PickFromList(IApplication app, string title, IReadOnlyList<string> items)
    {
        string? picked = null;
        var dialog = new Dialog { Title = title, Width = FormRenderer.ListDialogWidth(app, items), Height = Math.Min(items.Count + 5, 20) };
        var listView = new ListView { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(2) };
        listView.SetSource(new ObservableCollection<string>(items));
        listView.Accepting += (_, e) =>
        {
            if (listView.SelectedItem is int index && index >= 0 && index < items.Count)
            {
                picked = items[index];
            }

            e.Handled = true;
            app.RequestStop();
        };
        var selectButton = new Button { X = 0, Y = Pos.Bottom(listView), Text = "Select", IsDefault = true };
        selectButton.Accepting += (_, e) =>
        {
            if (listView.SelectedItem is int index && index >= 0 && index < items.Count)
            {
                picked = items[index];
            }

            e.Handled = true;
            app.RequestStop();
        };
        var cancelButton = new Button { X = Pos.Right(selectButton) + 1, Y = Pos.Top(selectButton), Text = "Cancel" };
        cancelButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            app.RequestStop();
        };
        dialog.Add(listView, selectButton, cancelButton);
        try
        {
            app.Run(dialog);
        }
        finally
        {
            dialog.Dispose();
        }

        return picked;
    }
}

/// <summary>
/// The controls a test needs to drive the TUI headlessly. <see cref="Output"/> is the startup tab's
/// output <see cref="Editor"/> (every pre-multi-tab test drives exactly one tab, so this stays a
/// stable reference rather than a delegate); <see cref="TabsView"/> gives a multi-tab test access to
/// the tab strip itself (tab count, headers, and switching the active tab via its <c>Value</c>
/// setter). Inject keys into <see cref="SendField"/>, read rendered text back from <see cref="Output"/>,
/// drive a live profile switch directly via <see cref="SwitchProfileAsync"/> (the same delegate the
/// "File &gt; Device Profiles..." menu item calls, acting on whichever tab is active), switch the send
/// format via <see cref="SetParser"/> (what a "Send as" menu item calls), or drive
/// <see cref="NewSessionMenuItem"/>/<see cref="CloseSessionMenuItem"/> directly; plus the
/// connection-state status line, the three Device menu items, <see cref="ToggleConnectionAsync"/> -
/// exactly what File &gt; Connect/Disconnect runs, including refreshing everything that follows the
/// connection state - and <see cref="AllSessions"/>, which <see cref="TuiMode.RunAsync"/> uses to
/// close every open tab's session when the loop ends.
/// </summary>
internal sealed record TuiWindowParts(
    Window Window,
    Editor Output,
    TextField SendField,
    MenuItem ConnectMenuItem,
    Func<CliOptions, Task<bool>> SwitchProfileAsync,
    Action<string> SetParser,
    Label StatusLabel,
    MenuItem K8055MenuItem,
    MenuItem BusylightMenuItem,
    MenuItem ScpiMenuItem,
    Func<Task> ToggleConnectionAsync,
    TuiLoggingParts Logging,
    TuiThemeMenu ThemeMenu,
    Func<Session> CurrentSession,
    MenuItem StreamMonitorMenuItem,
    Func<StreamMonitor?> CurrentStreamMonitor,
    Tabs TabsView,
    MenuItem NewSessionMenuItem,
    MenuItem CloseSessionMenuItem,
    Func<IReadOnlyList<Session>> AllSessions,
    Action CleanupAllTabs,
    MenuItem EchoSentCommandsMenuItem,
    MenuItem ClearOutputMenuItem,
    MenuItem SoftwareFlowControlMenuItem);
