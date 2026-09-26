using DevTerm.Configuration;

namespace DevTerm.Console;

/// <summary>Text shortening for the TUI's one-line displays, where a Terminal.Gui label silently drops whatever doesn't fit.</summary>
internal static class TuiText
{
    /// <summary>
    /// A path for display: the home folder as "~", then, if still longer than
    /// <paramref name="maxLength"/>, its root, "…", and as much of its end (the file name first) as
    /// fits - the end is the part that tells paths apart.
    /// </summary>
    public static string CompactPath(string path, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(path);
        var shown = SessionLogging.DisplayPath(path);
        if (shown.Length <= maxLength)
        {
            return shown;
        }

        var separator = Path.DirectorySeparatorChar;
        var root = shown.StartsWith('~') ? "~" + separator : Path.GetPathRoot(shown) ?? string.Empty;
        var segments = shown[Math.Min(root.Length, shown.Length)..].Split([separator, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
        {
            return shown;
        }

        var tail = segments[^1];
        for (var i = segments.Length - 2; i >= 0 && root.Length + 2 + segments[i].Length + 1 + tail.Length <= maxLength; i--)
        {
            tail = segments[i] + separator + tail;
        }

        return $"{root}…{separator}{tail}";
    }
}
