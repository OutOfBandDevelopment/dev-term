using System.Text.Json;

namespace DevTerm.Configuration;

/// <summary>
/// App-wide preferences - settings about dev-term itself rather than about a device connection, so
/// they live in <see cref="DevTermUserDataPaths.PreferencesFile"/>, never in a connection profile or
/// <c>appsettings.Local.json</c>. Only the theme so far.
/// </summary>
public sealed class AppPreferences
{
    /// <summary>The selected theme: <c>light</c>, <c>dark</c>, <c>system</c>, or a user theme's name. Null means never chosen (<c>system</c>).</summary>
    public string? Theme { get; set; }
}

/// <summary>
/// Reads and writes <see cref="AppPreferences"/> as JSON. A missing file is default preferences; an
/// unreadable or corrupt one is default preferences plus a <see cref="LoadProblem"/> to report - never
/// an exception, so a bad preferences file can't stop the app from starting.
/// </summary>
public sealed class AppPreferencesStore(string? path = null)
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public string Path { get; } = path ?? DevTermUserDataPaths.PreferencesFile;

    /// <summary>Why the last <see cref="Load"/> fell back to defaults, or null if it didn't.</summary>
    public string? LoadProblem { get; private set; }

    public AppPreferences Load()
    {
        LoadProblem = null;
        if (!File.Exists(Path))
        {
            return new AppPreferences();
        }

        try
        {
            return JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(Path), _jsonOptions) ?? new AppPreferences();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            LoadProblem = $"Could not read preferences from {Path} ({ex.Message}); using defaults.";
            return new AppPreferences();
        }
    }

    /// <summary>Writes <paramref name="preferences"/>, creating the folder if needed. Returns an error message instead of throwing if it can't.</summary>
    public string? Save(AppPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(preferences, _jsonOptions));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Could not save preferences to {Path}: {ex.Message}";
        }
    }
}
