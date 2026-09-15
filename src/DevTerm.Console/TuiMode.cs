using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;
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
            Title = $"dev-term — {ConnectionDescription.For(cliOptions)} ({presenter.Name})",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };

        var output = new TextView
        {
            X = 0,
            Y = 1,
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
            Enabled = session.State == ConnectionState.Open,
        };

        void AppendOutput(string line)
        {
            Application.Invoke(() =>
            {
                output.Text += output.Text.Length == 0 ? line : "\n" + line;
                output.MoveEnd();
            });
        }

        var connectMenuItem = new MenuItem(
            session.State == ConnectionState.Open ? "_Disconnect" : "_Connect",
            string.Empty,
            () => { });
        connectMenuItem.Action = () => _ = ToggleConnectionAsync(session, cliOptions, connectMenuItem, sendField, AppendOutput);

        var menuBar = new MenuBar(
        [
            new MenuBarItem("_File",
            [
                connectMenuItem,
                new MenuItem("_Device Profiles...", string.Empty, () =>
                {
                    var configureParts = ConfigureMode.BuildWindow(cliOptions, null, new ConnectionProfileStore());
                    Application.Run(configureParts.Window);

                    if (configureParts.Result is { } chosen)
                    {
                        DevTermConfiguration.SaveLocalProfile(chosen);
                        AppendOutput($"Saved '{ConnectionDescription.For(chosen)}' as the default profile — restart dev-term to connect with it.");
                    }
                }),
                new MenuItem("_Quit", "Ctrl+Q", () => Application.RequestStop(), Key.Q.WithCtrl),
            ]),
        ]);

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
        EventHandler<Key>? quitOnCtrlQ = null;
        quitOnCtrlQ = (_, key) =>
        {
            if (key != Key.Q.WithCtrl)
            {
                return;
            }

            key.Handled = true;
            Application.RequestStop();
        };
        Application.KeyDown += quitOnCtrlQ;
        window.Disposing += (_, _) => Application.KeyDown -= quitOnCtrlQ;

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

            if (session.State != ConnectionState.Open)
            {
                AppendOutput("Not connected — use File > Connect.");
                return;
            }

            if (presenter is not IPresenterInput input)
            {
                AppendOutput($"Presenter '{presenter.Name}' does not support sending.");
                return;
            }

            _ = SendAsync(session, cliOptions, input, line, AppendOutput);
        };

        window.Add(menuBar, output, sendLabel, sendField);

        return new TuiWindowParts(window, output, sendField, connectMenuItem);
    }

    /// <summary>
    /// The File > Connect/Disconnect menu item's action: closes an open session, or reopens a
    /// closed one, updating the menu item's own label (Terminal.Gui's <c>MenuItem.Title</c> is
    /// mutable, unlike its <c>Key</c> shortcut argument — see the Ctrl+Q comment above) and the
    /// send field's enabled state to match. Exposed as a testable method (not just reachable
    /// through the menu item's <c>Action</c> delegate) the same way <see cref="SendAsync"/> is.
    /// </summary>
    internal static async Task ToggleConnectionAsync(Session session, CliOptions cliOptions, MenuItem connectMenuItem, TextField sendField, Action<string> appendOutput)
    {
        if (session.State == ConnectionState.Open)
        {
            await session.CloseAsync();
            Application.Invoke(() =>
            {
                connectMenuItem.Title = "_Connect";
                sendField.Enabled = false;
            });
            appendOutput("Disconnected.");
            return;
        }

        try
        {
            await session.OpenAsync();
        }
        catch (Exception ex) when (ConnectionErrorMessages.IsConnectionFailure(ex))
        {
            appendOutput(ConnectionErrorMessages.For(cliOptions.Transport, ex));
            return;
        }

        Application.Invoke(() =>
        {
            connectMenuItem.Title = "_Disconnect";
            sendField.Enabled = true;
        });
        appendOutput($"Connected to {ConnectionDescription.For(cliOptions)}.");
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
internal sealed record TuiWindowParts(Window Window, TextView Output, TextField SendField, MenuItem ConnectMenuItem);
