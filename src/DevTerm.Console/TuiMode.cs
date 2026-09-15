using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Views;
using Terminal.Gui.ViewBase;

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
    public static async Task<int> RunAsync(Session session, IPresenter presenter, CliOptions cliOptions)
    {
        try
        {
            await session.OpenAsync();
        }
        catch (Exception ex) when (ConnectionErrorMessages.IsConnectionFailure(ex))
        {
            System.Console.Error.WriteLine(ConnectionErrorMessages.For(cliOptions.Transport, ex));
            return 1;
        }

        Application.Init();
        try
        {
            var parts = BuildWindow(session, presenter, cliOptions);
            parts.SendField.SetFocus();
            Application.Run(parts.Window);
        }
        finally
        {
            Application.Shutdown();
        }

        await session.CloseAsync();
        return 0;
    }

    /// <summary>
    /// Builds the window and wires it to <paramref name="session"/>, without touching
    /// <c>Application.Init</c>/<c>Run</c>/<c>Shutdown</c> — split out so tests can drive the same
    /// production controls headlessly (see <c>DevTerm.Console.Tests.TuiModeTests</c>), the same
    /// seam <c>MainWindow.xaml.cs</c> exposes for WPF (<c>ConnectAsync</c>/<c>SendCurrentInputAsync</c>).
    /// </summary>
    internal static TuiWindowParts BuildWindow(Session session, IPresenter presenter, CliOptions cliOptions)
    {
        var window = new Window
        {
            Title = $"dev-term — {ConnectionDescription.For(cliOptions)} ({presenter.Name}) — Ctrl+Q to quit",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };

        var output = new TextView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            ReadOnly = true,
            Text = string.Empty,
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
        };

        void AppendOutput(string line)
        {
            Application.Invoke(() =>
            {
                output.Text += output.Text.Length == 0 ? line : "\n" + line;
                output.MoveEnd();
            });
        }

        session.Output += (_, presenterOutput) => AppendOutput($"[{presenterOutput.PresenterName}] {presenterOutput.Text}");

        sendField.KeyDown += (_, key) =>
        {
            if (key != Key.Enter)
            {
                return;
            }

            key.Handled = true;
            var line = sendField.Text;
            sendField.Text = string.Empty;

            if (line.Length == 0)
            {
                return;
            }

            if (presenter is not IPresenterInput input)
            {
                AppendOutput($"Presenter '{presenter.Name}' does not support sending.");
                return;
            }

            _ = SendAsync(session, cliOptions, input, line, AppendOutput);
        };

        window.Add(output, sendLabel, sendField);

        return new TuiWindowParts(window, output, sendField);
    }

    internal static async Task SendAsync(Session session, CliOptions cliOptions, IPresenterInput input, string line, Action<string> appendOutput)
    {
        var payload = cliOptions.LineEnding.Append(input.Parse(line));
        if (payload.Length == 0)
        {
            return;
        }

        try
        {
            await session.SendAsync(payload);
        }
        catch (TimeoutException)
        {
            appendOutput("Send timed out — no response to hardware flow control (CTS)? Check the device or --handshake.");
        }
        catch (Exception ex) when (ConnectionErrorMessages.IsConnectionFailure(ex))
        {
            appendOutput($"Send failed: {ex.Message}");
        }
    }
}

/// <summary>The controls a test needs to drive the TUI headlessly: inject keys into <see cref="SendField"/>, read rendered text back from <see cref="Output"/>.</summary>
internal sealed record TuiWindowParts(Window Window, TextView Output, TextField SendField);
