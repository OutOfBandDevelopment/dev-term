using Microsoft.Extensions.Configuration;

namespace DevTerm.Configuration;

/// <summary>
/// Saves/lists/loads named connection profiles — <see cref="CliOptions"/>-shaped JSON files under
/// <see cref="DevTermUserDataPaths.ProfilesDirectory"/> by default, separate from the single
/// untracked <c>appsettings.Local.json</c> default profile each front end's own build output has.
/// See docs/design/connection-profiles.md.
/// </summary>
public sealed class ConnectionProfileStore(string? profilesDirectory = null)
{
    private readonly string _profilesDirectory = profilesDirectory ?? DevTermUserDataPaths.ProfilesDirectory;

    /// <summary>Names of every saved profile, alphabetical. Empty if the profiles directory doesn't exist yet.</summary>
    public IReadOnlyList<string> List()
    {
        if (!Directory.Exists(_profilesDirectory))
        {
            return [];
        }

        return [.. Directory.EnumerateFiles(_profilesDirectory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)];
    }

    public CliOptions Load(string name)
    {
        var path = GetPath(name);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"No connection profile named '{name}' was found.", path);
        }

        // The same Microsoft.Extensions.Configuration.Json + Bind() pipeline that loads
        // appsettings.Local.json, not a separate parser — a profile is just a CliOptions-shaped
        // JSON file (see DevTermConfiguration.ToProfileJson).
        var configuration = new ConfigurationBuilder().AddJsonFile(path, optional: false).Build();
        var options = new CliOptions();
        configuration.Bind(options);
        return options;
    }

    public void Save(string name, CliOptions options)
    {
        Directory.CreateDirectory(_profilesDirectory);
        File.WriteAllText(GetPath(name), DevTermConfiguration.ToProfileJson(options));
    }

    /// <returns><see langword="true"/> if a profile with that name existed and was deleted; <see langword="false"/> if there was nothing to delete.</returns>
    public bool Delete(string name)
    {
        var path = GetPath(name);
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    private string GetPath(string name) => Path.Combine(_profilesDirectory, $"{name}.json");
}
