using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;
using DevTerm.Devices.K8055;

namespace DevTerm.Wpf;

/// <summary>
/// A first stub of the GUI front end (see docs/design/frontends.md): a scrolling output list and
/// a send box, backed by the same <see cref="Session"/> the console app's CLI/TUI modes use. Not
/// the full design (no rendering-presenter drawings, no device control panels, no session
/// switching) — see frontends.md's GUI section for the target.
/// </summary>
public partial class MainWindow : Window
{
    private Session _session;
    private PresenterCatalog _catalog;
    private CliOptions _cliOptions;
    private readonly ConnectionProfileStore _profileStore;
    private bool _closeConfirmed;

    /// <param name="profileStore">What the title checks "is this connection a saved profile?" against, and what the Device Profiles window edits — defaults to the user's real profiles folder; a test passes an isolated one.</param>
    public MainWindow(Session session, PresenterCatalog catalog, CliOptions cliOptions, ConnectionProfileStore? profileStore = null)
    {
        InitializeComponent();
        _profileStore = profileStore ?? new ConnectionProfileStore();

        _session = session;
        _catalog = catalog;
        _cliOptions = cliOptions;

        ParserBox.ItemsSource = catalog.InputNames;
        ParserBox.SelectedItem = cliOptions.EffectiveParser;

        if (ManifestNameWarning.For(cliOptions) is { } manifestWarning)
        {
            OutputList.Items.Add(manifestWarning);
        }

        _session.Output += OnSessionOutput;
        Loaded += OnLoaded;
        Closing += OnClosing;

        // MenuItem.InputGestureText only labels the shortcut in the menu - it doesn't register a
        // live accelerator by itself (same gotcha found for Terminal.Gui's MenuItem.Key building
        // the TUI's own menu - see TuiMode.BuildWindow), so Ctrl+Q needs an explicit handler too.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Q && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                Close();
            }
        };
    }

    /// <summary>The send format (parser) currently encoding typed lines — the "Send as" box's selection, starting as the profile's.</summary>
    internal string CurrentParser => ParserBox.SelectedItem as string ?? _cliOptions.EffectiveParser;

    private string TitleText => ConnectionDescription.WindowTitle(_cliOptions, CurrentParser, _profileStore);

    // Only refreshes a title that's already been set for a connection; before ConnectAsync runs the
    // title is still the XAML's plain "dev-term".
    private void ParserBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_session.State == ConnectionState.Open)
        {
            Title = TitleText;
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await ConnectAsync();

    /// <summary>
    /// The connect logic <see cref="OnLoaded"/> triggers, exposed as an awaitable method (rather
    /// than only reachable through the <c>async void</c> event handler) so tests can drive and
    /// await it deterministically.
    /// </summary>
    internal async Task ConnectAsync()
    {
        try
        {
            await _session.OpenAsync();
        }
        catch (Exception ex) when (ConnectionErrorMessages.IsConnectionFailure(ex))
        {
            MessageBox.Show(
                ConnectionErrorMessages.For(_cliOptions.Transport, ex),
                "dev-term — connection failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Close();
            return;
        }

        Title = TitleText;
        ConnectMenuItem.Header = "_Disconnect";
        SendBox.IsEnabled = true;
        SendBox.Focus();
    }

    /// <summary>
    /// The File > Connect/Disconnect menu item's action: closes an open session, or reopens a
    /// closed one, updating the menu item's own label and <see cref="SendBox"/>'s enabled state to
    /// match — the WPF equivalent of <c>TuiMode.ToggleConnectionAsync</c>.
    /// </summary>
    internal async Task ToggleConnectionAsync()
    {
        if (_session.State == ConnectionState.Open)
        {
            await _session.CloseAsync();
            ConnectMenuItem.Header = "_Connect";
            SendBox.IsEnabled = false;
            OutputList.Items.Add("Disconnected.");
            return;
        }

        try
        {
            await _session.OpenAsync();
        }
        catch (Exception ex) when (ConnectionErrorMessages.IsConnectionFailure(ex))
        {
            MessageBox.Show(
                ConnectionErrorMessages.For(_cliOptions.Transport, ex),
                "dev-term — connection failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            // Unlike SwitchProfileAsync (which already resets these on a failed attempt), this
            // reuses the same session/transport across retries - but the menu label/send box still
            // need to reflect "not connected" on a failed *retry*, not just a failed first attempt
            // (ConnectAsync already leaves them alone on a failed first attempt, since it closes the
            // window instead). Same asymmetry found and fixed in TuiMode.ToggleConnectionAsync.
            ConnectMenuItem.Header = "_Connect";
            SendBox.IsEnabled = false;
            return;
        }

        ConnectMenuItem.Header = "_Disconnect";
        SendBox.IsEnabled = true;
        OutputList.Items.Add($"Connected to {ConnectionDescription.For(_cliOptions)}.");
    }

    private void ConnectMenuItem_Click(object sender, RoutedEventArgs e) => _ = ToggleConnectionAsync();

    private void OnSessionOutput(object? sender, PresenterOutput output)
    {
        Dispatcher.Invoke(() =>
        {
            OutputList.Items.Add($"[{output.PresenterName}] {output.Text}");
            if (OutputList.Items.Count > 0)
            {
                OutputList.ScrollIntoView(OutputList.Items[^1]);
            }
        });
    }

    private void SendBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _ = SendCurrentInputAsync();
        }
    }

    private void Send_Click(object sender, RoutedEventArgs e) => _ = SendCurrentInputAsync();

    /// <summary>
    /// Sends whatever's currently in <see cref="SendBox"/>, exposed as an awaitable method (rather
    /// than only reachable through the fire-and-forget UI event handlers) so tests can drive and
    /// await it deterministically.
    /// </summary>
    internal async Task SendCurrentInputAsync()
    {
        var line = SendBox.Text;
        SendBox.Clear();

        if (line.Length == 0 || !_catalog.TryGetInput(CurrentParser, out var input))
        {
            return;
        }

        var payload = _cliOptions.LineEnding.Append(input.Parse(line));
        if (payload.Length == 0)
        {
            return;
        }

        if (_session.State != ConnectionState.Open)
        {
            OutputList.Items.Add("Not connected — use File > Connect.");
            return;
        }

        try
        {
            await _session.SendAsync(payload);
        }
        catch (TimeoutException)
        {
            OutputList.Items.Add("Send timed out — no response to hardware flow control (CTS)? Check the device or --handshake.");
        }
        catch (Exception ex) when (ConnectionErrorMessages.IsConnectionFailure(ex))
        {
            // Matches CliMode/TuiMode's send-path handling (see docs/changes/2026-09-15.md): a
            // generic text presenter's typed input can't guarantee it matches a specific device's
            // framing requirements, so report the failure instead of crashing.
            OutputList.Items.Add($"Send failed: {ex.Message}");
        }
    }

    private void DeviceProfiles_Click(object sender, RoutedEventArgs e)
    {
        var window = new DeviceProfilesWindow(_profileStore, _cliOptions) { Owner = this };
        window.ShowDialog();

        if (window.Result is { } chosen)
        {
            DevTermConfiguration.SaveLocalProfile(chosen);
            _ = SwitchProfileAsync(chosen);
        }
    }

    // Show(), not ShowDialog(): unlike Device Profiles (a one-shot picker), this panel is meant to
    // stay open and update live alongside the main window, not block it. Reuses the current, already
    // -open _session rather than opening a second competing connection to the same physical device.
    private void K8055ControlPanel_Click(object sender, RoutedEventArgs e)
    {
        var structuredSource = _catalog.TryGet("k8055", out var presenter) ? presenter : null;
        var window = new ControlPanelWindow(
            K8055UiDefinition.Build(),
            new K8055ControlSurface(_session),
            structuredSource)
        {
            Owner = this,
        };
        window.Show();
    }

    /// <summary>
    /// Tears down the current session/transport and opens a new one composed from
    /// <paramref name="newOptions"/> — live, without restarting the app, unlike the
    /// save-as-default-and-ask-for-a-restart this replaced. Exposed as an awaitable method (rather
    /// than only reachable through <see cref="DeviceProfiles_Click"/>'s fire-and-forget call) so
    /// tests can drive and await it deterministically, the same convention as
    /// <see cref="ConnectAsync"/>/<see cref="ToggleConnectionAsync"/>.
    /// </summary>
    /// <returns><see langword="true"/> if the new connection opened successfully.</returns>
    internal async Task<bool> SwitchProfileAsync(CliOptions newOptions)
    {
        DevTermSessionBuilder.Result built;
        try
        {
            built = DevTermSessionBuilder.Build(newOptions);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "dev-term", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        _session.Output -= OnSessionOutput;
        await _session.CloseAsync();
        await _session.DisposeAsync();

        _session = built.Session;
        _catalog = built.Catalog;
        _cliOptions = newOptions;
        ParserBox.SelectedItem = newOptions.EffectiveParser;
        _session.Output += OnSessionOutput;

        // A different profile means a different device/connection - clearing prior output avoids
        // mixing readings from the old connection in with the new one.
        OutputList.Items.Clear();
        if (ManifestNameWarning.For(newOptions) is { } manifestWarning)
        {
            OutputList.Items.Add(manifestWarning);
        }

        try
        {
            await _session.OpenAsync();
        }
        catch (Exception ex) when (ConnectionErrorMessages.IsConnectionFailure(ex))
        {
            MessageBox.Show(
                ConnectionErrorMessages.For(_cliOptions.Transport, ex),
                "dev-term — connection failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            ConnectMenuItem.Header = "_Connect";
            SendBox.IsEnabled = false;
            return false;
        }

        Title = TitleText;
        ConnectMenuItem.Header = "_Disconnect";
        SendBox.IsEnabled = true;
        OutputList.Items.Add($"Switched to {ConnectionDescription.For(_cliOptions)}.");
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
        _session.Output -= OnSessionOutput;
        try
        {
            await _session.CloseAsync();
            await _session.DisposeAsync();
        }
        catch (Exception ex) when (ConnectionErrorMessages.IsConnectionFailure(ex))
        {
            // The window is closing regardless - a device that timed out or vanished mid-close
            // isn't worth crashing the app over (and would leave the window unclosable).
        }

        _closeConfirmed = true;

        // Never call Close() from inside this Closing event's own call stack: when the session was
        // already closed (never opened, or disconnected via the menu) the awaits above complete
        // synchronously, so this continuation runs *within* the first Close() and WPF throws
        // "Cannot ... Close ... while a Window is closing". Yielding lets that first Close() finish
        // unwinding (cancelled) before the real one is requested.
        await System.Windows.Threading.Dispatcher.Yield();
        Close();
    }
}
