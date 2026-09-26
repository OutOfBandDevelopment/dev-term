namespace DevTerm.Configuration;

/// <summary>
/// Writes a whole text file atomically: to a temporary file in the same folder, then
/// <see cref="File.Move(string, string, bool)"/> over the real path. A crash or power loss
/// mid-write can then never leave a truncated file where <see cref="ConnectionProfileStore"/>,
/// <see cref="AppPreferencesStore"/>, or <see cref="DevTermConfiguration.SaveLocalProfile"/> read
/// from next - the old file stays intact until the new one is fully written. Same pattern as
/// <c>DevTerm.Logging.SessionLog.Save</c> (a different project, writing a stream rather than a
/// short string, so not shared directly). See docs/bugs/fixed/033-non-atomic-writes.md.
/// </summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string contents)
    {
        var temporary = $"{path}.tmp";
        File.WriteAllText(temporary, contents);
        File.Move(temporary, path, overwrite: true);
    }
}
