using System.IO;
using System.Windows;
using DevTerm.Configuration;
using DevTerm.Logging;

namespace DevTerm.Wpf;

/// <summary>
/// Logger mode (File &gt; Start Logging... / Stop Logging, and <c>--log</c>) and File &gt; Open Log
/// for Playback... — kept in their own partial file so the main window's code only calls in at three
/// points: the constructor (<see cref="StartLoggingFromOptions"/>), a profile switch
/// (<see cref="FollowLogging"/>) and closing (<see cref="StopLogging"/>). One log follows whichever
/// tab is active when logging is started or when <see cref="RefreshLoggingUi"/> next runs — not yet
/// per-tab (docs/design/multi-session-ui.md's Open questions defers that to Step 4) — so only one tab
/// at a time can be logged from this window. See docs/specs/wpf-main-window.md and
/// docs/design/session-logging.md.
/// </summary>
public partial class MainWindow
{
    internal const string StartLoggingHeader = "Start _Logging...";
    internal const string StopLoggingHeader = "Stop _Logging";

    private SessionLogger? _logger;

    /// <summary>The running logger, or <see langword="null"/> when not logging.</summary>
    internal SessionLogger? Logger => _logger;

    /// <summary>
    /// Starts logging the active tab's session to <paramref name="path"/> (replacing any file
    /// there), stopping any log already running. A failure is reported in the output list, not
    /// thrown.
    /// </summary>
    /// <returns>Whether logging started.</returns>
    internal bool StartLogging(string path)
    {
        StopLogging(report: false);
        var tab = ActiveWindowTab.Tab;
        try
        {
            _logger = SessionLogging.Start(path, tab.Session, tab.CliOptions, CurrentParser, _profileStore.FindName(tab.CliOptions), "wpf");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            AppendOutput($"Could not start logging to '{path}': {ex.Message}", OutputKind.Error);
            RefreshLoggingUi();
            return false;
        }

        AppendOutput($"Logging to {SessionLogging.DisplayPath(_logger.Path!)}.", OutputKind.Status);
        RefreshLoggingUi();
        return true;
    }

    /// <summary>Stops logging, if it's running.</summary>
    internal void StopLogging(bool report = true)
    {
        if (_logger is null)
        {
            return;
        }

        var path = _logger.Path;
        _logger.Dispose();
        _logger = null;
        if (report)
        {
            AppendOutput($"Stopped logging to {(path is null ? "the log" : SessionLogging.DisplayPath(path))}.", OutputKind.Status);
        }

        RefreshLoggingUi();
    }

    // --log: start straight away (before the Loaded-triggered connect, so the connect is in the log).
    // Takes an explicit tab (rather than reading ActiveWindowTab) because NewSession_Click calls this
    // for a just-added tab that isn't necessarily the active one by the time it runs.
    private void StartLoggingFromOptions(WindowTab tab)
    {
        if (tab.Tab.CliOptions.Log is { Length: > 0 } logOption)
        {
            StartLogging(SessionLogging.ResolveLogPath(logOption, tab.Tab.CliOptions, _profileStore.FindName(tab.Tab.CliOptions), DateTimeOffset.Now));
        }
        else
        {
            RefreshLoggingUi();
        }
    }

    // A live profile switch on `tab`: the same log continues with the new session, provided `tab` is
    // the one currently being logged - takes an explicit tab rather than reading ActiveWindowTab
    // because SwitchProfileAsync can run for a tab that isn't the active one.
    private void FollowLogging(WindowTab tab) => SessionLogging.Follow(_logger, tab.Tab.Session, tab.Tab.CliOptions, _profileStore.FindName(tab.Tab.CliOptions));

    private void RefreshLoggingUi()
    {
        LoggingMenuItem.Header = _logger is null ? StartLoggingHeader : StopLoggingHeader;
        LoggingStatusText.Text = _logger?.Path is { } path ? $"● REC {Path.GetFileName(path)}" : string.Empty;

        // The file name trims to the status bar's width; the tooltip has the whole path.
        LoggingStatusText.ToolTip = _logger?.Path;
    }

    private void LoggingMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_logger is not null)
        {
            StopLogging();
            return;
        }

        var tabOptions = ActiveWindowTab.Tab.CliOptions;
        var suggested = SessionLogging.DefaultLogPath(tabOptions, _profileStore.FindName(tabOptions), DateTimeOffset.Now);
        Directory.CreateDirectory(Path.GetDirectoryName(suggested)!);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Start Logging",
            FileName = Path.GetFileName(suggested),
            InitialDirectory = Path.GetDirectoryName(suggested),
            DefaultExt = SessionLogFormat.FileExtension,
            Filter = LogFileFilter,
        };
        if (dialog.ShowDialog(this) == true)
        {
            StartLogging(dialog.FileName);
        }
    }

    private void OpenLogForPlayback_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open Log for Playback",
            InitialDirectory = Directory.Exists(DevTermUserDataPaths.LogsDirectory) ? DevTermUserDataPaths.LogsDirectory : null,
            Filter = LogFileFilter,
        };
        if (dialog.ShowDialog(this) == true)
        {
            OpenPlayback(dialog.FileName);
        }
    }

    /// <summary>Opens <paramref name="path"/> in a new, non-modal Playback window; a file that can't be read is reported in the output list.</summary>
    /// <returns>The window, or <see langword="null"/> if the log couldn't be opened.</returns>
    internal PlaybackWindow? OpenPlayback(string path, TimeProvider? clock = null, bool show = true)
    {
        Logging.Playback.PlaybackController controller;
        try
        {
            controller = new PlaybackPresenters(ActiveWindowTab.Tab.CliOptions).Open(path, clock);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SessionLogFormatException or ArgumentException or NotSupportedException)
        {
            AppendOutput($"Could not open '{path}' for playback: {ex.Message}", OutputKind.Error);
            return null;
        }

        // Owner only when really showing it: WPF throws setting Owner to a window that has never been
        // shown itself, which is the case for a MainWindow under test.
        var window = new PlaybackWindow(controller);
        if (show)
        {
            window.Owner = this;
            window.Show();
        }

        return window;
    }

    private static string LogFileFilter =>
        $"dev-term session logs (*{SessionLogFormat.FileExtension})|*{SessionLogFormat.FileExtension}|All files (*.*)|*.*";
}
