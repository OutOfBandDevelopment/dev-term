using DevTerm.Configuration;
using DevTerm.Core.Sessions;
using DevTerm.Logging;
using Terminal.Gui.App;
using Terminal.Gui.Views;

namespace DevTerm.Console;

/// <summary>
/// The TUI main window's logger-mode helpers: the status-line text for a given logger and the File
/// menu item's title constants, kept out of <see cref="TuiMode.BuildWindow"/> so that method only
/// wires them in. Per-tab (docs/design/multi-session-ui.md's Step 4): each
/// <see cref="TuiMode.TuiWindowTab"/>-equivalent owns its own running <see cref="SessionLogger"/>
/// directly rather than this class holding a single, window-level one, so two tabs can log to two
/// different files at once and closing one tab's log doesn't touch another's. See
/// docs/specs/tui-main-screen.md and docs/design/session-logging.md.
/// </summary>
internal static class TuiLogging
{
    public const string StartTitle = "Start _Logging...";
    public const string StopTitle = "Stop _Logging";

    /// <summary>The longest file name the status line shows; longer ones keep their end (the connection and extension), since a Terminal.Gui label word-wraps an over-long line and the second line is simply not shown.</summary>
    private const int _maxShownFileName = 28;

    /// <summary>Appended to the status line while logging: <c>   ● REC capture.jsonl</c>; empty when <paramref name="logger"/> is <see langword="null"/> (not logging, or no tab active).</summary>
    public static string StatusSuffixFor(SessionLogger? logger)
    {
        if (logger?.Path is not { } path)
        {
            return string.Empty;
        }

        var name = Path.GetFileName(path);
        return $"   ● REC {(name.Length > _maxShownFileName ? "…" + name[^(_maxShownFileName - 1)..] : name)}";
    }

    /// <summary>File &gt; Start Logging...'s prompt, defaulting to a timestamped file under the logs directory; <see langword="null"/> if cancelled.</summary>
    public static string? PromptForPath(IApplication app, CliOptions cliOptions, string? profileName) =>
        PlaybackMode.PromptForText(app, "Start Logging", "Log file:", SessionLogging.DefaultLogPath(cliOptions, profileName, DateTimeOffset.Now)) is { } path
            && path.Trim().Length > 0
                ? path.Trim()
                : null;
}

/// <summary>The logging controls a test drives: <see cref="Start"/>/<see cref="Stop"/> are what File &gt; Start/Stop Logging do after the prompt, acting on whichever tab is active.</summary>
internal sealed record TuiLoggingParts(MenuItem MenuItem, Func<string, bool> Start, Action Stop, Func<SessionLogger?> Logger);
