namespace DevTerm.Configuration;

/// <summary>
/// Whether a string is safe to use as a single path segment under a fixed base directory —
/// a saved connection profile's name (<see cref="ConnectionProfileStore"/>) or a device manifest's
/// name (<see cref="DevTermUserDataPaths.ResolveManifestDirectory"/>). Neither is meant to be a path:
/// this rejects anything that would let one escape its base directory (a rooted name, a <c>..</c>
/// segment), collide with an NTFS alternate data stream (<c>:</c>) or a Windows reserved device name
/// (<c>CON</c>, <c>NUL</c>, ...), or simply not be writable as a file name at all.
/// </summary>
public static class ProfileName
{
    private static readonly HashSet<string> _reservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static bool IsValid(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }

        if (Path.IsPathRooted(name) || name.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        return !_reservedNames.Contains(name);
    }

    /// <exception cref="ArgumentException"><paramref name="name"/> failed <see cref="IsValid"/>.</exception>
    public static void ThrowIfInvalid(string? name)
    {
        if (!IsValid(name))
        {
            throw new ArgumentException($"'{name}' isn't a valid connection profile name.", nameof(name));
        }
    }
}
