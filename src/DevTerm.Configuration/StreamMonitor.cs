using System.Globalization;
using System.Text;
using DevTerm.Core.Sessions;
using DevTerm.Core.StreamContent;

namespace DevTerm.Configuration;

/// <summary>One capture the <see cref="StreamMonitor"/> took, and where (or whether) it was saved.</summary>
/// <param name="Capture">The detected content itself.</param>
/// <param name="DeviceName">The connection it came from, as used in its file name.</param>
/// <param name="LocalStartedAt">When it started, in local time — the timestamp its file name uses.</param>
/// <param name="SavedPath">The file it was auto-saved to, or <see langword="null"/> if saving failed.</param>
/// <param name="SaveError">Why saving failed, when it did.</param>
public sealed record StreamMonitorCapture(StreamCapture Capture, string DeviceName, DateTimeOffset LocalStartedAt, string? SavedPath, string? SaveError)
{
    /// <summary>The front ends' one-line status message for this capture, e.g. <c>Captured 4,213 bytes of BMP image to C:\…\hp34401a_20260923-143512.bmp.</c></summary>
    public string Describe()
    {
        var size = Capture.Data.Length.ToString("N0", CultureInfo.InvariantCulture);
        var what = $"{size} bytes of {Capture.Kind.DisplayName}{EndNote(Capture.EndReason)}";
        return SavedPath is not null
            ? $"Captured {what} to {SavedPath}."
            : $"Captured {what}, but could not save it: {SaveError}";
    }

    /// <summary>What the capture is, for a detail line: e.g. <c>BMP image, 70 bytes, complete (declared by the command).</c></summary>
    public string Summary
    {
        get
        {
            var size = Capture.Data.Length.ToString("N0", CultureInfo.InvariantCulture);
            var declared = Capture.WasDeclared ? " (declared by the command)" : string.Empty;
            return $"{Capture.Kind.DisplayName}, {size} bytes, {EndLabel}{declared}.";
        }
    }

    /// <summary>A short, fixed-width-friendly description of how the capture ended (for a capture list).</summary>
    public string EndLabel => Capture.EndReason switch
    {
        StreamCaptureEnd.Complete => "complete",
        StreamCaptureEnd.IdleTimeout => "went quiet",
        StreamCaptureEnd.SizeLimit => "size limit",
        _ => "stopped",
    };

    private static string EndNote(StreamCaptureEnd reason) => reason switch
    {
        StreamCaptureEnd.IdleTimeout => " (ended when the device went quiet)",
        StreamCaptureEnd.SizeLimit => " (cut off at the size limit - probably incomplete)",
        StreamCaptureEnd.Flushed => " (monitoring stopped mid-capture - probably incomplete)",
        _ => string.Empty,
    };
}

/// <summary>
/// The Stream Monitor's front-end-independent half (docs/design/proposals/stream-content-detection.md,
/// docs/specs/stream-monitor.md): owns one <see cref="StreamContentWatcher"/> bound into the
/// current session's live pipeline while running, auto-saves every capture as
/// <c>{device}_{yyyyMMdd-HHmmss}.{ext}</c> under the connection's export directory, and keeps a
/// list of recent captures for a window to show. Both front ends drive the same instance shape:
/// one per main window, told about every session the window switches to (<see cref="SetSession"/>),
/// started/stopped explicitly — so monitoring follows a live profile switch instead of silently
/// staying bound to a disposed session.
/// </summary>
/// <remarks>
/// Bound through <see cref="Session.AddPresenter"/> rather than selected as a <c>--presenter</c>:
/// the watcher emits no text of its own, so as a display presenter it would do nothing visible;
/// binding it in place means monitoring can be turned on and off mid-connection without
/// reconnecting, and a fresh watcher per session keeps its state from leaking across connections.
/// <see cref="CaptureAdded"/> is raised on a background thread (the session's read loop, or the
/// watcher's idle timer) — a UI subscriber marshals to its own thread.
/// </remarks>
public sealed class StreamMonitor : IDisposable
{
    /// <summary>How many captures <see cref="Captures"/> keeps in memory (oldest dropped first). The saved files themselves are never deleted.</summary>
    public const int MaxRetainedCaptures = 100;

    private readonly TimeProvider _timeProvider;
    private readonly StreamContentWatcherOptions? _watcherOptions;
    private readonly Lock _gate = new();
    private readonly List<StreamMonitorCapture> _captures = [];

    private Session? _session;
    private StreamContentWatcher? _watcher;
    private string _deviceName = "device";
    private string _exportDirectory = DevTermUserDataPaths.ExportsDirectory;

    public StreamMonitor(TimeProvider? timeProvider = null, StreamContentWatcherOptions? watcherOptions = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _watcherOptions = watcherOptions;
    }

    /// <summary>Raised (on a background thread) after each capture has been saved, or failed to save.</summary>
    public event EventHandler<StreamMonitorCapture>? CaptureAdded;

    /// <summary>Raised when <see cref="IsRunning"/>, the device name or the export directory changes.</summary>
    public event EventHandler? StateChanged;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _watcher is not null;
            }
        }
    }

    public string DeviceName
    {
        get
        {
            lock (_gate)
            {
                return _deviceName;
            }
        }
    }

    public string ExportDirectory
    {
        get
        {
            lock (_gate)
            {
                return _exportDirectory;
            }
        }
    }

    /// <summary>The most recent captures, oldest first.</summary>
    public IReadOnlyList<StreamMonitorCapture> Captures
    {
        get
        {
            lock (_gate)
            {
                return [.. _captures];
            }
        }
    }

    /// <summary>
    /// The resolved device name used in capture file names: the saved profile's name when
    /// <paramref name="cliOptions"/> is exactly one, otherwise its <c>tcp://…</c>/<c>serial://…</c>
    /// connection definition — the same subject a main window's title shows.
    /// </summary>
    public static string DeviceNameFor(CliOptions cliOptions, ConnectionProfileStore profileStore)
    {
        ArgumentNullException.ThrowIfNull(cliOptions);
        ArgumentNullException.ThrowIfNull(profileStore);
        return profileStore.FindName(cliOptions) ?? ConnectionDescription.Definition(cliOptions);
    }

    /// <summary>
    /// <paramref name="path"/> for display, with the user's home folder shortened to <c>~</c> — so
    /// the default export directory reads <c>~\.dev-term\exports</c> instead of a long
    /// <c>C:\Users\…</c> path that doesn't fit a terminal line.
    /// </summary>
    public static string DisplayPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (home.Length == 0 || !path.StartsWith(home, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        var rest = path[home.Length..];
        return rest.Length == 0 ? "~" : rest[0] is '\\' or '/' ? "~" + rest : path;
    }

    /// <summary><c>{device}_{yyyyMMdd-HHmmss}.{extension}</c>, with <paramref name="deviceName"/> made file-name-safe (see <see cref="SanitizeForFileName"/>).</summary>
    public static string FileNameFor(string deviceName, DateTimeOffset localTimestamp, string extension) =>
        $"{SanitizeForFileName(deviceName)}_{localTimestamp.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.{extension}";

    /// <summary>
    /// Keeps letters, digits, <c>-</c>, <c>_</c> and <c>.</c>; everything else (spaces, <c>:</c>,
    /// <c>/</c>, …) becomes a single <c>_</c> — e.g. <c>tcp://192.168.0.5:5025</c> →
    /// <c>tcp_192.168.0.5_5025</c>, <c>HP 34401A</c> → <c>HP_34401A</c>.
    /// </summary>
    public static string SanitizeForFileName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var builder = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            var keep = char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.';
            if (keep)
            {
                builder.Append(ch);
            }
            else if (builder.Length > 0 && builder[^1] != '_')
            {
                builder.Append('_');
            }
        }

        var sanitized = builder.ToString().Trim('_', '.');
        return sanitized.Length > 0 ? sanitized : "device";
    }

    /// <summary>
    /// Points the monitor at <paramref name="session"/> — call it with the main window's session
    /// when the monitor is created and again after every profile switch. While running, the
    /// watcher moves to the new session (any capture still in progress on the old one is flushed
    /// and saved first).
    /// </summary>
    public void SetSession(Session session, string deviceName, string exportDirectory)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrEmpty(deviceName);
        ArgumentException.ThrowIfNullOrEmpty(exportDirectory);

        bool wasRunning;
        lock (_gate)
        {
            wasRunning = _watcher is not null;
        }

        if (wasRunning)
        {
            Detach();
        }

        lock (_gate)
        {
            _session = session;
            _deviceName = deviceName;
            _exportDirectory = exportDirectory;
        }

        if (wasRunning)
        {
            Attach();
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Starts watching the current session (a no-op if already running). Requires <see cref="SetSession"/> first.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_session is null)
            {
                throw new InvalidOperationException("StreamMonitor.SetSession must be called before Start.");
            }

            if (_watcher is not null)
            {
                return;
            }
        }

        Attach();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Stops watching; a capture still in progress is flushed and saved. A no-op if not running.</summary>
    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        Detach();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => Detach();

    private void Attach()
    {
        var watcher = new StreamContentWatcher(_watcherOptions, _timeProvider);
        watcher.ContentDetected += OnContentDetected;

        Session session;
        lock (_gate)
        {
            session = _session!;
            _watcher = watcher;
        }

        session.AddPresenter(watcher);
    }

    private void Detach()
    {
        StreamContentWatcher? watcher;
        Session? session;
        lock (_gate)
        {
            watcher = _watcher;
            session = _session;
        }

        if (watcher is null)
        {
            return;
        }

        session?.RemovePresenter(watcher);

        // Still the current watcher while flushing, so a partial capture is saved, not dropped.
        watcher.Flush();

        lock (_gate)
        {
            _watcher = null;
        }

        watcher.ContentDetected -= OnContentDetected;
        watcher.Dispose();
    }

    private void OnContentDetected(object? sender, StreamCapture capture)
    {
        string deviceName;
        string exportDirectory;
        lock (_gate)
        {
            if (!ReferenceEquals(sender, _watcher))
            {
                // A late capture from a watcher already replaced/stopped.
                return;
            }

            deviceName = _deviceName;
            exportDirectory = _exportDirectory;
        }

        var record = Save(capture, deviceName, exportDirectory);
        lock (_gate)
        {
            _captures.Add(record);
            if (_captures.Count > MaxRetainedCaptures)
            {
                _captures.RemoveAt(0);
            }
        }

        CaptureAdded?.Invoke(this, record);
    }

    private StreamMonitorCapture Save(StreamCapture capture, string deviceName, string exportDirectory)
    {
        var localStart = TimeZoneInfo.ConvertTime(capture.StartedAt, _timeProvider.LocalTimeZone);
        try
        {
            Directory.CreateDirectory(exportDirectory);
            var fileName = FileNameFor(deviceName, localStart, capture.Kind.Extension);
            var stem = Path.GetFileNameWithoutExtension(fileName);

            // Two captures within the same second get -2, -3, ... rather than overwriting each other.
            for (var attempt = 1; ; attempt++)
            {
                var candidate = Path.Combine(exportDirectory, attempt == 1 ? fileName : $"{stem}-{attempt}.{capture.Kind.Extension}");
                try
                {
                    using var file = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    file.Write(capture.Data);
                    return new StreamMonitorCapture(capture, deviceName, localStart, candidate, null);
                }
                catch (IOException) when (File.Exists(candidate) && attempt < 1000)
                {
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new StreamMonitorCapture(capture, deviceName, localStart, null, ex.Message);
        }
    }
}
