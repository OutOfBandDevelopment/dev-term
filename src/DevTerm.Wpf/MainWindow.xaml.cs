using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DevTerm.Configuration;
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

namespace DevTerm.Wpf;

/// <summary>
/// The GUI front end's main window (see docs/design/frontends.md): a <see cref="TabControl"/> of
/// open <see cref="SessionTab"/>s, each with its own scrolling output list and status, sharing one
/// send box/parser box/status bar bound to whichever tab is active. See
/// docs/design/multi-session-ui.md for the design this implements.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// Caps each tab's scrolling output log so a long-running session (especially against a device
    /// that streams continuously, like the K8055's unprompted input reports at hundreds/sec) doesn't
    /// grow its output list without bound — an unbounded <c>ItemsControl</c> eventually makes the
    /// whole window unresponsive. Oldest lines are dropped first.
    /// </summary>
    private const int _maxOutputLines = 1000;

    // View > Echo Sent Commands (default off - most sessions already see their own input in the
    // SendBox history and don't need it duplicated into the output log). Toggled per-window, not
    // per-tab - applies to every tab's AppendOutput going forward.
    private bool _echoSentCommands;

    /// <summary>Pairs one <see cref="SessionTab"/> with the WPF chrome built for it — a <see cref="TabItem"/>, its own output <see cref="ListBox"/>, its header label, and the specific event-handler delegate instances subscribed to its <see cref="Configuration.SessionTab.Session"/> (so a profile switch or tab close can unsubscribe the exact same instances). Kept a private nested type: <see cref="Configuration.SessionTab"/> itself stays UI-framework-agnostic so the TUI front end can reuse it.</summary>
    private sealed class WindowTab
    {
        public required SessionTab Tab { get; init; }

        public required TabItem Item { get; init; }

        public required ListBox OutputList { get; init; }

        public required TextBlock HeaderText { get; init; }

        public EventHandler<PresenterOutput>? OutputHandler { get; set; }

        public EventHandler<SessionDisconnectedEventArgs>? DisconnectedHandler { get; set; }

        // Mirrors the window-level _switchCts this replaced: a slow-to-fail connect on THIS tab can
        // still be pending when the user switches THIS tab's profile again - without a per-tab token,
        // the earlier attempt's failure handler would stomp the newer one's UI once it finally
        // resolved. See docs/bugs/resolved/017-wpf-profile-switch-no-supersede.md.
        public CancellationTokenSource? SwitchCts { get; set; }

        // Per-tab logging/Stream Monitor (docs/design/multi-session-ui.md's Step 4) - each tab owns
        // its own logger and monitor rather than the window sharing one across every tab.
        public SessionLogger? Logger { get; set; }

        public StreamMonitor? Monitor { get; set; }

        public StreamMonitorWindow? MonitorWindow { get; set; }
    }

    private readonly List<WindowTab> _tabs = [];
    private readonly ConnectionProfileStore _profileStore;
    private bool _closeConfirmed;
    private bool _closing;

    /// <summary>
    /// How many times <see cref="OnClosing"/>'s cleanup (stream monitor dispose, session close,
    /// logging stop) has actually run - exposed only so
    /// <c>MainWindowTests.Close_CalledAgainWhileTheFirstCloseIsStillCleaningUp...</c> can tell a
    /// genuinely re-entrant second run apart from the harmless, expected extra `Closing` events a
    /// cancelled close still raises. See docs/bugs/resolved/058-wpf-onclosing-reentry.md.
    /// </summary>
    internal int ClosingCleanupRunCount { get; private set; }

    // Every open control panel (K8055, Busylight, RadexOne, ZoomH4n, De5000, SCPI, a device manifest
    // panel) holds an IControlSurface built against one tab's Session and, for most of them, a
    // structured presenter from that tab's Catalog - both go stale the moment that tab's session is
    // replaced or closed, so a panel gets closed along with the tab it belongs to instead of being
    // left to fail silently against a dead transport. Each panel's owning WindowTab is stashed in its
    // own Window.Tag. See docs/bugs/resolved/016-wpf-panels-bound-to-old-session.md.
    private readonly List<Window> _openControlPanels = [];

    /// <summary>Every currently open control panel window, for tests to assert against.</summary>
    internal IReadOnlyList<Window> OpenControlPanels => _openControlPanels;

    // The most recently active tab's CliOptions, kept around as a seed for File > New Session... /
    // Open Log for Playback... once the window has zero tabs open (Step 4's zero-tab support - see
    // docs/design/multi-session-ui.md) and there's no ActiveWindowTab left to read it from.
    private CliOptions _lastCliOptions;

    /// <param name="profileStore">What the title checks "is this connection a saved profile?" against, and what the Device Profiles window edits — defaults to the user's real profiles folder; a test passes an isolated one.</param>
    public MainWindow(Session session, PresenterCatalog catalog, CliOptions cliOptions, ConnectionProfileStore? profileStore = null)
    {
        InitializeComponent();
        WpfTheme.Attach(this);
        _profileStore = profileStore ?? new ConnectionProfileStore();
        _lastCliOptions = cliOptions;

        var tab = AddTab(new SessionTab(session, catalog, cliOptions));

        if (ManifestNameWarning.For(cliOptions) is { } manifestWarning)
        {
            AppendOutput(tab, manifestWarning, OutputKind.Status);
        }

        // View > Theme, and any problems loading themes/preferences at startup - MainWindow.Theme.cs.
        BuildThemeMenu();

        Loaded += OnLoaded;
        Closing += OnClosing;
        StartLoggingFromOptions(tab);

        // MenuItem.InputGestureText only labels the shortcut in the menu - it doesn't register a
        // live accelerator by itself (same gotcha found for Terminal.Gui's MenuItem.Key building
        // the TUI's own menu - see TuiMode.BuildWindow), so every shortcut below needs an explicit
        // handler too.
        PreviewKeyDown += (_, e) =>
        {
            if (HandleGlobalKeyDown(e.Key, Keyboard.Modifiers))
            {
                e.Handled = true;
            }
        };
    }

    /// <summary>
    /// The window-wide keyboard shortcuts (Ctrl+Q exit, Ctrl+T new session, Ctrl+W close session,
    /// Ctrl+Tab/Ctrl+Shift+Tab next/previous tab) - split out from the <c>PreviewKeyDown</c> handler
    /// so tests can drive it directly, the same reasoning as <see cref="HandleSendBoxKey"/>. Returns
    /// whether the key was handled.
    /// </summary>
    internal bool HandleGlobalKeyDown(Key key, ModifierKeys modifiers)
    {
        if (modifiers == ModifierKeys.Control)
        {
            switch (key)
            {
                case Key.Q:
                    Close();
                    return true;

                case Key.T:
                    NewSession_Click(this, new RoutedEventArgs());
                    return true;

                case Key.W:
                    if (ActiveWindowTabOrNull is { } tab)
                    {
                        Observe(CloseTabAsync(tab));
                    }

                    return true;

                case Key.Tab:
                    SelectAdjacentTab(1);
                    return true;
            }
        }
        else if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.Tab)
        {
            SelectAdjacentTab(-1);
            return true;
        }

        return false;
    }

    /// <summary>Moves <see cref="SessionTabs"/>'s selection by <paramref name="direction"/> tabs, wrapping around. A no-op with fewer than two tabs.</summary>
    private void SelectAdjacentTab(int direction)
    {
        if (_tabs.Count < 2)
        {
            return;
        }

        var currentIndex = SessionTabs.SelectedIndex;
        if (currentIndex < 0)
        {
            return;
        }

        SessionTabs.SelectedIndex = (currentIndex + direction + _tabs.Count) % _tabs.Count;
    }

    private WindowTab? ActiveWindowTabOrNull => SessionTabs.SelectedItem is TabItem { Tag: WindowTab tab } ? tab : null;

    private WindowTab ActiveWindowTab => ActiveWindowTabOrNull ?? throw new InvalidOperationException("No session tabs are open.");

    /// <summary>The active tab's own output list — kept as a same-named, same-accessibility property (not a field) so every existing single-tab test call site (<c>window.OutputList...</c>) keeps working unchanged.</summary>
    internal ListBox OutputList => ActiveWindowTab.OutputList;

    /// <summary>The send format (parser) currently encoding typed lines — the "Send as" box's selection, starting as the profile's.</summary>
    internal string CurrentParser => ParserBox.SelectedItem as string ?? ActiveWindowTab.Tab.CliOptions.EffectiveParser;

    private string TitleText => ConnectionDescription.WindowTitle(ActiveWindowTab.Tab.CliOptions, CurrentParser, _profileStore, ActiveWindowTab.Tab.Session.State == ConnectionState.Open);

    private void ParserBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ActiveWindowTabOrNull is not { } tab)
        {
            return;
        }

        if (ParserBox.SelectedItem is string parser)
        {
            tab.Tab.Parser = parser;
        }

        Title = TitleText;
    }

    /// <summary>
    /// Builds the WPF chrome for a new <see cref="SessionTab"/> (its output <see cref="ListBox"/>,
    /// header, close button), wires that tab's own <see cref="Session.Output"/>/
    /// <see cref="Session.Disconnected"/> handlers (so incoming data always reaches ITS output list,
    /// not whichever tab happens to be active when the event fires), adds it to
    /// <see cref="SessionTabs"/>, and makes it the active tab.
    /// </summary>
    private WindowTab AddTab(SessionTab tab)
    {
        var outputList = new ListBox
        {
            FontFamily = new FontFamily("Consolas"),
            ItemTemplate = (DataTemplate)FindResource("OutputLineTemplate"),
            SelectionMode = SelectionMode.Extended,
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(outputList, ScrollBarVisibility.Auto);
        outputList.ContextMenu = new ContextMenu
        {
            Items = { new MenuItem { Header = "_Copy", Command = ApplicationCommands.Copy } },
        };
        outputList.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, OutputList_CopyExecuted, OutputList_CopyCanExecute));

        var headerText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Text = ConnectionDescription.Subject(tab.CliOptions, _profileStore),
        };
        var closeButton = new Button
        {
            Content = "✕",
            Padding = new Thickness(4, 0, 4, 0),
            Margin = new Thickness(8, 0, 0, 0),
            Focusable = false,
            ToolTip = "Close this session",
        };
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(headerText);
        header.Children.Add(closeButton);

        var item = new TabItem { Header = header, Content = outputList };

        var windowTab = new WindowTab { Tab = tab, Item = item, OutputList = outputList, HeaderText = headerText };
        item.Tag = windowTab;
        closeButton.Click += (_, _) => Observe(CloseTabAsync(windowTab));

        windowTab.OutputHandler = (_, output) => Dispatcher.Invoke(() => AppendOutput(windowTab, $"[{output.PresenterName}] {output.Text}"));
        windowTab.DisconnectedHandler = (_, e) => Dispatcher.BeginInvoke(() =>
        {
            AppendOutput(windowTab, $"{ConnectionErrorMessages.ForDisconnect(windowTab.Tab.CliOptions.Transport, e.Error)} Use File > Connect to reconnect.", OutputKind.Error);
            RefreshConnectionUi(windowTab);
        });
        tab.Session.Output += windowTab.OutputHandler;
        tab.Session.Disconnected += windowTab.DisconnectedHandler;

        _tabs.Add(windowTab);
        SessionTabs.Items.Add(item);
        SessionTabs.SelectedItem = item;
        UpdateCloseSessionAvailability();
        return windowTab;
    }

    private static void OutputList_CopyCanExecute(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = sender is ListBox { SelectedItems.Count: > 0 };

    // Builds the copied text from listBox.Items (original document order), not SelectedItems
    // directly - out-of-order Ctrl+clicks scramble SelectedItems' own enumeration order.
    private static void OutputList_CopyExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (sender is not ListBox { SelectedItems.Count: > 0 } listBox)
        {
            return;
        }

        var text = string.Join(
            Environment.NewLine,
            listBox.Items.Cast<OutputLine>().Where(listBox.SelectedItems.Contains).Select(line => line.Text));
        Clipboard.SetText(text);
    }

    private void EchoSentCommandsMenuItem_Click(object sender, RoutedEventArgs e) =>
        _echoSentCommands = EchoSentCommandsMenuItem.IsChecked;

    // View > Clear Output: clears only the active tab's scrollback, not every open tab's.
    private void ClearOutputMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveWindowTabOrNull is { } tab)
        {
            tab.OutputList.Items.Clear();
        }
    }

    // Fires for both the auto-select of the first tab ever added and any later explicit switch -
    // rebinds the chrome shared across tabs (ParserBox, SendBox/its history) to whichever tab is now
    // active, then refreshes everything that shows connection state for it.
    private void SessionTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SessionTabs.SelectedItem is not TabItem { Tag: WindowTab tab })
        {
            return;
        }

        _lastCliOptions = tab.Tab.CliOptions;
        ParserBox.ItemsSource = tab.Tab.Catalog.InputNames;
        ParserBox.SelectedItem = tab.Tab.Parser;
        SendBox.ItemsSource = tab.Tab.SendHistory.Items;
        SendBox.Text = string.Empty;
        RefreshConnectionUi(tab);
        RefreshLoggingUiForActiveTab();
    }

    private void NewSession_Click(object sender, RoutedEventArgs e)
    {
        var seedOptions = ActiveWindowTabOrNull?.Tab.CliOptions ?? _lastCliOptions;
        var window = new DeviceProfilesWindow(_profileStore, seedOptions) { Owner = this };
        window.ShowDialog();

        if (window.Result is not { } chosen)
        {
            return;
        }

        DevTermConfiguration.SaveLocalProfile(chosen);
        SessionTab newTab;
        try
        {
            newTab = SessionTab.Build(chosen);
        }
        catch (Exception ex)
        {
            if (ActiveWindowTabOrNull is { } activeTab)
            {
                AppendOutput(activeTab, $"Could not open a new session: {ex.Message}", OutputKind.Error);
            }
            else
            {
                MessageBox.Show(this, $"Could not open a new session: {ex.Message}", "dev-term", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            return;
        }

        var tab = AddTab(newTab);
        StartLoggingFromOptions(tab);
        Observe(ConnectAsync());
    }

    private void CloseSession_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveWindowTabOrNull is { } tab)
        {
            Observe(CloseTabAsync(tab));
        }
    }

    /// <summary>
    /// Closes one tab's session, its Stream Monitor/logger, and any control panels opened from it,
    /// then removes it. Closing the last remaining tab is allowed (Step 4 of
    /// docs/design/multi-session-ui.md) — the window stays open with zero tabs, disabled down to
    /// File > New Session.../Exit/View, via <see cref="HandleZeroTabs"/>.
    /// </summary>
    private async Task CloseTabAsync(WindowTab tab)
    {
        if (!_tabs.Contains(tab))
        {
            return;
        }

        foreach (var panel in _openControlPanels.Where(p => ReferenceEquals(p.Tag, tab)).ToArray())
        {
            panel.Close();
        }

        tab.MonitorWindow?.Close();
        tab.Monitor?.Dispose();
        StopLogging(tab, report: false);

        tab.Tab.Session.Output -= tab.OutputHandler;
        tab.Tab.Session.Disconnected -= tab.DisconnectedHandler;
        try
        {
            await tab.Tab.Session.CloseAsync();
            await tab.Tab.Session.DisposeAsync();
        }
        catch (Exception)
        {
            // The tab is closing regardless - a device that timed out or vanished mid-close isn't
            // worth reporting once its whole tab is going away.
        }

        _lastCliOptions = tab.Tab.CliOptions;
        _tabs.Remove(tab);
        SessionTabs.Items.Remove(tab.Item);
        UpdateCloseSessionAvailability();
        if (_tabs.Count == 0)
        {
            HandleZeroTabs();
        }
    }

    private void UpdateCloseSessionAvailability() => CloseSessionMenuItem.IsEnabled = _tabs.Count > 0;

    /// <summary>
    /// Disables everything that needs an active tab (connect, device panels, send row, Stream
    /// Monitor, logging) once the last one has closed, and shows a neutral "no sessions" status —
    /// see docs/design/multi-session-ui.md's Step 4 zero-tab behavior. Reversed automatically by
    /// <see cref="AddTab"/> making the new tab active, which re-enables everything via
    /// <see cref="RefreshConnectionUi(WindowTab, ConnectionState?)"/>/<see cref="RefreshLoggingUiForActiveTab"/>.
    /// </summary>
    private void HandleZeroTabs()
    {
        ConnectMenuItem.Header = "_Connect";
        ConnectMenuItem.IsEnabled = false;
        DeviceProfilesMenuItem.IsEnabled = false;
        StreamMonitorMenuItem.IsEnabled = false;
        K8055MenuItem.IsEnabled = false;
        BusylightMenuItem.IsEnabled = false;
        ScpiMenuItem.IsEnabled = false;
        RadexOneMenuItem.IsEnabled = false;
        ZoomH4nMenuItem.IsEnabled = false;
        De5000MenuItem.IsEnabled = false;
        Nmea0183MenuItem.IsEnabled = false;
        ManifestMenuItem.IsEnabled = false;

        SendBox.IsEnabled = false;
        SendBox.Text = string.Empty;
        SendBox.ItemsSource = null;
        ParserBox.ItemsSource = null;
        ParserBox.SelectedItem = null;

        Title = "dev-term";
        ConnectionStatusText.Text = "No sessions open — File > New Session... to start one.";
        ConnectionStatusDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, WpfTheme.Key(ThemeRole.StatusDisconnected));
        RefreshLoggingUiForActiveTab();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await ConnectAsync();

    /// <summary>
    /// The connect logic <see cref="OnLoaded"/> triggers for the active tab, exposed as an awaitable
    /// method (rather than only reachable through the <c>async void</c> event handler) so tests can
    /// drive and await it deterministically.
    /// </summary>
    /// <remarks>
    /// A failed connect leaves the window open and disconnected with the error in that tab's output
    /// list, so the user can retry (File > Connect) or choose another connection (File > Device
    /// Profiles...) - it used to show a modal and then close the whole app.
    /// </remarks>
    internal async Task ConnectAsync()
    {
        if (ActiveWindowTabOrNull is not { } tab)
        {
            return;
        }

        RefreshConnectionUi(tab, ConnectionState.Opening);
        try
        {
            await tab.Tab.Session.OpenAsync();
        }
        catch (Exception ex)
        {
            AppendOutput(tab, $"{ConnectionErrorMessages.For(tab.Tab.CliOptions.Transport, ex)} Use File > Connect to retry, or File > Device Profiles... to choose another connection.", OutputKind.Error);
            RefreshConnectionUi(tab);
            return;
        }

        RefreshConnectionUi(tab);
        SendBox.Focus();
    }

    /// <summary>
    /// Everything that depends on one tab's connection state, derived from
    /// <see cref="Session.State"/> in one place: its header label, and — only when
    /// <paramref name="tab"/> is the active tab — the File menu header, <see cref="SendBox"/>, the
    /// title (" — disconnected" when closed), the status bar, and which Device panels make sense
    /// (<see cref="DevicePanels"/>). Called after every connect, disconnect, fault and profile switch
    /// - the WPF equivalent of <c>TuiMode</c>'s <c>RefreshConnectionUi</c>.
    /// </summary>
    /// <param name="showState">Overrides the displayed state - <see cref="ConnectionState.Opening"/> while a connect is in flight, which the transport never announces to this window.</param>
    private void RefreshConnectionUi(WindowTab tab, ConnectionState? showState = null)
    {
        var state = showState ?? tab.Tab.Session.State;
        var connected = state == ConnectionState.Open;

        tab.HeaderText.Text = ConnectionDescription.Subject(tab.Tab.CliOptions, _profileStore);

        if (!ReferenceEquals(tab, ActiveWindowTabOrNull))
        {
            return;
        }

        ConnectMenuItem.Header = connected ? "_Disconnect" : "_Connect";
        ConnectMenuItem.IsEnabled = true;
        DeviceProfilesMenuItem.IsEnabled = true;
        StreamMonitorMenuItem.IsEnabled = true;
        SendBox.IsEnabled = connected;
        Title = TitleText;

        ConnectionStatusText.Text = ConnectionDescription.StatusText(tab.Tab.CliOptions, state);
        ConnectionStatusDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, WpfTheme.Key(connected
            ? ThemeRole.StatusConnected
            : state == ConnectionState.Opening ? ThemeRole.StatusConnecting : ThemeRole.StatusDisconnected));

        K8055MenuItem.IsEnabled = DevicePanels.IsAvailable(DevicePanel.K8055, tab.Tab.CliOptions, connected);
        BusylightMenuItem.IsEnabled = DevicePanels.IsAvailable(DevicePanel.Busylight, tab.Tab.CliOptions, connected);
        ScpiMenuItem.IsEnabled = DevicePanels.IsAvailable(DevicePanel.Scpi, tab.Tab.CliOptions, connected);
        RadexOneMenuItem.IsEnabled = DevicePanels.IsAvailable(DevicePanel.RadexOne, tab.Tab.CliOptions, connected);
        ZoomH4nMenuItem.IsEnabled = DevicePanels.IsAvailable(DevicePanel.ZoomH4n, tab.Tab.CliOptions, connected);
        De5000MenuItem.IsEnabled = DevicePanels.IsAvailable(DevicePanel.De5000, tab.Tab.CliOptions, connected);
        Nmea0183MenuItem.IsEnabled = DevicePanels.IsAvailable(DevicePanel.Nmea0183, tab.Tab.CliOptions, connected);
        ManifestMenuItem.IsEnabled = DevicePanels.IsAvailable(DevicePanel.Manifest, tab.Tab.CliOptions, connected);
        RefreshSoftwareFlowControlMenu(tab);
    }

    private void RefreshConnectionUi(ConnectionState? showState = null)
    {
        if (ActiveWindowTabOrNull is { } tab)
        {
            RefreshConnectionUi(tab, showState);
        }
    }

    /// <summary>
    /// Observes a fire-and-forget task (an event handler can't await): anything it throws is
    /// reported in the active tab's output list instead of surfacing later as an unobserved task
    /// exception.
    /// </summary>
    private void Observe(Task task) =>
        _ = task.ContinueWith(
            t => Dispatcher.BeginInvoke(() => AppendOutput($"Unexpected error: {t.Exception!.GetBaseException().Message}", OutputKind.Error)),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

    /// <summary>
    /// The File > Connect/Disconnect menu item's action: closes an open session, or reopens a
    /// closed one, for the active tab - updating the menu item's own label and <see cref="SendBox"/>'s
    /// enabled state to match — the WPF equivalent of <c>TuiMode.ToggleConnectionAsync</c>.
    /// </summary>
    internal async Task ToggleConnectionAsync()
    {
        if (ActiveWindowTabOrNull is not { } tab)
        {
            return;
        }

        if (tab.Tab.Session.State == ConnectionState.Open)
        {
            await tab.Tab.Session.CloseAsync();
            RefreshConnectionUi(tab);
            AppendOutput(tab, "Disconnected.", OutputKind.Status);
            return;
        }

        RefreshConnectionUi(tab, ConnectionState.Opening);
        try
        {
            await tab.Tab.Session.OpenAsync();
        }
        catch (Exception ex)
        {
            AppendOutput(tab, ConnectionErrorMessages.For(tab.Tab.CliOptions.Transport, ex), OutputKind.Error);

            // This reuses the same session/transport across retries - the menu label/send box
            // still need to reflect "not connected" on a failed *retry*. Same asymmetry found and
            // fixed in TuiMode.ToggleConnectionAsync.
            RefreshConnectionUi(tab);
            return;
        }

        RefreshConnectionUi(tab);
        AppendOutput(tab, $"Connected to {ConnectionDescription.For(tab.Tab.CliOptions)}.", OutputKind.Status);
    }

    private void ConnectMenuItem_Click(object sender, RoutedEventArgs e) => Observe(ToggleConnectionAsync());

    private void AppendOutput(WindowTab tab, string line, OutputKind kind = OutputKind.Device)
    {
        tab.OutputList.Items.Add(new OutputLine(line, kind));
        while (tab.OutputList.Items.Count > _maxOutputLines)
        {
            tab.OutputList.Items.RemoveAt(0);
        }

        if (tab.OutputList.Items.Count > 0)
        {
            tab.OutputList.ScrollIntoView(tab.OutputList.Items[^1]);
        }
    }

    private void AppendOutput(string line, OutputKind kind = OutputKind.Device)
    {
        // No-ops with zero tabs open - there's no output list left to show it in; see
        // docs/design/multi-session-ui.md's Step 4 zero-tab behavior.
        if (ActiveWindowTabOrNull is { } tab)
        {
            AppendOutput(tab, line, kind);
        }
    }

    private void SendBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (HandleSendBoxKey(e.Key))
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// Handles Up/Down history recall and Enter-to-send on <see cref="SendBox"/> for the active tab
    /// — pulled out of the <c>PreviewKeyDown</c> handler (rather than only reachable via a real
    /// routed key event) so tests can drive it deterministically, the same reasoning as
    /// <see cref="SendCurrentInputAsync"/> itself. Wired to <c>PreviewKeyDown</c> (tunneling), not
    /// <c>KeyDown</c>, so this runs before the editable <see cref="ComboBox"/>'s own native key
    /// handling (opening the drop-down on Up/Down, moving the caret) can react first — the same
    /// "framework default doesn't reliably compose with our own handling" precedent as Ctrl+Q needing
    /// a <c>PreviewKeyDown</c> handler instead of relying on <c>MenuItem.InputGestureText</c>. Returns
    /// whether the key was handled.
    /// </summary>
    internal bool HandleSendBoxKey(Key key)
    {
        if (ActiveWindowTabOrNull is not { } tab)
        {
            return false;
        }

        switch (key)
        {
            case Key.Enter:
                Observe(SendCurrentInputAsync());
                return true;

            case Key.Up:
                if (tab.Tab.SendHistory.Previous() is { } older)
                {
                    SendBox.Text = older;
                }

                return true;

            case Key.Down:
                if (tab.Tab.SendHistory.Next() is { } newer)
                {
                    SendBox.Text = newer;
                }

                return true;

            default:
                return false;
        }
    }

    private void Send_Click(object sender, RoutedEventArgs e) => Observe(SendCurrentInputAsync());

    /// <summary>
    /// Sends whatever's currently in <see cref="SendBox"/> on the active tab, exposed as an
    /// awaitable method (rather than only reachable through the fire-and-forget UI event handlers) so
    /// tests can drive and await it deterministically. A line the "Send as" parser rejects is
    /// reported and not sent (the connection is left alone); a device-side failure has already
    /// disconnected the session and been reported by that tab's disconnected handler, so it isn't
    /// reported twice. Never throws.
    /// </summary>
    internal async Task SendCurrentInputAsync()
    {
        if (ActiveWindowTabOrNull is not { } tab)
        {
            return;
        }

        // Restore focus to SendBox once we're done, however we exit below - clicking "Send" (unlike
        // pressing Enter in the box) moves keyboard focus to that button, and nothing else brings it
        // back. Left alone, Up/Down stop recalling history right after the first mouse-driven send
        // until the user clicks back into the box (see docs/bugs/resolved/062-sendbox-arrow-keys-lose-focus-after-send.md).
        try
        {
            var line = SendBox.Text;
            SendBox.Text = string.Empty;
            tab.Tab.SendHistory.Add(line);

            if (line.Length == 0 || !tab.Tab.Catalog.TryGetInput(CurrentParser, out var input))
            {
                return;
            }

            if (!TypedInput.TryEncode(input, CurrentParser, line, tab.Tab.CliOptions.LineEnding, out var payload, out var error))
            {
                AppendOutput(tab, error!, OutputKind.Error);
                return;
            }

            if (payload.Length == 0)
            {
                return;
            }

            if (tab.Tab.Session.State != ConnectionState.Open)
            {
                AppendOutput(tab, "Not connected — use File > Connect.", OutputKind.Error);
                return;
            }

            if (_echoSentCommands)
            {
                AppendOutput(tab, $"Out> {TypedInput.FormatForEcho(line, tab.Tab.CliOptions.LineEnding)}", OutputKind.Sent);
            }

            try
            {
                await tab.Tab.Session.SendAsync(payload);
            }
            catch (Exception ex)
            {
                if (tab.Tab.Session.State == ConnectionState.Open)
                {
                    AppendOutput(tab, $"Send failed: {ex.Message}", OutputKind.Error);
                }
            }
        }
        finally
        {
            SendBox.Focus();
        }
    }

    private void DeviceProfiles_Click(object sender, RoutedEventArgs e)
    {
        var tab = ActiveWindowTab;
        var window = new DeviceProfilesWindow(_profileStore, tab.Tab.CliOptions) { Owner = this };
        window.ShowDialog();

        if (window.Result is { } chosen)
        {
            DevTermConfiguration.SaveLocalProfile(chosen);
            Observe(SwitchProfileAsync(chosen));
        }
    }

    /// <summary>
    /// Tracks a just-created control panel window (K8055/Busylight/RadexOne/ZoomH4n/De5000/SCPI/a
    /// device manifest panel) against the tab it was opened from, so <see cref="SwitchProfileAsync"/>
    /// and <see cref="CloseTabAsync"/> can close it — its <see cref="IControlSurface"/> and structured
    /// presenter are both bound to the session/catalog active when it was opened, and go stale the
    /// moment those are replaced or the tab closes. Removed from <see cref="_openControlPanels"/> as
    /// soon as the window closes for any other reason too.
    /// </summary>
    private void TrackControlPanel(Window window, WindowTab tab)
    {
        window.Tag = tab;
        _openControlPanels.Add(window);
        window.Closed += (_, _) => _openControlPanels.Remove(window);
    }

    // Show(), not ShowDialog(): unlike Device Profiles (a one-shot picker), this panel is meant to
    // stay open and update live alongside the main window, not block it. Reuses the current, already
    // -open session rather than opening a second competing connection to the same physical device.
    private void K8055ControlPanel_Click(object sender, RoutedEventArgs e) => OpenK8055ControlPanel();

    /// <summary>Split from the click handler so tests can drive it and assert against <see cref="OpenControlPanels"/> without simulating a menu click.</summary>
    internal ControlPanelWindow OpenK8055ControlPanel()
    {
        var tab = ActiveWindowTab;
        var structuredSource = tab.Tab.Catalog.TryGet("k8055", out var presenter) ? presenter : null;
        var window = new ControlPanelWindow(
            K8055UiDefinition.Build(),
            new K8055ControlSurface(tab.Tab.Session),
            structuredSource)
        {
            Owner = this,
        };
        TrackControlPanel(window, tab);
        window.Show();
        return window;
    }

    // Show(), not ShowDialog(): unlike Device Profiles (a one-shot picker), this panel is meant to
    // stay open and update live alongside the main window, not block it. Reuses the current, already
    // -open session rather than opening a second competing connection to the same physical device.
    private void BusylightControlPanel_Click(object sender, RoutedEventArgs e)
    {
        var tab = ActiveWindowTab;
        var structuredSource = tab.Tab.Catalog.TryGet("busylight", out var presenter) ? presenter : null;
        var window = new ControlPanelWindow(
            BusylightUiDefinition.Build(),
            new BusylightControlSurface(tab.Tab.Session),
            structuredSource)
        {
            Owner = this,
        };
        TrackControlPanel(window, tab);
        window.Show();
    }

    // Show(), not ShowDialog(): unlike Device Profiles (a one-shot picker), this panel is meant to
    // stay open and update live alongside the main window, not block it. Reuses the current, already
    // -open session rather than opening a second competing connection to the same physical device.
    private void RadexOneControlPanel_Click(object sender, RoutedEventArgs e)
    {
        var tab = ActiveWindowTab;
        var structuredSource = tab.Tab.Catalog.TryGet("radexone", out var presenter) ? presenter : null;
        var window = new ControlPanelWindow(
            RadexOneUiDefinition.Build(),
            new RadexOneControlSurface(tab.Tab.Session),
            structuredSource)
        {
            Owner = this,
        };
        TrackControlPanel(window, tab);
        window.Show();
    }

    // Show(), not ShowDialog(): unlike Device Profiles (a one-shot picker), this panel is meant to
    // stay open and update live alongside the main window, not block it. Reuses the current, already
    // -open session rather than opening a second competing connection to the same physical device.
    private void ZoomH4nControlPanel_Click(object sender, RoutedEventArgs e)
    {
        var tab = ActiveWindowTab;
        var structuredSource = tab.Tab.Catalog.TryGet("zoomh4n", out var presenter) ? presenter : null;
        var window = new ControlPanelWindow(
            ZoomH4nUiDefinition.Build(),
            new ZoomH4nControlSurface(tab.Tab.Session),
            structuredSource)
        {
            Owner = this,
        };
        TrackControlPanel(window, tab);
        window.Show();
    }

    // Show(), not ShowDialog(): unlike Device Profiles (a one-shot picker), this panel is meant to
    // stay open and update live alongside the main window, not block it. Reuses the current, already
    // -open session rather than opening a second competing connection to the same physical device.
    // Passes no session to the control surface itself (De5000ControlSurface takes none) - the DE-5000
    // has no writable commands, only live indicators driven by the structured presenter below.
    private void De5000ControlPanel_Click(object sender, RoutedEventArgs e)
    {
        var tab = ActiveWindowTab;
        var structuredSource = tab.Tab.Catalog.TryGet("de5000", out var presenter) ? presenter : null;
        var window = new ControlPanelWindow(
            De5000UiDefinition.Build(),
            new De5000ControlSurface(),
            structuredSource)
        {
            Owner = this,
        };
        TrackControlPanel(window, tab);
        window.Show();
    }

    // Show(), not ShowDialog(): same reasoning as De5000ControlPanel_Click above. Passes no session
    // to the control surface (NmeaGpsControlSurface takes none) - a GPS receiver has no writable
    // commands, only live indicators driven by the structured presenter below. The decoder/UI/
    // control surface are a generic NMEA 0183 GPS panel, not specific to the Earthmate BT-20 - only
    // this menu item's gate (DevicePanels.Nmea0183) is tied to that device's VID/PID. Not tracked
    // against a tab (unlike the panels above) since its control surface holds no session reference,
    // so it never goes stale on a profile switch/tab close.
    private void Nmea0183ControlPanel_Click(object sender, RoutedEventArgs e)
    {
        var structuredSource = ActiveWindowTab.Tab.Catalog.TryGet("nmea", out var presenter) ? presenter : null;
        var window = new ControlPanelWindow(
            NmeaGpsUiDefinition.Build(),
            new NmeaGpsControlSurface(),
            structuredSource)
        {
            Owner = this,
        };
        window.Show();
    }

    // ShowDialog(), not Show(): unlike the two panels above, this is a one-shot picker (mirrors
    // Device Profiles), and the resulting control panel is opened separately below with Show().
    // Device > Device Manifest...: pick and load a manifest, then open its panel (non-modal) on the
    // current session - see ManifestPickerWindow.
    private void DeviceManifest_Click(object sender, RoutedEventArgs e)
    {
        var tab = ActiveWindowTab;
        var picker = new ManifestPickerWindow(InstalledManifests.Discover()) { Owner = this };
        if (picker.ShowDialog() == true && picker.Chosen is { } manifest)
        {
            TrackControlPanel(ManifestPickerWindow.OpenPanel(this, tab.Tab.Session, manifest), tab);
        }
    }

    // Device > Edit Device Manifest...: the manifest editor, non-modal, always available (editing a
    // manifest needs no connection) - see ManifestEditorWindow.
    private void EditDeviceManifest_Click(object sender, RoutedEventArgs e)
    {
        var editor = ManifestEditorWindow.Create();
        editor.Owner = this;
        editor.Show();
    }

    private void ScpiInstrument_Click(object sender, RoutedEventArgs e)
    {
        var tab = ActiveWindowTab;
        var chosen = ResolveSavedScpiProfileChoice(tab.Tab.CliOptions.ScpiProfile);
        if (chosen is null)
        {
            var picker = new ScpiInstrumentPickerWindow { Owner = this };
            if (picker.ShowDialog() != true || picker.Chosen is not { } picked)
            {
                return;
            }

            chosen = picked;
        }

        var structuredSource = ResolveActiveScpiPresenter();
        if (chosen == ScpiInstrumentPickerWindow.AutoDetectChoice)
        {
            Observe(DetectAndOpenScpiInstrumentAsync(structuredSource));
            return;
        }

        var profile = chosen == ScpiInstrumentPickerWindow.GenericChoice
            ? ScpiProfileCatalog.Generic
            : ScpiProfileCatalog.All.First(p => p.Name == chosen);
        OpenScpiInstrumentWindow(tab, structuredSource, profile);
    }

    /// <summary>
    /// Resolves the active tab's registered "scpi" presenter and binds it into that tab's session's
    /// live pipeline if it isn't there already. <see cref="PresenterCatalog.TryGet"/> alone resolves
    /// the DI-registered singleton regardless of whether the user selected "scpi" for this connection
    /// (the pipeline is normally fixed at session-build time from
    /// <see cref="CliOptions.EffectivePresenters"/>), which used to silently break query/reply
    /// correlation: a Measure-style button still sent and the device still beeped, but the reply was
    /// never routed through <c>ScpiReplyPresenter</c> so it never appeared anywhere — see
    /// docs/changes/2026-09-23.md's real-hardware report. The fix binds the presenter onto the
    /// session's existing <see cref="Session.Presenters"/>/<see cref="Pipeline"/> instance in place
    /// (<see cref="Session.AddPresenter"/>) rather than resolving/rebuilding a new pipeline: the read
    /// loop already holds a reference to this one, immutable-from-the-outside instance for the whole
    /// life of the session, so anything not mutated into that same instance would never be seen by
    /// it. Mirrors <c>TuiMode.ResolveActiveScpiPresenter</c>.
    /// </summary>
    private IPresenter? ResolveActiveScpiPresenter()
    {
        var tab = ActiveWindowTab;
        if (!tab.Tab.Catalog.TryGet("scpi", out var presenter))
        {
            return null;
        }

        tab.Tab.Session.AddPresenter(presenter);
        return presenter;
    }

    /// <summary>
    /// Resolves a saved <see cref="CliOptions.ScpiProfile"/> choice to a picker-equivalent string,
    /// or <see langword="null"/> if it's unset/no longer resolvable — the latter falls back to
    /// showing <see cref="ScpiInstrumentPickerWindow"/> exactly as if nothing had been saved.
    /// Mirrors <c>TuiMode.ResolveSavedScpiProfileChoice</c>.
    /// </summary>
    private static string? ResolveSavedScpiProfileChoice(string? saved)
    {
        if (string.IsNullOrWhiteSpace(saved))
        {
            return null;
        }

        if (saved == ScpiInstrumentPickerWindow.AutoDetectChoice
            || saved == ScpiInstrumentPickerWindow.GenericChoice
            || ScpiProfileCatalog.All.Any(p => p.Name == saved))
        {
            return saved;
        }

        return null;
    }

    // *IDN? is a real send/await over the live transport, so unlike the synchronous picker above
    // this can't finish before the click handler returns - fire-and-forget (observed). Shares its
    // detect logic with the TUI (ScpiAutoDetect); reports progress while it waits (a wait cursor and
    // a status line) and what it found afterward, using the connection's configured timeout.
    /// <summary>internal so a test can drive the auto-detect-during-a-profile-switch race directly.</summary>
    internal async Task DetectAndOpenScpiInstrumentAsync(IPresenter? structuredSource)
    {
        if (ActiveWindowTabOrNull is not { } tab)
        {
            return;
        }

        // Captured so that if SwitchProfileAsync replaces this tab's Session/Catalog while this
        // detection is in flight, the completion below can tell and not open a panel pairing the NEW
        // session with structuredSource from the OLD catalog - part of bug 016, see
        // docs/bugs/resolved/016-wpf-panels-bound-to-old-session.md's "Related" note.
        var sessionAtStart = tab.Tab.Session;
        var timeout = TimeSpan.FromMilliseconds(tab.Tab.CliOptions.ScpiAutoDetectTimeoutMs);
        AppendOutput(tab, ScpiAutoDetect.ProgressMessage(timeout), OutputKind.Status);

        ScpiAutoDetectResult result;
        var previousCursor = Cursor;
        Cursor = System.Windows.Input.Cursors.Wait;
        try
        {
            result = await ScpiAutoDetect.DetectAsync(sessionAtStart, structuredSource, timeout);
        }
        catch (Exception ex)
        {
            // The *IDN? send failed - the session has disconnected itself and reported why, so
            // there's no connection to open a panel against.
            AppendOutput(tab, $"SCPI auto-detect failed: {ex.Message}", OutputKind.Error);
            return;
        }
        finally
        {
            Cursor = previousCursor;
        }

        if (!ReferenceEquals(tab.Tab.Session, sessionAtStart))
        {
            // The profile changed while auto-detect was waiting; the detected profile belongs to a
            // connection that's already closed, so there's nothing live to open a panel against.
            return;
        }

        AppendOutput(tab, result.Describe(timeout), OutputKind.Status);
        OpenScpiInstrumentWindow(tab, structuredSource, result.Profile ?? ScpiProfileCatalog.Generic);
    }

    // Show(), not ShowDialog(): unlike Device Profiles (a one-shot picker), this panel is meant to
    // stay open and update live alongside the main window, not block it. Reuses the current, already
    // -open session rather than opening a second competing connection to the same physical device.
    private void OpenScpiInstrumentWindow(WindowTab tab, IPresenter? structuredSource, ScpiInstrumentProfile profile)
    {
        if (structuredSource is ScpiReplyPresenter replyPresenter)
        {
            replyPresenter.ConfigureTerminator(profile.Terminator);
        }

        var window = new ControlPanelWindow(
            ScpiUiDefinitionBuilder.Build(profile),
            new ScpiControlSurface(tab.Tab.Session, profile, structuredSource as IScpiReplyTracker),
            structuredSource)
        {
            Owner = this,
        };
        TrackControlPanel(window, tab);
        window.Show();
    }

    /// <summary>
    /// The active tab's own Stream Monitor, pointed at its session and started — opening the window
    /// is asking to watch. Created on first use and kept for that tab's lifetime, scoped per-tab
    /// (docs/design/multi-session-ui.md's Step 4) so one tab's captures never land in another's
    /// output list; <see cref="CloseTabAsync"/> disposes it when the tab closes and
    /// <see cref="SwitchProfileAsync"/> moves it along with that tab's session. Split from the click
    /// handler so tests can drive it without showing a window.
    /// </summary>
    internal StreamMonitor EnsureStreamMonitor() => EnsureStreamMonitor(ActiveWindowTab);

    private StreamMonitor EnsureStreamMonitor(WindowTab tab)
    {
        if (tab.Monitor is null)
        {
            var monitor = new StreamMonitor();
            monitor.CaptureAdded += (_, capture) => Dispatcher.BeginInvoke(() => AppendOutput(tab, capture.Describe(), OutputKind.Status));
            tab.Monitor = monitor;
        }

        tab.Monitor.SetSession(tab.Tab.Session, StreamMonitor.DeviceNameFor(tab.Tab.CliOptions, _profileStore), tab.Tab.CliOptions.EffectiveExportDirectory);
        tab.Monitor.Start();
        return tab.Monitor;
    }

    // Show(), not ShowDialog(): like the control panels, it's meant to stay open and update live
    // alongside this window. A second click brings the already-open one forward.
    private void StreamMonitor_Click(object sender, RoutedEventArgs e)
    {
        var tab = ActiveWindowTab;
        var monitor = EnsureStreamMonitor(tab);
        if (tab.MonitorWindow is { } open)
        {
            open.RefreshState();
            open.Activate();
            return;
        }

        var window = new StreamMonitorWindow(monitor, tab.Tab.CliOptions) { Owner = this };
        window.Closed += (_, _) => tab.MonitorWindow = null;
        tab.MonitorWindow = window;
        window.Show();
    }

    /// <summary>
    /// Tears down the active tab's current session/transport and opens a new one composed from
    /// <paramref name="newOptions"/> — live, without restarting the app, unlike the
    /// save-as-default-and-ask-for-a-restart this replaced. Exposed as an awaitable method (rather
    /// than only reachable through <see cref="DeviceProfiles_Click"/>'s fire-and-forget call) so
    /// tests can drive and await it deterministically, the same convention as
    /// <see cref="ConnectAsync"/>/<see cref="ToggleConnectionAsync"/>.
    /// </summary>
    /// <returns><see langword="true"/> if the new connection opened successfully.</returns>
    internal async Task<bool> SwitchProfileAsync(CliOptions newOptions)
    {
        if (ActiveWindowTabOrNull is not { } tab)
        {
            return false;
        }

        tab.SwitchCts?.Cancel();
        var cts = new CancellationTokenSource();
        tab.SwitchCts = cts;

        DevTermSessionBuilder.Result built;
        try
        {
            built = DevTermSessionBuilder.Build(newOptions);
        }
        catch (Exception ex)
        {
            AppendOutput(tab, $"Could not switch profile: {ex.Message}", OutputKind.Error);
            return false;
        }

        var mySession = built.Session;

        // Captured now, before any await: a second, overlapping switch on this SAME tab reassigns
        // tab.Tab.Session below (once its own build/close/dispose completes) while this call is still
        // suspended closing/disposing its OWN old session. Reading tab.Tab.Session again after that
        // await - instead of this local - would tear down whatever the OTHER call had already
        // installed there (possibly its brand-new, just-opened session) rather than the session
        // this call actually meant to replace.
        var oldSession = tab.Tab.Session;

        // Every open control panel opened from THIS tab has an IControlSurface (and, for most, its
        // structured presenter) bound to the session/catalog being replaced below - closing them
        // here, rather than leaving them open against a disposed session, is what fixes bug 016.
        // ToArray: Closed removes each one from _openControlPanels as it fires, which would otherwise
        // mutate the list mid-iteration.
        foreach (var panel in _openControlPanels.Where(p => ReferenceEquals(p.Tag, tab)).ToArray())
        {
            panel.Close();
        }

        oldSession.Output -= tab.OutputHandler;
        oldSession.Disconnected -= tab.DisconnectedHandler;
        await oldSession.CloseAsync();
        await oldSession.DisposeAsync();

        if (!ReferenceEquals(tab.SwitchCts, cts))
        {
            // Superseded while closing the old session, before ever adopting mySession as current -
            // a newer switch on this tab has already moved tab.Tab.Session on (possibly to its own,
            // by-now-open session). Never having been subscribed or assigned to tab.Tab.Session,
            // mySession just needs disposing.
            await mySession.DisposeAsync();
            return false;
        }

        tab.Tab.Session = mySession;
        tab.Tab.Catalog = built.Catalog;
        tab.Tab.CliOptions = newOptions;
        tab.Tab.Parser = newOptions.EffectiveParser;
        tab.Monitor?.SetSession(mySession, StreamMonitor.DeviceNameFor(newOptions, _profileStore), newOptions.EffectiveExportDirectory);
        if (ReferenceEquals(tab, ActiveWindowTabOrNull))
        {
            ParserBox.ItemsSource = built.Catalog.InputNames;
            ParserBox.SelectedItem = newOptions.EffectiveParser;
        }

        mySession.Output += tab.OutputHandler;
        mySession.Disconnected += tab.DisconnectedHandler;
        FollowLogging(tab);

        // A different profile means a different device/connection - clearing prior output avoids
        // mixing readings from the old connection in with the new one.
        tab.OutputList.Items.Clear();
        if (ManifestNameWarning.For(newOptions) is { } manifestWarning)
        {
            AppendOutput(tab, manifestWarning, OutputKind.Status);
        }

        RefreshConnectionUi(tab, ConnectionState.Opening);
        try
        {
            await mySession.OpenAsync(cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Superseded by a newer switch before this one finished connecting - that newer attempt
            // owns the UI now, so this stale one reports nothing.
            return false;
        }
        catch (Exception ex)
        {
            if (!ReferenceEquals(tab.SwitchCts, cts))
            {
                // Superseded between the failure and this catch running - don't stomp the newer
                // attempt's state with a stale one.
                return false;
            }

            AppendOutput(tab, $"{ConnectionErrorMessages.For(tab.Tab.CliOptions.Transport, ex)} Use File > Connect to retry, or File > Device Profiles... to choose another connection.", OutputKind.Error);
            RefreshConnectionUi(tab);
            return false;
        }

        if (!ReferenceEquals(tab.SwitchCts, cts))
        {
            // Connected, but superseded in the meantime - close it rather than adopting a stray
            // connection as current.
            mySession.Output -= tab.OutputHandler;
            mySession.Disconnected -= tab.DisconnectedHandler;
            await mySession.CloseAsync();
            await mySession.DisposeAsync();
            return false;
        }

        RefreshConnectionUi(tab);
        AppendOutput(tab, $"Switched to {ConnectionDescription.For(tab.Tab.CliOptions)}.", OutputKind.Status);
        return true;
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closeConfirmed)
        {
            return;
        }

        // Session.CloseAsync/DisposeAsync must be awaited before the window actually closes, so
        // cancel the first close request, do the async cleanup, then close for real.
        e.Cancel = true;

        if (_closing)
        {
            // A second close request (Ctrl+Q, Alt+F4, File > Exit) arrived while the first
            // request's cleanup below is still running - _closeConfirmed isn't set until that
            // cleanup finishes, so without this guard a second request re-entered here and ran the
            // whole sequence (stream monitor dispose, session close, Close()) a second time
            // concurrently with the first. Just cancel this one too and let the first request's own
            // continuation finish the job once. See docs/bugs/resolved/058-wpf-onclosing-reentry.md.
            return;
        }

        _closing = true;
        ClosingCleanupRunCount++;
        foreach (var tab in _tabs)
        {
            tab.MonitorWindow?.Close();
            tab.Monitor?.Dispose();
            StopLogging(tab, report: false);
            tab.Tab.Session.Output -= tab.OutputHandler;
            tab.Tab.Session.Disconnected -= tab.DisconnectedHandler;
            try
            {
                await tab.Tab.Session.CloseAsync();
                await tab.Tab.Session.DisposeAsync();
            }
            catch (Exception)
            {
                // The window is closing regardless - a device that timed out or vanished mid-close
                // isn't worth crashing the app over (and would leave the window unclosable).
            }
        }

        _closeConfirmed = true;

        // Never call Close() from inside this Closing event's own call stack: when every tab's
        // session was already closed (never opened, or disconnected via the menu) the awaits above
        // complete synchronously, so this continuation runs *within* the first Close() and WPF throws
        // "Cannot ... Close ... while a Window is closing". Yielding lets that first Close() finish
        // unwinding (cancelled) before the real one is requested.
        await System.Windows.Threading.Dispatcher.Yield();
        Close();
    }
}
