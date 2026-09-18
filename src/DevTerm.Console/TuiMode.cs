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
    public static async Task<int> RunAsync(Session session, PresenterCatalog catalog, CliOptions cliOptions, ConnectionProfileStore? profileStore = null)
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
            var parts = BuildWindow(session, catalog, cliOptions, profileStore);
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
    internal static TuiWindowParts BuildWindow(Session session, PresenterCatalog catalog, CliOptions cliOptions, ConnectionProfileStore? profileStore = null)
    {
        // Also what "is this connection a saved profile?" (the title) is answered against, and what the
        // Device Profiles screen edits - a test passes an isolated one rather than the real user folder.
        profileStore ??= new ConnectionProfileStore();

        // The parser (send format) currently encoding typed lines - starts as the profile's, and
        // the "Send as" menu switches it for every line typed afterward. Captured/reassigned by the
        // closures below like session/cliOptions are (see SwitchProfileAsync's comment).
        var parser = cliOptions.EffectiveParser;

        string TitleFor() => ConnectionDescription.WindowTitle(cliOptions, parser, profileStore);

        var window = new Window
        {
            Title = TitleFor(),
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
            Text = ManifestNameWarning.For(cliOptions) ?? string.Empty,
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

        void SetParser(string name)
        {
            // Called from a menu item's action, already on the UI thread - no Application.Invoke
            // needed (and it would never flush under a headless test without a real run loop).
            parser = name;
            window.Title = TitleFor();
        }

        var menuBar = new MenuBar(
        [
            new MenuBarItem("_File",
            [
                connectMenuItem,
                new MenuItem("_Device Profiles...", string.Empty, () =>
                {
                    var configureParts = ConfigureMode.BuildWindow(cliOptions, null, profileStore);
                    Application.Run(configureParts.Window);

                    if (configureParts.Result is { } chosen)
                    {
                        DevTermConfiguration.SaveLocalProfile(chosen);
                        _ = SwitchProfileAsync(chosen);
                    }
                }),
                new MenuItem("_Quit", "Ctrl+Q", () => Application.RequestStop(), Key.Q.WithCtrl),
            ]),
            // One entry per presenter that can encode typed text; picking one applies from the next
            // line typed on (the title bar shows which is current). Built from the catalog as of
            // startup - a profile switch never changes which presenters are registered.
            new MenuBarItem("_Send as", [.. catalog.InputNames.Select(name => new MenuItem(name, string.Empty, () => SetParser(name)))]),
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
        async Task<bool> SwitchProfileAsync(CliOptions newOptions)
        {
            DevTermSessionBuilder.Result built;
            try
            {
                built = DevTermSessionBuilder.Build(newOptions);
            }
            catch (Exception ex)
            {
                AppendOutput($"Could not switch profile: {ex.Message}");
                return false;
            }

            session.Output -= OnSessionOutput;
            await session.CloseAsync();
            await session.DisposeAsync();

            session = built.Session;
            catalog = built.Catalog;
            cliOptions = newOptions;
            parser = newOptions.EffectiveParser;
            session.Output += OnSessionOutput;

            Application.Invoke(() =>
            {
                window.Title = TitleFor();
                output.Text = string.Empty;
            });

            if (ManifestNameWarning.For(cliOptions) is { } manifestWarning)
            {
                AppendOutput(manifestWarning);
            }

            try
            {
                await session.OpenAsync();
            }
            catch (Exception ex) when (ConnectionErrorMessages.IsConnectionFailure(ex))
            {
                AppendOutput(ConnectionErrorMessages.For(cliOptions.Transport, ex));
                Application.Invoke(() =>
                {
                    connectMenuItem.Title = "_Connect";
                    sendField.Enabled = false;
                });
                return false;
            }

            Application.Invoke(() =>
            {
                connectMenuItem.Title = "_Disconnect";
                sendField.Enabled = true;
            });
            AppendOutput($"Switched to {ConnectionDescription.For(cliOptions)}.");
            return true;
        }

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

            if (!catalog.TryGetInput(parser, out var input))
            {
                AppendOutput($"Parser '{parser}' does not support sending.");
                return;
            }

            _ = SendAsync(session, cliOptions, input, line, AppendOutput);
        };

        window.Add(menuBar, output, sendLabel, sendField);

        return new TuiWindowParts(window, output, sendField, connectMenuItem, SwitchProfileAsync, SetParser);
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

/// <summary>The controls a test needs to drive the TUI headlessly: inject keys into <see cref="SendField"/>, read rendered text back from <see cref="Output"/>, drive a live profile switch directly via <see cref="SwitchProfileAsync"/> (the same delegate the "File &gt; Device Profiles..." menu item calls), or switch the send format via <see cref="SetParser"/> (what a "Send as" menu item calls).</summary>
internal sealed record TuiWindowParts(Window Window, TextView Output, TextField SendField, MenuItem ConnectMenuItem, Func<CliOptions, Task<bool>> SwitchProfileAsync, Action<string> SetParser);
