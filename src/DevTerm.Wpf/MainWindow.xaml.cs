using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;

namespace DevTerm.Wpf;

/// <summary>
/// A first stub of the GUI front end (see docs/design/frontends.md): a scrolling output list and
/// a send box, backed by the same <see cref="Session"/> the console app's CLI/TUI modes use. Not
/// the full design (no rendering-presenter drawings, no device control panels, no session
/// switching) — see frontends.md's GUI section for the target.
/// </summary>
public partial class MainWindow : Window
{
    private readonly Session _session;
    private readonly IPresenter _presenter;
    private readonly CliOptions _cliOptions;
    private bool _closeConfirmed;

    public MainWindow(Session session, IPresenter presenter, CliOptions cliOptions)
    {
        InitializeComponent();

        _session = session;
        _presenter = presenter;
        _cliOptions = cliOptions;

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

        Title = $"dev-term — {ConnectionDescription.For(_cliOptions)} ({_presenter.Name})";
        SendBox.IsEnabled = _presenter is IPresenterInput;
        SendBox.Focus();
    }

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

        if (line.Length == 0 || _presenter is not IPresenterInput input)
        {
            return;
        }

        var payload = _cliOptions.LineEnding.Append(input.Parse(line));
        if (payload.Length == 0)
        {
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
        var window = new DeviceProfilesWindow(new ConnectionProfileStore()) { Owner = this };
        window.ShowDialog();
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
        await _session.CloseAsync();
        await _session.DisposeAsync();
        _closeConfirmed = true;
        Close();
    }
}
