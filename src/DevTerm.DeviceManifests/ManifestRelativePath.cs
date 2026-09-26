namespace DevTerm.DeviceManifests;

/// <summary>
/// Guards a manifest's own <see cref="DeviceManifest.UiFile"/>/<see cref="InboundProtocol.KaitaiFile"/>
/// path against escaping the manifest's folder — a rooted path (<c>C:\...</c>) or a <c>..\</c> segment
/// would otherwise let <see cref="DeviceManifestWriter.Save"/> write, and <see cref="DeviceManifestLoader"/>
/// read, anywhere on disk. See docs/bugs/fixed/010-manifest-path-traversal.md.
/// </summary>
public static class ManifestRelativePath
{
    /// <summary>True if <paramref name="relativePath"/> is safe to combine with a manifest's own folder:
    /// not rooted, and containing no ".." segment. A missing/blank path (nothing to combine) is safe.</summary>
    public static bool IsSafe(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return true;
        }

        if (Path.IsPathRooted(relativePath))
        {
            return false;
        }

        return relativePath.Split('/', '\\').All(segment => segment != "..");
    }

    /// <summary>
    /// Combines <paramref name="baseDirectory"/> with <paramref name="relativePath"/>, throwing
    /// <see cref="InvalidOperationException"/> if that isn't safe (see <see cref="IsSafe"/>) or the
    /// resolved path still falls outside <paramref name="baseDirectory"/>. <paramref name="what"/> names
    /// the manifest field being combined, for the error message.
    /// </summary>
    public static string CombineSafely(string baseDirectory, string relativePath, string what)
    {
        if (!IsSafe(relativePath))
        {
            throw new InvalidOperationException($"{what} '{relativePath}' must be a path inside the manifest's own folder.");
        }

        var combined = Path.GetFullPath(Path.Combine(baseDirectory, relativePath));
        var normalizedBase = Path.GetFullPath(baseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{what} '{relativePath}' must be a path inside the manifest's own folder.");
        }

        return combined;
    }
}
