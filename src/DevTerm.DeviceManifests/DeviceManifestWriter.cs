using System.Text.Json;
using System.Text.Json.Serialization;
using DevTerm.UiDefinitions;

namespace DevTerm.DeviceManifests;

/// <summary>
/// Writes a <see cref="DeviceManifest"/> back to disk in the shapes <see cref="DeviceManifestLoader"/>
/// reads — the manifest editor's save. JSON the way the bundled manifests are written by hand
/// (camelCase, indented, unset values left out), which <see cref="DeviceManifestSerializer.FromJson"/>
/// reads back to the same manifest (property names are case-insensitive on read).
/// </summary>
public static class DeviceManifestWriter
{
    private static readonly JsonSerializerOptions _fileOptions = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The manifest as the JSON a manifest file holds.</summary>
    public static string ToFileJson(DeviceManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return JsonSerializer.Serialize(manifest, _fileOptions);
    }

    /// <summary>
    /// Saves <paramref name="manifest"/> to <paramref name="path"/>: a <c>.json</c> path is written as
    /// that single file; anything else is a folder, written as its <c>device.json</c> (created as
    /// needed). A manifest whose UI lives in its own file (<see cref="DeviceManifest.UiFile"/>) keeps it
    /// there — the UI is written to that file beside the manifest and left out of the manifest itself.
    /// A referenced Kaitai file missing at the destination is copied from
    /// <paramref name="sourceDirectory"/> (the folder the manifest was loaded from) when given, so a
    /// manifest saved somewhere new still loads. Returns the manifest file's path.
    /// </summary>
    public static string Save(DeviceManifest manifest, string path, string? sourceDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var file = string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFullPath(path)
            : Path.Combine(Path.GetFullPath(path), DeviceManifestLoader.ManifestFileName);
        var directory = Path.GetDirectoryName(file)!;
        Directory.CreateDirectory(directory);

        var toWrite = manifest;
        if (manifest.UiFile is { Length: > 0 } uiFile && manifest.Ui is { } ui)
        {
            var uiPath = ManifestRelativePath.CombineSafely(directory, uiFile, "UiFile");
            Directory.CreateDirectory(Path.GetDirectoryName(uiPath)!);
            File.WriteAllText(uiPath, string.Equals(Path.GetExtension(uiPath), ".xml", StringComparison.OrdinalIgnoreCase)
                ? UiDefinitionSerializer.ToXml(ui)
                : JsonSerializer.Serialize(ui, _fileOptions));
            toWrite = DeviceManifestSerializer.FromJson(DeviceManifestSerializer.ToJson(manifest));
            toWrite.Ui = null;
        }

        if (manifest.Inbound?.KaitaiFile is { Length: > 0 } kaitai && sourceDirectory is not null)
        {
            var destination = ManifestRelativePath.CombineSafely(directory, kaitai, "KaitaiFile");
            var source = ManifestRelativePath.CombineSafely(sourceDirectory, kaitai, "KaitaiFile");
            if (!File.Exists(destination) && File.Exists(source) && !string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination);
            }
        }

        File.WriteAllText(file, ToFileJson(toWrite) + "\n");
        return file;
    }
}
