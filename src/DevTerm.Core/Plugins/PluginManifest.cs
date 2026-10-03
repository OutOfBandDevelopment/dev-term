using System.Text.Json;

namespace DevTerm.Core.Plugins;

/// <summary>
/// A plugin folder's <c>plugin.json</c>: <c>{ "name": "...", "version": "1.0.0", "contract": 1, "assembly": "My.Plugin.dll" }</c>.
/// <c>contract</c> is the <see cref="PluginLoader.ContractVersion"/> the plugin was built against.
/// </summary>
public sealed class PluginManifest
{
    public const string FileName = "plugin.json";

    private static readonly JsonSerializerOptions _options = new() { PropertyNameCaseInsensitive = true };

    public string Name { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public int Contract { get; set; }

    public string Assembly { get; set; } = string.Empty;

    /// <summary>Reads a manifest; null (with <paramref name="error"/> set) when the file is missing, unreadable or incomplete.</summary>
    public static PluginManifest? TryRead(string path, out string error)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(path), _options);
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Name) || string.IsNullOrWhiteSpace(manifest.Assembly))
            {
                error = $"{FileName} needs at least a name and an assembly.";
                return null;
            }

            error = string.Empty;
            return manifest;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            error = $"can't read {FileName}: {ex.Message}";
            return null;
        }
    }
}
