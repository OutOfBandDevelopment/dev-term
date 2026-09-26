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
using DevTerm.Devices.RadexOne;
using DevTerm.Devices.Scpi;
using DevTerm.Devices.ZoomH4n;
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
/// A first stub, not the full design (no session switching, no live plugin selection, no
/// rendering-presenter graphics) — see docs/design/frontends.md's TUI section for the target.
/// </remarks>
public static class TuiMode
{
    /// <summary>
    /// Caps the scrolling output pane the same way <c>MainWindow.MaxOutputLines</c> does for WPF, so
    /// a long-running session doesn't grow it without bound — but shorter than WPF's 1000, since this
    /// pane is a single concatenated <see cref="Editor.Text"/> string rebuilt on every trim, not a
    /// virtualized items list; keeping it smaller keeps that rebuild cheap. Oldest lines are dropped
    /// first.
    /// </summary>
    private const int _maxOutputLines = 300;

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
        try
        {
            var parts = BuildWindow(app, session, catalog, cliOptions, profileStore, startupError);
            parts.SendField.SetFocus();

            // Application.Run's errorHandler is what WPF's DispatcherUnhandledException does for the
            // GUI: report whatever slips past every existing catch block (a genuine bug, not one of
            // the already-handled ConnectionErrorMessages.IsConnectionFailure cases) and resume the
            // loop rather than letting the whole TUI die. Per Terminal.Gui's own doc comment on this
            // overload, this only takes effect in RELEASE builds - a DEBUG build still rethrows so a
            // debugger can break on the original exception.
            app.Run(parts.Window, OnUnhandledException);
            parts.Logging.Stop();
        }
        finally
        {
            app.Dispose();
        }

        await session.CloseAsync();
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
    /// Builds the window and wires it to <paramref name="session"/>, without touching
    /// <c>Application.Init</c>/<c>Run</c>/<c>Shutdown</c> — split out so tests can drive the same
    /// production controls headlessly (see <c>DevTerm.Console.Tests.TuiModeTests</c>), the same
    /// seam <c>MainWindow.xaml.cs</c> exposes for WPF (<c>ConnectAsync</c>/<c>SendCurrentInputAsync</c>).
    /// </summary>
    internal static TuiWindowParts BuildWindow(IApplication app, Session session, PresenterCatalog catalog, CliOptions cliOptions, ConnectionProfileStore? profileStore = null, string? initialMessage = null)
    {
        // Also what "is this connection a saved profile?" (the title) is answered against, and what the
        // Device Profiles screen edits - a test passes an isolated one rather than the real user folder.
        profileStore ??= new ConnectionProfileStore();

        // The parser (send format) currently encoding typed lines - starts as the profile's, and
        // the "Send as" menu switches it for every line typed afterward. Captured/reassigned by the
        // closures below like session/cliOptions are (see SwitchProfileAsync's comment).
        var parser = cliOptions.EffectiveParser;

        // Cancels and replaces the in-flight profile-switch attempt's token on every
        // SwitchProfileAsync call - declared up here (not next to SwitchProfileAsync itself) purely
        // so the "_Device Profiles..." menu item below, which calls SwitchProfileAsync before its
        // own declaration appears in this method, doesn't hit a definite-assignment error over a
        // variable a local function closes over.
        CancellationTokenSource? switchCts = null;

        // The Device menu items RefreshConnectionUi enables/disables - assigned when the menu is built
        // below, declared up here for the same definite-assignment reason as switchCts.
        MenuItem? k8055MenuItem = null;
        MenuItem? busylightMenuItem = null;
        MenuItem? scpiMenuItem = null;
        MenuItem? radexOneMenuItem = null;
        MenuItem? zoomH4nMenuItem = null;
        MenuItem? de5000MenuItem = null;
        MenuItem? manifestMenuItem = null;

        // Created on first use of Device > Stream Monitor..., then kept for the window's lifetime so
        // monitoring carries on after its (modal) window closes - see OpenStreamMonitor below.
        StreamMonitor? streamMonitor = null;

        // Logger mode (File > Start Logging... / Stop Logging): the logger follows `session` across
        // a profile switch (see SwitchProfileAsync). State and menu item live in TuiLogging; declared
        // up here because RefreshConnectionUi reads it (same definite-assignment reason as above).
        var logging = new TuiLogging();

        string TitleFor() => ConnectionDescription.WindowTitle(cliOptions, parser, profileStore, session.State == ConnectionState.Open);

        var window = new Window
        {
            Title = TitleFor(),
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };

        var outputLines = new List<string>();
        if (ManifestNameWarning.For(cliOptions) is { } startupWarning)
        {
            outputLines.Add(StatusLine(startupWarning));
        }

        // Bad theme files, an unknown --theme, an unreadable preferences file - reported, never fatal.
        outputLines.AddRange(ActiveTheme.StartupProblems.Select(StatusLine));

        if (initialMessage is not null)
        {
            outputLines.Add(ErrorLine(initialMessage));
        }

        var output = new Editor
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(2),
            ReadOnly = true,
            Text = string.Join('\n', outputLines),

            // Soft-wrapped: a long reply or error line used to run off the right edge, and moving the
            // caret to the end after each append scrolled the whole pane sideways to that line's end,
            // hiding the start of every line (the "[source]" tags included).
            WordWrap = true,

            // Colors [error]/[dev-term] lines apart from device output - see OutputHighlighting.
            HighlightingDefinition = OutputHighlighting.Definition,
        };

        var sendLabel = new Label
        {
            Text = "Send:",
            X = 0,
            Y = Pos.Bottom(output),
            Width = 6,
        };

        var sendField = new TextField
        {
            X = Pos.Right(sendLabel),
            Y = Pos.Bottom(output),
            Width = Dim.Fill(),
            Enabled = session.State == ConnectionState.Open,
        };

        // The connection-state indicator: a full-width colored line under the send row (see
        // RefreshConnectionUi) - "● Connected — tcp://…" / "● Disconnected — …".
        var statusLabel = new Label
        {
            X = 0,
            Y = Pos.Bottom(sendLabel),
            Width = Dim.Fill(),
        };

        // Terminal.Gui 2.5.0 has no combo box, so Up/Down recall is implemented directly on
        // sendField's own KeyDown handler below rather than a dropdown widget - see
        // DevTerm.Wpf.MainWindow's editable ComboBox for the WPF equivalent of the same history.
        var sendHistory = new SendHistory();

        void AppendOutput(string line)
        {
            app.Invoke(() =>
            {
                outputLines.Add(line);
                if (outputLines.Count > _maxOutputLines)
                {
                    outputLines.RemoveAt(0);
                }

                output.Text = string.Join('\n', outputLines);
                output.CaretOffset = output.Text.Length;
            });
        }

        // The output pane is one plain-text Editor (no per-line colors), so status and error lines
        // are told apart from device output by a source tag, the same "[source] text" shape device
        // lines already use ("[ascii] ...").
        void AppendStatus(string text) => AppendOutput(StatusLine(text));
        void AppendError(string text) => AppendOutput(ErrorLine(text));

#pragma warning disable IDE0017 // Simplify object initialization
        var connectMenuItem = new MenuItem(
            session.State == ConnectionState.Open ? "_Disconnect" : "_Connect",
            string.Empty,
            () => { });
        connectMenuItem.Action = () => Observe(ToggleAndRefreshAsync(), AppendOutput);
#pragma warning restore IDE0017 // Simplify object initialization

        async Task ToggleAndRefreshAsync()
        {
            await ToggleConnectionAsync(app, session, cliOptions, connectMenuItem, sendField, AppendOutput);
            app.Invoke(RefreshConnectionUi);
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

        // The session (this one, or whichever SwitchProfileAsync swaps in) closed itself because
        // the connection ended - report why and flip the UI to "disconnected", ready to reconnect.
        void OnSessionDisconnected(object? _, SessionDisconnectedEventArgs e)
        {
            AppendError($"{ConnectionErrorMessages.ForDisconnect(cliOptions.Transport, e.Error)} Use File > Connect to reconnect.");
            app.Invoke(RefreshConnectionUi);
        }

        session.Disconnected += OnSessionDisconnected;

        bool StartLogging(string path)
        {
            try
            {
                logging.Start(path, session, cliOptions, parser, profileStore.FindName(cliOptions));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                AppendError($"Could not start logging to '{path}': {ex.Message}");
                return false;
            }

            AppendStatus($"Logging to {SessionLogging.DisplayPath(logging.Logger!.Path!)}.");
            RefreshConnectionUi();
            return true;
        }

        void StopLogging()
        {
            if (logging.Logger?.Path is { } path)
            {
                logging.Stop();
                AppendStatus($"Stopped logging to {SessionLogging.DisplayPath(path)}.");
                RefreshConnectionUi();
            }
        }

        logging.MenuItem.Action = Guarded(() =>
        {
            if (logging.Logger is not null)
            {
                StopLogging();
            }
            else if (TuiLogging.PromptForPath(app, cliOptions, profileStore.FindName(cliOptions)) is { } path)
            {
                StartLogging(path);
            }
        });

        void SetParser(string name)
        {
            // Called from a menu item's action, already on the UI thread - no Application.Invoke
            // needed (and it would never flush under a headless test without a real run loop).
            parser = name;
            window.Title = TitleFor();
        }

        // View > Theme: switching re-applies live through OnThemeChanged below.
        var themeMenu = new TuiThemeMenu(AppendStatus);

        var menuBar = new MenuBar(
        [
            new MenuBarItem("_File",
            [
                connectMenuItem,
                new MenuItem("_Device Profiles...", string.Empty, Guarded(() =>
                {
                    var configureParts = ConfigureMode.BuildWindow(app, cliOptions, null, profileStore);
                    app.Run(configureParts.Window);

                    if (configureParts.Result is { } chosen)
                    {
                        DevTermConfiguration.SaveLocalProfile(chosen);
                        Observe(SwitchProfileAsync(chosen), AppendOutput);
                    }
                })),
                logging.MenuItem,
                new MenuItem("Open Log for _Playback...", string.Empty, Guarded(() => PlaybackMode.OpenAndRun(app, cliOptions))),
                new MenuItem("_Quit", string.Empty, () => app.RequestStop(), Key.Q.WithCtrl),
            ]),
            // One entry per presenter that can encode typed text; picking one applies from the next
            // line typed on (the title bar shows which is current). Built from the catalog as of
            // startup - a profile switch never changes which presenters are registered.
            new MenuBarItem("_Send as", [.. catalog.InputNames.Select(name => new MenuItem(name, string.Empty, () => SetParser(name)))]),
            new MenuBarItem("_Device",
            [
                // Reuses the current, already-open session/connection rather than opening a second
                // competing one to the same physical device - reads the live "session"/"catalog"
                // closure variables, which SwitchProfileAsync above reassigns on a profile switch,
                // the same way the "_Device Profiles..." item above reads the live "cliOptions".
                k8055MenuItem = new MenuItem("_K8055 Control Panel...", string.Empty, Guarded(() =>
                {
                    var structuredSource = catalog.TryGet("k8055", out var presenter) ? presenter : null;
                    var panelParts = ControlPanelMode.BuildWindow(
                        app,
                        K8055UiDefinition.Build(),
                        new K8055ControlSurface(session),
                        structuredSource,
                        "dev-term — K8055 Control Panel");
                    app.Run(panelParts.Window);
                })),
                busylightMenuItem = new MenuItem("_Busylight Control Panel...", string.Empty, Guarded(() =>
                {
                    var structuredSource = catalog.TryGet("busylight", out var presenter) ? presenter : null;
                    var panelParts = ControlPanelMode.BuildWindow(
                        app,
                        BusylightUiDefinition.Build(),
                        new BusylightControlSurface(session),
                        structuredSource,
                        "dev-term — Busylight Control Panel");
                    app.Run(panelParts.Window);
                })),
                radexOneMenuItem = new MenuItem("_Radex One Control Panel...", string.Empty, Guarded(() =>
                {
                    var structuredSource = catalog.TryGet("radexone", out var presenter) ? presenter : null;
                    var panelParts = ControlPanelMode.BuildWindow(
                        app,
                        RadexOneUiDefinition.Build(),
                        new RadexOneControlSurface(session),
                        structuredSource,
                        "dev-term — Radex One Control Panel");
                    app.Run(panelParts.Window);
                })),
                zoomH4nMenuItem = new MenuItem("_Zoom H4n Remote...", string.Empty, Guarded(() =>
                {
                    var structuredSource = catalog.TryGet("zoomh4n", out var presenter) ? presenter : null;
                    var panelParts = ControlPanelMode.BuildWindow(
                        app,
                        ZoomH4nUiDefinition.Build(),
                        new ZoomH4nControlSurface(session),
                        structuredSource,
                        "dev-term — Zoom H4n Remote");
                    app.Run(panelParts.Window);
                })),
                de5000MenuItem = new MenuItem("_DE-5000 LCR Meter...", string.Empty, Guarded(() =>
                {
                    var structuredSource = catalog.TryGet("de5000", out var presenter) ? presenter : null;
                    var panelParts = ControlPanelMode.BuildWindow(
                        app,
                        De5000UiDefinition.Build(),
                        new De5000ControlSurface(),
                        structuredSource,
                        "dev-term — DE-5000 LCR Meter");
                    app.Run(panelParts.Window);
                })),
                // One generic entry, not one per instrument, unlike the two above - the command set
                // is data (ScpiProfileCatalog), not a hardcoded per-device UiDefinition, so a new
                // instrument is a dropped-in JSON file, not a new menu item.
                scpiMenuItem = new MenuItem("_SCPI Instrument...", string.Empty, Guarded(() =>
                {
                    var structuredSource = ResolveActiveScpiPresenter(session, catalog);
                    var picked = ResolveSavedScpiProfileChoice(cliOptions.ScpiProfile) ?? PickScpiProfileChoice(app);
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
                        Observe(DetectAndOpenScpiInstrumentAsync(app, session, structuredSource, cliOptions.ScpiAutoDetectTimeoutMs, AppendStatus, AppendError), AppendOutput);
                        return;
                    }

                    var profile = picked == _scpiGenericChoice
                        ? ScpiProfileCatalog.Generic
                        : ScpiProfileCatalog.All.First(p => p.Name == picked);
                    OpenScpiInstrumentWindow(app, session, structuredSource, profile);
                })),
                manifestMenuItem = new MenuItem("Device _Manifest...", string.Empty, Guarded(() => OpenDeviceManifest(app, session))),

                // Always available: editing a manifest needs no connection (see ManifestEditorMode).
                new MenuItem("_Edit Device Manifest...", string.Empty, Guarded(() => ManifestEditorMode.Run(app))),
                new MenuItem("S_tream Monitor...", string.Empty, Guarded(OpenStreamMonitor)),
            ]),
            themeMenu.MenuBarItem,
        ]);

        // A theme switch (View > Theme, from this window or any other) re-applies everything themed:
        // Terminal.Gui's schemes, the output pane's highlighting (XSHD colors are fixed per
        // definition, so it's swapped for the new theme's), and the status line. Raised on the thread
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
            output.HighlightingDefinition = OutputHighlighting.Definition;
            themeMenu.Refresh();
            RefreshConnectionUi();
            window.SetNeedsDraw();
        }

        ActiveTheme.Changed += OnThemeChanged;
        window.Disposing += (_, _) => ActiveTheme.Changed -= OnThemeChanged;

        // Everything that depends on the connection state, derived from session.State in one
        // place: the File menu label, the send field, the title (" — disconnected" when closed), the
        // status line, and which Device panels make sense (DevicePanels). Called on the UI thread -
        // directly while building, via app.Invoke after any connect/disconnect/fault/profile switch.
        void RefreshConnectionUi()
        {
            var state = session.State;
            var connected = state == ConnectionState.Open;

            connectMenuItem.Title = connected ? "_Disconnect" : "_Connect";
            sendField.Enabled = connected;
            window.Title = TitleFor();

            statusLabel.Text = $" ● {ConnectionDescription.StatusText(cliOptions, state)}{logging.StatusSuffix}";
            statusLabel.SetScheme(TuiTheme.Solid(TuiTheme.StatusAttribute(ActiveTheme.Current, state)));

            k8055MenuItem!.Enabled = DevicePanels.IsAvailable(DevicePanel.K8055, cliOptions, connected);
            busylightMenuItem!.Enabled = DevicePanels.IsAvailable(DevicePanel.Busylight, cliOptions, connected);
            scpiMenuItem!.Enabled = DevicePanels.IsAvailable(DevicePanel.Scpi, cliOptions, connected);
            radexOneMenuItem!.Enabled = DevicePanels.IsAvailable(DevicePanel.RadexOne, cliOptions, connected);
            zoomH4nMenuItem!.Enabled = DevicePanels.IsAvailable(DevicePanel.ZoomH4n, cliOptions, connected);
            de5000MenuItem!.Enabled = DevicePanels.IsAvailable(DevicePanel.De5000, cliOptions, connected);
            manifestMenuItem!.Enabled = DevicePanels.IsAvailable(DevicePanel.Manifest, cliOptions, connected);
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
        void quitOnCtrlQ(object? _, Key key)
        {
            // The Connection Editor (File > Device Profiles...) quits itself on Ctrl+Q, with its
            // unsaved-changes prompt; stopping it from here would skip that prompt.
            if (key != Key.Q.WithCtrl || key.Handled || ConfigureMode.OwnsQuitKey(app.TopRunnableView))
            {
                return;
            }

            key.Handled = true;
            app.RequestStop();
        }

        app.Keyboard.KeyDown += quitOnCtrlQ;
        window.Disposing += (_, _) => app.Keyboard.KeyDown -= quitOnCtrlQ;

        // A named handler, not an inline lambda, so SwitchProfileAsync below can unsubscribe it
        // from the old session before subscribing it to the new one.
        void OnSessionOutput(object? _, PresenterOutput presenterOutput) => AppendOutput($"[{presenterOutput.PresenterName}] {presenterOutput.Text}");
        session.Output += OnSessionOutput;

        // Tears down the current session/transport and opens a new one composed from
        // newOptions - live, without restarting the app, unlike the save-as-default-and-ask-for-a-
        // restart this replaced. Reassigns the session/presenter/cliOptions *parameters* directly
        // (not a wrapper object) - every other closure in this method (sendField.KeyDown,
        // connectMenuItem.Action, this same menu handler on a later invocation) reads those same
        // captured parameters, so C#'s normal closure-over-a-shared-variable semantics means they
        // all see the switch without needing to be individually re-wired. Must run under a real
        // Application.Run() loop (RunWithLoop in tests, never RunHeadless) - it calls
        // Application.Invoke like ToggleConnectionAsync below, which silently never flushes
        // otherwise (see CLAUDE.md).
        // A slow-to-fail connect (an unreachable host that never actively refuses, so it sits on
        // the OS connect timeout) can still be pending when the user switches to a *different*
        // host, and without switchCts, the earlier attempt's success/failure handler ran anyway
        // once it finally resolved - using the by-then-stale cliOptions - and stomped
        // connectMenuItem.Title/sendField.Enabled/output back over whatever the newer attempt had
        // already set. Reported as "I tried connecting to 192.168.0.108 and it failed, so I tried
        // 192.168.0.107 and it won't even try to connect now" - .107 *did* try, but .108's late
        // failure silently reverted the UI afterward. Real TCP connects honor cancellation (unlike
        // SerialPort/HidStream - see CLAUDE.md), so the superseded attempt now fails fast instead
        // of leaking a connect in the background.
        async Task<bool> SwitchProfileAsync(CliOptions newOptions)
        {
            switchCts?.Cancel();
            var cts = new CancellationTokenSource();
            switchCts = cts;

            DevTermSessionBuilder.Result built;
            try
            {
                built = DevTermSessionBuilder.Build(newOptions);
            }
            catch (Exception ex)
            {
                AppendError($"Could not switch profile: {ex.Message}");
                return false;
            }

            var mySession = built.Session;

            session.Output -= OnSessionOutput;
            session.Disconnected -= OnSessionDisconnected;
            await session.CloseAsync();
            await session.DisposeAsync();

            session = mySession;
            catalog = built.Catalog;
            cliOptions = newOptions;
            parser = newOptions.EffectiveParser;
            streamMonitor?.SetSession(mySession, StreamMonitor.DeviceNameFor(newOptions, profileStore), newOptions.EffectiveExportDirectory);
            mySession.Output += OnSessionOutput;
            mySession.Disconnected += OnSessionDisconnected;
            logging.Follow(mySession, newOptions, profileStore.FindName(newOptions));

            app.Invoke(() =>
            {
                // Clear the backing list too - clearing only output.Text brought the previous
                // connection's lines straight back on the next AppendOutput, which rebuilds the text
                // from the list.
                outputLines.Clear();
                output.Text = string.Empty;
                RefreshConnectionUi();
            });

            if (ManifestNameWarning.For(cliOptions) is { } manifestWarning)
            {
                AppendStatus(manifestWarning);
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
                if (!ReferenceEquals(switchCts, cts))
                {
                    // Superseded between the failure and this catch running - don't stomp the
                    // newer attempt's state with a stale one.
                    return false;
                }

                AppendError(ConnectionErrorMessages.For(cliOptions.Transport, ex));
                app.Invoke(RefreshConnectionUi);
                return false;
            }

            if (!ReferenceEquals(switchCts, cts))
            {
                // Connected, but superseded in the meantime - close it rather than adopting a
                // stray connection as current.
                mySession.Output -= OnSessionOutput;
                mySession.Disconnected -= OnSessionDisconnected;
                await mySession.CloseAsync();
                await mySession.DisposeAsync();
                return false;
            }

            app.Invoke(RefreshConnectionUi);
            AppendStatus($"Switched to {ConnectionDescription.For(cliOptions)}.");
            return true;
        }

        // Device > Stream Monitor...: opening it starts monitoring the current session (that's what
        // opening it is for); its Stop button stops it. The monitor outlives the modal window so
        // captures keep being auto-saved - each reported as a status line here - while the user is
        // back in this window sending commands. SwitchProfileAsync moves it to the new session.
        void OpenStreamMonitor()
        {
            if (streamMonitor is null)
            {
                var monitor = new StreamMonitor();
                monitor.CaptureAdded += (_, capture) => AppendStatus(capture.Describe());
                window.Disposing += (_, _) => monitor.Dispose();
                streamMonitor = monitor;
            }

            streamMonitor.SetSession(session, StreamMonitor.DeviceNameFor(cliOptions, profileStore), cliOptions.EffectiveExportDirectory);
            streamMonitor.Start();

            var monitorParts = StreamMonitorMode.BuildWindow(app, streamMonitor);
            app.Run(monitorParts.Window);
            monitorParts.Window.Dispose();
        }

        sendField.KeyDown += (_, key) =>
        {
            if (key == Key.CursorUp)
            {
                key.Handled = true;
                if (sendHistory.Previous() is { } older)
                {
                    sendField.Text = older;
                    sendField.MoveEnd();
                }

                return;
            }

            if (key == Key.CursorDown)
            {
                key.Handled = true;
                if (sendHistory.Next() is { } newer)
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
            sendHistory.Add(line);

            if (line.Length == 0)
            {
                return;
            }

            if (session.State != ConnectionState.Open)
            {
                AppendError("Not connected — use File > Connect.");
                return;
            }

            if (!catalog.TryGetInput(parser, out var input))
            {
                AppendError($"Parser '{parser}' does not support sending.");
                return;
            }

            Observe(SendAsync(session, cliOptions, input, line, AppendOutput, parser), AppendOutput);
        };

        window.Add(menuBar, output, sendLabel, sendField, statusLabel);
        RefreshConnectionUi();

        // --log starts logging straight away (the session may already be open - the log's first
        // record says so). RunAsync stops it when the loop ends.
        if (cliOptions.Log is { Length: > 0 } logOption)
        {
            StartLogging(SessionLogging.ResolveLogPath(logOption, cliOptions, profileStore.FindName(cliOptions), DateTimeOffset.Now));
        }

        return new TuiWindowParts(window, output, sendField, connectMenuItem, SwitchProfileAsync, SetParser, statusLabel, k8055MenuItem!, busylightMenuItem!, scpiMenuItem!, ToggleAndRefreshAsync, new TuiLoggingParts(logging.MenuItem, StartLogging, StopLogging, () => logging.Logger), themeMenu);
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
    /// Never throws.
    /// </summary>
    internal static async Task SendAsync(Session session, CliOptions cliOptions, IPresenterInput input, string line, Action<string> appendOutput, string? parserName = null)
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
        app.Run(panelParts.Window);
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
        app.Run(dialog);
        return picked;
    }
}

/// <summary>The controls a test needs to drive the TUI headlessly: inject keys into <see cref="SendField"/>, read rendered text back from <see cref="Output"/>, drive a live profile switch directly via <see cref="SwitchProfileAsync"/> (the same delegate the "File &gt; Device Profiles..." menu item calls), or switch the send format via <see cref="SetParser"/> (what a "Send as" menu item calls); plus the connection-state status line, the three Device menu items, and <see cref="ToggleConnectionAsync"/> - exactly what File ; plus the connection-state status line and the three Device menu items, to check they follow the connection.</summary>gt; Connect/Disconnect runs, including refreshing everything that follows the connection state.</summary>
internal sealed record TuiWindowParts(Window Window, Editor Output, TextField SendField, MenuItem ConnectMenuItem, Func<CliOptions, Task<bool>> SwitchProfileAsync, Action<string> SetParser, Label StatusLabel, MenuItem K8055MenuItem, MenuItem BusylightMenuItem, MenuItem ScpiMenuItem, Func<Task> ToggleConnectionAsync, TuiLoggingParts Logging, TuiThemeMenu ThemeMenu);
