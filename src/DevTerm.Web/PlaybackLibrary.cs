using DevTerm.Logging;

namespace DevTerm.Web;

/// <summary>The folder of session logs the Playback page may open. A name resolves only to a file directly inside it, so a request can never read an arbitrary path.</summary>
public sealed class PlaybackLibrary(string directory)
{
    public string Directory { get; } = directory;

    /// <summary>Log file names, newest first.</summary>
    public IReadOnlyList<string> List() =>
        System.IO.Directory.Exists(Directory)
            ? [.. new DirectoryInfo(Directory).EnumerateFiles("*" + SessionLogFormat.FileExtension).OrderByDescending(f => f.LastWriteTimeUtc).Select(f => f.Name)]
            : [];

    /// <summary>The full path of <paramref name="name"/> if it is a log in the folder; otherwise null.</summary>
    public string? Resolve(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name))
        {
            return null;
        }

        var path = Path.Combine(Directory, name);
        return File.Exists(path) ? path : null;
    }
}
