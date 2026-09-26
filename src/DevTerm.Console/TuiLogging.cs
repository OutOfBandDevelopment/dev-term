using DevTerm.Configuration;
using DevTerm.Core.Sessions;
using DevTerm.Logging;
using Terminal.Gui.App;
using Terminal.Gui.Views;

namespace DevTerm.Console;

/// <summary>
/// The TUI main window's logger-mode state: the running <see cref="SessionLogger"/> (if any) and the
/// File menu item that starts/stops it, kept out of <see cref="TuiMode.BuildWindow"/> so that method
/// only wires it in. See docs/specs/tui-main-screen.md and docs/design/session-logging.md.
/// </summary>
internal sealed class TuiLogging
{
    public const string StartTitle = "Start _Logging...";
    public const string StopTitle = "Stop _Logging";

    public TuiLogging() => MenuItem = new MenuItem(StartTitle, string.Empty, () => { });

    /// <summary>File &gt; Start Logging... / Stop Logging — one item whose title flips, like Connect/Disconnect.</summary>
    public MenuItem MenuItem { get; }

    public SessionLogger? Logger { get; private set; }

    /// <summary>The longest file name the status line shows; longer ones keep their end (the connection and extension), since a Terminal.Gui label word-wraps an over-long line and the second line is simply not shown.</summary>
    private const int _maxShownFileName = 28;

    /// <summary>Appended to the status line while logging: <c>   ● REC capture.jsonl</c>.</summary>
    public string StatusSuffix
    {
        get
        {
            if (Logger?.Path is not { } path)
            {
                return string.Empty;
            }

            var name = Path.GetFileName(path);
            return $"   ● REC {(name.Length > _maxShownFileName ? "…" + name[^(_maxShownFileName - 1)..] : name)}";
        }
    }

    /// <summary>Starts logging <paramref name="session"/> to <paramref name="path"/>, stopping any log already running.</summary>
    public void Start(string path, Session session, CliOptions cliOptions, string parser, string? profileName)
    {
        Stop();
        Logger = SessionLogging.Start(path, session, cliOptions, parser, profileName, "tui");
        MenuItem.Title = StopTitle;
    }

    public void Stop()
    {
        Logger?.Dispose();
        Logger = null;
        MenuItem.Title = StartTitle;
    }

    /// <summary>A live profile switch: the same log continues with the new session.</summary>
    public void Follow(Session session, CliOptions cliOptions, string? profileName) =>
        SessionLogging.Follow(Logger, session, cliOptions, profileName);

    /// <summary>File &gt; Start Logging...'s prompt, defaulting to a timestamped file under the logs directory; <see langword="null"/> if cancelled.</summary>
    public static string? PromptForPath(IApplication app, CliOptions cliOptions, string? profileName) =>
        PlaybackMode.PromptForText(app, "Start Logging", "Log file:", SessionLogging.DefaultLogPath(cliOptions, profileName, DateTimeOffset.Now)) is { } path
            && path.Trim().Length > 0
                ? path.Trim()
                : null;
}

/// <summary>The logging controls a test drives: <see cref="Start"/>/<see cref="Stop"/> are what File &gt; Start/Stop Logging do after the prompt.</summary>
internal sealed record TuiLoggingParts(MenuItem MenuItem, Func<string, bool> Start, Action Stop, Func<SessionLogger?> Logger);
