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
/// <param name="ConvertedFrom">For an entry that is a converter's output, the display name of what it was converted from (e.g. <c>HP-GL plot</c>); otherwise <see langword="null"/>.</param>
/// <param name="Source">The key the originating session was tracked under (see <see cref="StreamMonitor.Track"/>), so a front end can route a message to that session's tab; <see langword="null"/> if unknown.</param>
public sealed record StreamMonitorCapture(StreamCapture Capture, string DeviceName, DateTimeOffset LocalStartedAt, string? SavedPath, string? SaveError, string? ConvertedFrom = null, object? Source = null)
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
            if (ConvertedFrom is not null)
            {
                return $"{Capture.Kind.DisplayName}, {size} bytes, converted from {ConvertedFrom}.";
            }

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
/// The Stream Monitor's front-end-independent half (docs/design/features/stream-content-detection.md,
/// docs/specs/stream-monitor.md): owns one <see cref="StreamContentWatcher"/> per tracked session,
/// each bound into that session's live pipeline while running, auto-saves every capture as
/// <c>{device}_{yyyyMMdd-HHmmss}.{ext}</c> under that connection's export directory, and keeps one
/// shared list of recent captures for a window to show. Both front ends drive the same instance
/// shape: one per main window that tracks every session (tab) the window holds
/// (<see cref="Track"/>/<see cref="Untrack"/>, re-tracked on a live profile switch), started/stopped
/// explicitly — so monitoring covers all sessions and follows a profile switch instead of silently
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

    // The key SetSession (the single-session shorthand) tracks under.
    private readonly object _singleKey = new();
    private readonly List<Entry> _entries = [];
    private bool _running;

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
                return _running;
            }
        }
    }

    /// <summary>The tracked connections' names, comma-separated (<c>device</c> when none is tracked yet).</summary>
    public string DeviceName
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count == 0 ? "device" : string.Join(", ", _entries.Select(e => e.DeviceName));
            }
        }
    }

    /// <summary>The first tracked connection's export directory (the user's default when none is tracked); see <see cref="ExportDirectories"/> when sessions differ.</summary>
    public string ExportDirectory
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count == 0 ? DevTermUserDataPaths.ExportsDirectory : _entries[0].ExportDirectory;
            }
        }
    }

    /// <summary>The distinct folders captures are saved to — more than one when tracked sessions export elsewhere.</summary>
    public IReadOnlyList<string> ExportDirectories
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count == 0
                    ? [DevTermUserDataPaths.ExportsDirectory]
                    : [.. _entries.Select(e => e.ExportDirectory).Distinct(StringComparer.OrdinalIgnoreCase)];
            }
        }
    }

    /// <summary>How many sessions are being tracked.</summary>
    public int SessionCount
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
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
    /// Single-session shorthand: tracks <paramref name="session"/> under one default key, replacing whatever that key
    /// tracked before. A front end holding several sessions uses <see cref="Track"/> with a key per session instead.
    /// </summary>
    public void SetSession(Session session, string deviceName, string exportDirectory) => Track(_singleKey, session, deviceName, exportDirectory);

    /// <summary>
    /// Adds a session to watch under <paramref name="key"/> (a tab, say), or — when the key is already tracked — moves that
    /// key to <paramref name="session"/> (a live profile switch): any capture still in progress on the old session is
    /// flushed and saved first. While running, the new session is watched immediately.
    /// </summary>
    public void Track(object key, Session session, string deviceName, string exportDirectory)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrEmpty(deviceName);
        ArgumentException.ThrowIfNullOrEmpty(exportDirectory);

        Entry? existing;
        lock (_gate)
        {
            existing = _entries.Find(e => ReferenceEquals(e.Key, key));
        }

        if (existing is not null)
        {
            Detach(existing);
        }

        Entry entry;
        bool attach;
        lock (_gate)
        {
            entry = existing ?? new Entry(key);
            entry.Session = session;
            entry.DeviceName = deviceName;
            entry.ExportDirectory = exportDirectory;
            if (existing is null)
            {
                _entries.Add(entry);
            }

            attach = _running;
        }

        if (attach)
        {
            Attach(entry);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Stops watching the session tracked under <paramref name="key"/> (flushing a capture in progress) and forgets it. A no-op for an unknown key.</summary>
    public void Untrack(object key)
    {
        Entry? entry;
        lock (_gate)
        {
            entry = _entries.Find(e => ReferenceEquals(e.Key, key));
        }

        if (entry is null)
        {
            return;
        }

        Detach(entry);
        lock (_gate)
        {
            _entries.Remove(entry);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Starts watching every tracked session (a no-op if already running). Requires a session tracked first.</summary>
    public void Start()
    {
        Entry[] toAttach;
        lock (_gate)
        {
            if (_entries.Count == 0)
            {
                throw new InvalidOperationException("StreamMonitor.Track (or SetSession) must be called before Start.");
            }

            if (_running)
            {
                return;
            }

            _running = true;
            toAttach = [.. _entries];
        }

        foreach (var entry in toAttach)
        {
            Attach(entry);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Stops watching every session; captures still in progress are flushed and saved. A no-op if not running.</summary>
    public void Stop()
    {
        Entry[] toDetach;
        lock (_gate)
        {
            if (!_running)
            {
                return;
            }

            _running = false;
            toDetach = [.. _entries];
        }

        foreach (var entry in toDetach)
        {
            Detach(entry);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        Entry[] all;
        lock (_gate)
        {
            _running = false;
            all = [.. _entries];
        }

        foreach (var entry in all)
        {
            Detach(entry);
        }
    }

    private void Attach(Entry entry)
    {
        var watcher = new StreamContentWatcher(_watcherOptions, _timeProvider);
        watcher.ContentDetected += OnContentDetected;

        Session session;
        lock (_gate)
        {
            session = entry.Session!;
            entry.Watcher = watcher;
        }

        session.AddPresenter(watcher);
    }

    private void Detach(Entry entry)
    {
        StreamContentWatcher? watcher;
        Session? session;
        lock (_gate)
        {
            watcher = entry.Watcher;
            session = entry.Session;
        }

        if (watcher is null)
        {
            return;
        }

        session?.RemovePresenter(watcher);

        // Still the entry's current watcher while flushing, so a partial capture is saved, not dropped.
        watcher.Flush();

        lock (_gate)
        {
            entry.Watcher = null;
        }

        watcher.ContentDetected -= OnContentDetected;
        watcher.Dispose();
    }

    private void OnContentDetected(object? sender, StreamCapture capture)
    {
        string deviceName;
        string exportDirectory;
        object key;
        lock (_gate)
        {
            var entry = _entries.Find(e => ReferenceEquals(e.Watcher, sender));
            if (entry is null)
            {
                // A late capture from a watcher already replaced/stopped.
                return;
            }

            deviceName = entry.DeviceName;
            exportDirectory = entry.ExportDirectory;
            key = entry.Key;
        }

        var record = Save(capture, deviceName, exportDirectory, key);
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

    private sealed class Entry(object key)
    {
        public object Key { get; } = key;

        public Session? Session { get; set; }

        public StreamContentWatcher? Watcher { get; set; }

        public string DeviceName { get; set; } = "device";

        public string ExportDirectory { get; set; } = DevTermUserDataPaths.ExportsDirectory;
    }

    /// <summary>
    /// Adds a converter's output file to <see cref="Captures"/> (and raises <see cref="CaptureAdded"/>)
    /// so it appears in a window's list next to what it was converted from. Returns
    /// <see langword="null"/>, adding nothing, if the file can't be read.
    /// </summary>
    public StreamMonitorCapture? AddConverted(StreamMonitorCapture source, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(outputPath);

        byte[] data;
        try
        {
            data = File.ReadAllBytes(outputPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var kind = StreamContentKind.ForExtension(Path.GetExtension(outputPath));
        var capture = new StreamCapture(kind, data, source.Capture.StartedAt, StreamCaptureEnd.Complete, WasDeclared: false);
        var record = new StreamMonitorCapture(capture, source.DeviceName, source.LocalStartedAt, outputPath, null, source.Capture.Kind.DisplayName, source.Source);
        lock (_gate)
        {
            _captures.Add(record);
            if (_captures.Count > MaxRetainedCaptures)
            {
                _captures.RemoveAt(0);
            }
        }

        CaptureAdded?.Invoke(this, record);
        return record;
    }

    private StreamMonitorCapture Save(StreamCapture capture, string deviceName, string exportDirectory, object? source)
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
                    return new StreamMonitorCapture(capture, deviceName, localStart, candidate, null, Source: source);
                }
                catch (IOException) when (File.Exists(candidate) && attempt < 1000)
                {
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new StreamMonitorCapture(capture, deviceName, localStart, null, ex.Message, Source: source);
        }
    }
}
