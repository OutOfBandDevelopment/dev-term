using System.Text.Json;

namespace DevTerm.Configuration;

/// <summary>
/// The app-wide list of Stream Monitor converter tools, kept in <see cref="DevTermUserDataPaths.ConverterToolsFile"/>
/// so one registration serves every device and profile. A tool a profile still carries from before the list became
/// global is kept (see <see cref="Merge"/>), so existing profiles keep working.
/// See docs/design/features/stream-converter-tools.md.
/// </summary>
public sealed class ConverterToolsStore
{
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    private readonly string _path;

    public ConverterToolsStore(string? path = null) => _path = path ?? DevTermUserDataPaths.ConverterToolsFile;

    /// <summary>The saved tools in order. A missing or unreadable file is an empty list, never an error.</summary>
    public IReadOnlyList<StreamConvertToolOptions> Load()
    {
        try
        {
            return File.Exists(_path)
                ? [.. (JsonSerializer.Deserialize<List<StreamConvertToolOptions>>(File.ReadAllText(_path)) ?? []).Select(StreamConvertToolOptions.Clone)]
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    /// <summary>Replaces the saved list, creating the folder if needed.</summary>
    public void Save(IEnumerable<StreamConvertToolOptions> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(tools.Select(StreamConvertToolOptions.Clone).ToList(), _json));
    }

    /// <summary>The global tools, then any of <paramref name="profileTools"/> whose name isn't already taken (global wins).</summary>
    public static List<StreamConvertToolOptions> Merge(IEnumerable<StreamConvertToolOptions>? globalTools, IEnumerable<StreamConvertToolOptions>? profileTools)
    {
        var merged = new List<StreamConvertToolOptions>();
        foreach (var tool in (globalTools ?? []).Concat(profileTools ?? []))
        {
            if (!merged.Any(t => string.Equals(t.Name, tool.Name, StringComparison.OrdinalIgnoreCase)))
            {
                merged.Add(StreamConvertToolOptions.Clone(tool));
            }
        }

        return merged;
    }
}
