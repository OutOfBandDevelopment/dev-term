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
            sendField.SetFocus();

            Application.Run(window);
        }
        finally
        {
            Application.Shutdown();
        }

        await session.CloseAsync();
        return 0;
    }

    private static async Task SendAsync(Session session, CliOptions cliOptions, IPresenterInput input, string line, Action<string> appendOutput)
    {
        try
        {
            await session.SendAsync(cliOptions.LineEnding.Append(input.Parse(line)));
        }
        catch (TimeoutException)
        {
            appendOutput("Send timed out — no response to hardware flow control (CTS)? Check the device or --handshake.");
        }
    }
}
