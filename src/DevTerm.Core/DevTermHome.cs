namespace DevTerm.Core;

/// <summary>
/// The per-user data folder (profiles, manifests, logs, exports, themes, preferences) every front end shares.
/// <c>~/.dev-term</c> by default; the <c>DEVTERM_HOME</c> environment variable points it somewhere else, so a
/// test run or a second install can't touch the real user's files. Read on every call, not cached, so setting the
/// variable after startup (in a test) takes effect.
/// </summary>
public static class DevTermHome
{
    /// <summary>The environment variable that overrides the data folder.</summary>
    public const string EnvironmentVariable = "DEVTERM_HOME";

    /// <summary>The data folder: <c>DEVTERM_HOME</c> when it is set to a non-blank path, otherwise <c>~/.dev-term</c>.</summary>
    public static string Root
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable(EnvironmentVariable);
            return string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dev-term")
                : Path.GetFullPath(configured);
        }
    }
}
