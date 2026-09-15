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
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
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
            Send();
        }
    }

    private void Send_Click(object sender, RoutedEventArgs e) => Send();

    private async void Send()
    {
        var line = SendBox.Text;
        SendBox.Clear();

        if (line.Length == 0 || _presenter is not IPresenterInput input)
        {
            return;
        }

        try
        {
            await _session.SendAsync(_cliOptions.LineEnding.Append(input.Parse(line)));
        }
        catch (TimeoutException)
        {
            OutputList.Items.Add("Send timed out — no response to hardware flow control (CTS)? Check the device or --handshake.");
        }
    }

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
