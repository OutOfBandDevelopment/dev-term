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

    /// <summary>Optional load order (lower first, then by folder name); decides the order presenters and menu entries are listed in.</summary>
    public int Order { get; set; }

    /// <summary>The .NET assembly of an in-process plugin. Empty for an out-of-process one (see <see cref="Process"/>).</summary>
    public string Assembly { get; set; } = string.Empty;

    /// <summary>Set for an out-of-process plugin: the program to run. It only runs after the user approves it (<see cref="PluginTrust"/>).</summary>
    public PluginProcess? Process { get; set; }

    /// <summary>Reads a manifest; null (with <paramref name="error"/> set) when the file is missing, unreadable or incomplete.</summary>
    public static PluginManifest? TryRead(string path, out string error)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(path), _options);
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Name)
                || (string.IsNullOrWhiteSpace(manifest.Assembly) && string.IsNullOrWhiteSpace(manifest.Process?.Command)))
            {
                error = $"{FileName} needs at least a name and an assembly (or a process command).";
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

/// <summary>An out-of-process plugin's program: <c>{ "command": "python", "arguments": ["{folder}/shout.py"], "replyTimeoutMs": 2000 }</c>. <c>{folder}</c> in either is replaced by the plugin's folder.</summary>
public sealed class PluginProcess
{
    public string Command { get; set; } = string.Empty;

    public List<string> Arguments { get; set; } = [];

    public int ReplyTimeoutMs { get; set; } = 2000;
}
