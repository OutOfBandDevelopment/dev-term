namespace DevTerm.Configuration;

/// <summary>
/// How much of a folder of generated files (session logs, exports) to keep. Either limit may be null
/// (no limit); a file is removed when it breaks any limit that is set. A rule with both null keeps everything.
/// </summary>
public sealed class RetentionRule
{
    /// <summary>Delete files last written more than this many days ago.</summary>
    public int? MaxAgeDays { get; set; }

    /// <summary>Keep only this many of the newest files.</summary>
    public int? MaxFiles { get; set; }

    /// <summary>Whether any limit is set.</summary>
    public bool IsActive => MaxAgeDays is > 0 || MaxFiles is > 0;

    /// <summary>
    /// Deletes the files in <paramref name="directory"/> matching <paramref name="searchPattern"/> that break this rule and
    /// returns the paths removed. Never throws: a missing folder, a locked file or a permission problem is skipped, so
    /// housekeeping can never stop the app starting. Subfolders are left alone.
    /// </summary>
    public IReadOnlyList<string> Apply(string directory, string searchPattern, DateTimeOffset now)
    {
        var removed = new List<string>();
        if (!IsActive || !Directory.Exists(directory))
        {
            return removed;
        }

        List<FileInfo> files;
        try
        {
            files = [.. new DirectoryInfo(directory).EnumerateFiles(searchPattern).OrderByDescending(f => f.LastWriteTimeUtc)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return removed;
        }

        for (var i = 0; i < files.Count; i++)
        {
            var tooMany = MaxFiles is > 0 && i >= MaxFiles;
            var tooOld = MaxAgeDays is > 0 && now - files[i].LastWriteTimeUtc > TimeSpan.FromDays(MaxAgeDays.Value);
            if (!tooMany && !tooOld)
            {
                continue;
            }

            try
            {
                files[i].Delete();
                removed.Add(files[i].FullName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // in use or read-only: keep it
            }
        }

        return removed;
    }
}
