using System.Text;
using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Testing;

namespace DevTerm.Console.Tests;

/// <summary>
/// Drives a real <see cref="TuiMode"/> window — real <c>Window</c>/<c>TextView</c>/<c>TextField</c>,
/// built by the actual production <see cref="TuiMode.BuildWindow"/> — using Terminal.Gui v2.5.0's
/// own official testing primitives (<c>Terminal.Gui.Testing.IInputInjector</c>, <c>IOutputBuffer</c>)
/// rather than OS-level UI Automation: no real terminal, real display, or external automation
/// library needed. See docs/design/testing.md for the investigation this came out of.
///
/// Two distinct modes exist here because of two real, empirically-confirmed constraints (not
/// documented anywhere — found by actually running code against the installed package, the same
/// rigor already applied to the WPF harness):
///
/// <list type="bullet">
/// <item><b><see cref="RunHeadless"/></b> — single-threaded, no <c>Application.Run()</c> loop:
/// <c>Init</c> → <c>Begin</c> → manual <c>LayoutAndDraw</c>. Key injection
/// (<see cref="TypeText"/>/<see cref="PressEnter"/>) only works reliably in this mode — injecting
/// through <c>IInputInjector</c> while a real <c>Application.Run()</c> loop is simultaneously
/// pumping on another thread was tried and the injected keys never reached the focused view, even
/// when the injection itself was marshaled onto the loop thread via <c>Application.Invoke</c> and
/// <c>ProcessQueue()</c> was called explicitly. Use this mode for anything that types into the send
/// field, and for a plain rendered-buffer "screenshot".</item>
/// <item><b><see cref="RunWithLoop"/></b> — a dedicated background thread runs the real, blocking
/// <c>Application.Run()</c> loop; the test thread marshals in via <c>Application.Invoke</c>
/// (mirrors <c>DevTerm.Wpf.Tests.StaTestRunner</c>'s dedicated STA thread). <c>Application.Invoke</c>
/// silently queues forever and is never drained unless a real <c>Run()</c> loop is actively pumping
/// — confirmed by calling it from a background thread with no loop running and finding
/// <c>LayoutAndDraw</c>/<c>RaiseIteration</c> never flush it. Since <c>TuiMode</c>'s own
/// <c>AppendOutput</c> uses <c>Application.Invoke</c> to marshal <c>Session.Output</c> events
/// (raised on <c>Session</c>'s own background read-loop <c>Task</c>) onto the UI, exercising that
/// specific wiring needs this mode instead.</item>
/// </list>
/// </summary>
internal static class TuiTestRunner
{
    private static readonly TimeSpan _startTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _stopTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _invokeTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// A one-presenter catalog for tests that fake a single presenter: the send format is pinned to
    /// that presenter's name (a default <see cref="CliOptions"/> would otherwise ask for "hex", which
    /// a catalog holding only, say, ASCII can't resolve).
    /// </summary>
    public static PresenterCatalog CatalogFor(IPresenter presenter, CliOptions cliOptions)
    {
        cliOptions.Parser ??= presenter.Name;
        return new PresenterCatalog([presenter]);
    }

    /// <summary>
    /// A store over a directory that doesn't exist, so a window built under test never reads the
    /// developer's real <c>~/.dev-term/profiles</c> when its title asks "is this a saved profile?".
    /// </summary>
    public static ConnectionProfileStore EmptyProfiles() =>
        new(Path.Combine(Path.GetTempPath(), $"devterm-tests-{Guid.NewGuid():N}"));

    public static void RunHeadless(Session session, IPresenter presenter, CliOptions cliOptions, Action<TuiWindowParts> body, ConnectionProfileStore? profileStore = null) =>
        RunHeadless(session, CatalogFor(presenter, cliOptions), cliOptions, body, profileStore);

    public static void RunHeadless(Session session, PresenterCatalog presenter, CliOptions cliOptions, Action<TuiWindowParts> body, ConnectionProfileStore? profileStore = null)
    {
        Application.Init("dotnet");
        try
        {
            var parts = TuiMode.BuildWindow(session, presenter, cliOptions, profileStore ?? EmptyProfiles());
            parts.SendField.SetFocus();
            var token = Application.Begin(parts.Window);
            Application.LayoutAndDraw(true);

            try
            {
                body(parts);
            }
            finally
            {
                Application.End(token);
            }
        }
        finally
        {
            Application.Shutdown();
        }
    }

    /// <summary>Injects each character of <paramref name="text"/> as a key press into whichever view currently has focus (see <see cref="RunHeadless"/>'s doc comment for why this only works headlessly, not with a running <c>Application.Run()</c> loop).</summary>
    public static void TypeText(string text)
    {
        var injector = Application.Instance!.GetInputInjector();
        foreach (var ch in text)
        {
            injector.InjectKey(new Key(ch), new InputInjectionOptions());
        }

        injector.ProcessQueue();
        Application.LayoutAndDraw(true);
    }

    public static void PressEnter() => PressKey(Key.Enter);

    public static void PressKey(Key key)
    {
        var injector = Application.Instance!.GetInputInjector();
        injector.InjectKey(key, new InputInjectionOptions());
        injector.ProcessQueue();
        Application.LayoutAndDraw(true);
    }

    /// <summary>Renders the current screen buffer as plain text, one line per row — a text "screenshot" of the TUI, usable both for assertions and for docs/user-guide.</summary>
    public static string DumpBuffer()
    {
        var buffer = Application.Driver!.GetOutputBuffer();
        var text = new StringBuilder();
        for (var row = 0; row < buffer.Rows; row++)
        {
            for (var col = 0; col < buffer.Cols; col++)
            {
                text.Append(buffer.Contents[row, col].Grapheme);
            }

            text.Append('\n');
        }

        return text.ToString();
    }

    public static void RunWithLoop(Session session, IPresenter presenter, CliOptions cliOptions, Action<TuiWindowParts> body, ConnectionProfileStore? profileStore = null) =>
        RunWithLoop(session, CatalogFor(presenter, cliOptions), cliOptions, body, profileStore);

    public static void RunWithLoop(Session session, PresenterCatalog presenter, CliOptions cliOptions, Action<TuiWindowParts> body, ConnectionProfileStore? profileStore = null)
    {
        TuiWindowParts? parts = null;
        var ready = new ManualResetEventSlim(false);
        Exception? threadException = null;

        var thread = new Thread(() =>
        {
            try
            {
                Application.Init("dotnet");
                parts = TuiMode.BuildWindow(session, presenter, cliOptions, profileStore ?? EmptyProfiles());
                parts.SendField.SetFocus();
                Application.Invoke(() => ready.Set());
                Application.Run(parts.Window);
            }
            catch (Exception ex)
            {
                threadException = ex;
                ready.Set();
            }
        })
        {
            IsBackground = true,
        };
        thread.Start();

        if (!ready.Wait(_startTimeout))
        {
            throw new TimeoutException("The TUI run loop did not start in time.");
        }

        if (threadException is not null)
        {
            throw new InvalidOperationException("The TUI run loop failed to start.", threadException);
        }

        try
        {
            body(parts!);
        }
        finally
        {
            Application.Invoke(() => Application.RequestStop());
            thread.Join(_stopTimeout);
            Application.Shutdown();
        }
    }

    /// <summary>
    /// Same real-<c>Application.Run()</c>-loop pattern as <see cref="RunWithLoop(Session, PresenterCatalog, CliOptions, Action{TuiWindowParts}, ConnectionProfileStore)"/>,
    /// generalized to any window built by <paramref name="buildParts"/> — used for a control panel
    /// (<see cref="DevTerm.Console.ControlPanelMode.BuildWindow"/>) rather than <see cref="TuiMode"/>
    /// itself, e.g. to capture a live-updating indicator that only marshals via
    /// <c>Application.Invoke</c> when a real loop is pumping.
    /// </summary>
    public static void RunWithLoop(Func<ControlPanelWindowParts> buildParts, Action<ControlPanelWindowParts> body)
    {
        ControlPanelWindowParts? parts = null;
        var ready = new ManualResetEventSlim(false);
        Exception? threadException = null;

        var thread = new Thread(() =>
        {
            try
            {
                Application.Init("dotnet");
                parts = buildParts();
                Application.Invoke(() => ready.Set());
                Application.Run(parts.Window);
            }
            catch (Exception ex)
            {
                threadException = ex;
                ready.Set();
            }
        })
        {
            IsBackground = true,
        };
        thread.Start();

        if (!ready.Wait(_startTimeout))
        {
            throw new TimeoutException("The TUI run loop did not start in time.");
        }

        if (threadException is not null)
        {
            throw new InvalidOperationException("The TUI run loop failed to start.", threadException);
        }

        try
        {
            body(parts!);
        }
        finally
        {
            Application.Invoke(() => Application.RequestStop());
            thread.Join(_stopTimeout);
            Application.Shutdown();
        }
    }

    /// <summary>Runs <paramref name="func"/> on the TUI loop thread (see <see cref="RunWithLoop"/>) and waits for it to complete, so reads of view state don't race the loop's own redraw/input processing.</summary>
    public static T InvokeOnLoop<T>(Func<T> func)
    {
        var done = new ManualResetEventSlim(false);
        var result = default(T);
        Exception? exception = null;

        Application.Invoke(() =>
        {
            try
            {
                result = func();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
            finally
            {
                done.Set();
            }
        });

        if (!done.Wait(_invokeTimeout))
        {
            throw new TimeoutException("Application.Invoke did not run within the timeout.");
        }

        if (exception is not null)
        {
            throw new InvalidOperationException("Action invoked on the TUI loop thread threw.", exception);
        }

        return result!;
    }

    public static bool WaitUntilOnLoop(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (InvokeOnLoop(predicate))
            {
                return true;
            }

            Thread.Sleep(20);
        }

        return false;
    }
}
