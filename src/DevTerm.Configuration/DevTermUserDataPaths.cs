namespace DevTerm.Configuration;

/// <summary>
/// Per-user/per-install storage locations for named connection profiles and device manifests —
/// shared across every front end regardless of where each is built/run from, unlike the untracked
/// <c>appsettings.Local.json</c> default profile (which deliberately stays next to each app's own
/// output). See docs/design/connection-profiles.md and docs/design/device-manifests.md.
/// </summary>
public static class DevTermUserDataPaths
{
    private static readonly string _userRootDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dev-term");

    /// <summary><c>~/.dev-term/profiles</c> — one JSON file per named connection profile.</summary>
    public static string ProfilesDirectory => Path.Combine(_userRootDirectory, "profiles");

    /// <summary><c>~/.dev-term/manifests</c> — the user's own, personal device manifests.</summary>
    public static string UserManifestsDirectory => Path.Combine(_userRootDirectory, "manifests");

    /// <summary>
    /// <c>~/.dev-term/exports</c> — the default destination for auto-saved captures (e.g. the Stream
    /// Monitor's detected binary/image data, named <c>{device}_{timestamp}.{ext}</c>; see
    /// docs/design/proposals/stream-content-detection.md) when <see cref="CliOptions.ExportDirectory"/>
    /// isn't set to something else.
    /// </summary>
    public static string ExportsDirectory => Path.Combine(_userRootDirectory, "exports");

    /// <summary>
    /// <c>~/.dev-term/logs</c> — where session logs (logger mode) go by default, named
    /// <c>{yyyyMMdd-HHmmss}_{connection}.jsonl</c> (see <see cref="SessionLogging.DefaultLogPath"/>),
    /// and where the Open Log for Playback dialogs start.
    /// </summary>
    public static string LogsDirectory => Path.Combine(_userRootDirectory, "logs");

    /// <summary><c>./manifests</c> (relative to this app's own install/build output) — pre-packaged manifests that ship with dev-term itself.</summary>
    public static string AppManifestsDirectory => Path.Combine(AppContext.BaseDirectory, "manifests");

    /// <summary>
    /// Resolves a device manifest by name to a directory <c>DeviceManifestLoader</c> can load:
    /// checks the user's own manifests first (so a user can override a built-in one by name),
    /// then this app's pre-packaged ones. Returns null if neither exists — this only resolves a
    /// path, it doesn't load or validate the manifest itself. A caller that can't resolve a name
    /// should warn and fall back to default behavior, not treat it as a hard failure — see
    /// docs/design/connection-profiles.md.
    /// </summary>
    public static string? ResolveManifestDirectory(string deviceName)
    {
        var userPath = Path.Combine(UserManifestsDirectory, deviceName);
        if (Directory.Exists(userPath))
        {
            return userPath;
        }

        var appPath = Path.Combine(AppManifestsDirectory, deviceName);
        return Directory.Exists(appPath) ? appPath : null;
    }
}
