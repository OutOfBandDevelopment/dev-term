namespace DevTerm.Configuration;

/// <summary>
/// The "a missing manifest is a warning, not a hard failure" policy from
/// docs/design/connection-profiles.md, in one place so every front end shows the same message
/// instead of duplicating the resolve-and-format logic three times.
/// </summary>
public static class ManifestNameWarning
{
    /// <summary>
    /// <see langword="null"/> if <see cref="CliOptions.ManifestName"/> is unset or resolves to a
    /// real manifest directory; otherwise the warning text a front end should show the user before
    /// continuing (the connection itself still proceeds with the default text presenters — see
    /// <see cref="DevTermUserDataPaths.ResolveManifestDirectory"/>'s own doc comment).
    /// </summary>
    public static string? For(CliOptions cliOptions)
    {
        if (cliOptions.ManifestName is not { } name)
        {
            return null;
        }

        if (DevTermUserDataPaths.ResolveManifestDirectory(name) is not null)
        {
            return null;
        }

        return $"Warning: device manifest '{name}' was not found under " +
               $"'{DevTermUserDataPaths.UserManifestsDirectory}' or " +
               $"'{DevTermUserDataPaths.AppManifestsDirectory}' — continuing without its " +
               "device-specific commands/UI.";
    }
}
