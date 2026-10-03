namespace DevTerm.Configuration;

/// <summary>Picks the newest saved Stream Monitor captures and copies them out (the CLI's <c>--listcaptures</c> / <c>--exportcaptures</c>).</summary>
public static class CaptureExport
{
    /// <summary>The newest <paramref name="count"/> captures that have a saved file, oldest first.</summary>
    public static IReadOnlyList<StreamMonitorCapture> Newest(IEnumerable<StreamMonitorCapture> captures, int count) =>
        count <= 0
            ? []
            : [.. captures.Where(c => c.SavedPath is not null).OrderByDescending(c => c.LocalStartedAt).Take(count).OrderBy(c => c.LocalStartedAt)];

    /// <summary>Copies each capture's file into <paramref name="directory"/> (created if needed); a name already there is not overwritten.</summary>
    /// <returns>The paths written.</returns>
    public static IReadOnlyList<string> CopyTo(IEnumerable<StreamMonitorCapture> captures, string directory)
    {
        Directory.CreateDirectory(directory);
        var written = new List<string>();
        foreach (var capture in captures.Where(c => c.SavedPath is not null))
        {
            var target = Path.Combine(directory, Path.GetFileName(capture.SavedPath!));
            for (var n = 2; File.Exists(target); n++)
            {
                target = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(capture.SavedPath!)}-{n}{Path.GetExtension(capture.SavedPath!)}");
            }

            try
            {
                File.Copy(capture.SavedPath!, target);
                written.Add(target);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Vanished or unreadable since it was listed: skip it.
            }
        }

        return written;
    }
}
