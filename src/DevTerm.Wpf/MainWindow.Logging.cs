using System.IO;
using System.Windows;
using DevTerm.Configuration;
using DevTerm.Logging;

namespace DevTerm.Wpf;

/// <summary>
/// Logger mode (File &gt; Start Logging... / Stop Logging, and <c>--log</c>) and File &gt; Open Log
/// for Playback... — kept in their own partial file so the main window's code only calls in at three
/// points: the constructor (<see cref="StartLoggingFromOptions"/>), a profile switch
/// (<see cref="FollowLogging"/>) and closing/tab-close (<see cref="StopLogging(WindowTab, bool)"/>).
/// Per-tab (docs/design/multi-session-ui.md's Step 4): each <see cref="WindowTab"/> owns its own
/// <see cref="WindowTab.Logger"/>, so two tabs can be logged to two different files at once, and
/// closing one tab's log doesn't touch another's. See docs/specs/wpf-main-window.md and
/// docs/design/session-logging.md.
/// </summary>
public partial class MainWindow
{
    internal const string StartLoggingHeader = "Start _Logging...";
    internal const string StopLoggingHeader = "Stop _Logging";

    /// <summary>The active tab's running logger, or <see langword="null"/> when not logging (or no tab is active).</summary>
    internal SessionLogger? Logger => ActiveWindowTabOrNull?.Logger;

    /// <summary>
    /// Starts logging the active tab's session to <paramref name="path"/> (replacing any file
    /// there), stopping any log already running on that tab. A failure is reported in the output
    /// list, not thrown. No-ops (returns <see langword="false"/>) with no tab active.
    /// </summary>
    /// <returns>Whether logging started.</returns>
    internal bool StartLogging(string path) => ActiveWindowTabOrNull is { } tab && StartLogging(tab, path);

    private bool StartLogging(WindowTab tab, string path)
    {
        StopLogging(tab, report: false);
        try
        {
            tab.Logger = SessionLogging.Start(path, tab.Tab.Session, tab.Tab.CliOptions, CurrentParser, _profileStore.FindName(tab.Tab.CliOptions), "wpf");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            AppendOutput(tab, $"Could not start logging to '{path}': {ex.Message}", OutputKind.Error);
            RefreshLoggingUiForActiveTab();
            return false;
        }

        AppendOutput(tab, $"Logging to {SessionLogging.DisplayPath(tab.Logger.Path!)}.", OutputKind.Status);
        RefreshLoggingUiForActiveTab();
        return true;
    }

    /// <summary>Stops logging on the active tab, if it's running. A no-op with no tab active.</summary>
    internal void StopLogging(bool report = true)
    {
        if (ActiveWindowTabOrNull is { } tab)
        {
            StopLogging(tab, report);
        }
    }

    /// <summary>Stops logging on <paramref name="tab"/> specifically, if it's running — used when closing that tab (or the window) so another tab's log is never touched.</summary>
    private void StopLogging(WindowTab tab, bool report = true)
    {
        if (tab.Logger is null)
        {
            return;
        }

        var path = tab.Logger.Path;
        tab.Logger.Dispose();
        tab.Logger = null;
        if (report)
        {
            AppendOutput(tab, $"Stopped logging to {(path is null ? "the log" : SessionLogging.DisplayPath(path))}.", OutputKind.Status);
        }

        RefreshLoggingUiForActiveTab();
    }

    // --log: start straight away (before the Loaded-triggered connect, so the connect is in the log).
    // Takes an explicit tab (rather than reading ActiveWindowTab) because NewSession_Click calls this
    // for a just-added tab that isn't necessarily the active one by the time it runs.
    private void StartLoggingFromOptions(WindowTab tab)
    {
        if (tab.Tab.CliOptions.Log is { Length: > 0 } logOption)
        {
            StartLogging(tab, SessionLogging.ResolveLogPath(logOption, tab.Tab.CliOptions, _profileStore.FindName(tab.Tab.CliOptions), DateTimeOffset.Now));
        }
        else
        {
            RefreshLoggingUiForActiveTab();
        }
    }

    // A live profile switch on `tab`: the same log continues with the new session, provided `tab` is
    // the one currently being logged - takes an explicit tab rather than reading ActiveWindowTab
    // because SwitchProfileAsync can run for a tab that isn't the active one.
    private void FollowLogging(WindowTab tab) => SessionLogging.Follow(tab.Logger, tab.Tab.Session, tab.Tab.CliOptions, _profileStore.FindName(tab.Tab.CliOptions));

    /// <summary>
    /// Reflects the active tab's own logging state into the menu item and status bar. Safe with no
    /// tab active (zero tabs): disables the menu item and clears the status text.
    /// </summary>
    private void RefreshLoggingUiForActiveTab()
    {
        var logger = ActiveWindowTabOrNull?.Logger;
        LoggingMenuItem.IsEnabled = ActiveWindowTabOrNull is not null;
        LoggingMenuItem.Header = logger is null ? StartLoggingHeader : StopLoggingHeader;
        LoggingStatusText.Text = logger?.Path is { } path ? $"● REC {Path.GetFileName(path)}" : string.Empty;

        // The file name trims to the status bar's width; the tooltip has the whole path.
        LoggingStatusText.ToolTip = logger?.Path;
    }

    private void LoggingMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveWindowTabOrNull is not { } activeTab)
        {
            return;
        }

        if (activeTab.Logger is not null)
        {
            StopLogging();
            return;
        }

        var tabOptions = activeTab.Tab.CliOptions;
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
            controller = new PlaybackPresenters(ActiveWindowTabOrNull?.Tab.CliOptions ?? _lastCliOptions).Open(path, clock);
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
