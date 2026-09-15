using System.IO.Compression;
using DevTerm.UiDefinitions;

namespace DevTerm.DeviceManifests;

/// <summary>
/// Loads a <see cref="DeviceManifest"/> from any of the three shapes docs/design/device-manifests.md
/// describes: a single manifest file, a folder containing <c>device.json</c> at its root, or a
/// <c>.zip</c> of one (extracted to a local folder, then loaded exactly the same way as a folder —
/// no separate zip-specific logic beyond the extraction step itself).
/// </summary>
public static class DeviceManifestLoader
{
    public const string ManifestFileName = "device.json";

    public static DeviceManifest Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (Directory.Exists(path))
        {
            return LoadFromDirectory(path);
        }

        if (string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            var extractDirectory = Path.Combine(Path.GetTempPath(), "devterm-manifests", Path.GetRandomFileName());
            Directory.CreateDirectory(extractDirectory);
            ZipFile.ExtractToDirectory(path, extractDirectory);
            return LoadFromDirectory(extractDirectory);
        }

        if (File.Exists(path))
        {
            return LoadFromFile(path);
        }

        throw new FileNotFoundException($"No device manifest found at '{path}'.", path);
    }

    private static DeviceManifest LoadFromDirectory(string directoryPath)
    {
        var manifestPath = Path.Combine(directoryPath, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException($"Expected a '{ManifestFileName}' inside '{directoryPath}'.", manifestPath);
        }

        return LoadFromFile(manifestPath);
    }

    private static DeviceManifest LoadFromFile(string manifestPath)
    {
        var manifest = DeviceManifestSerializer.FromJson(File.ReadAllText(manifestPath));
        var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;

        if (manifest.Ui is null && manifest.UiFile is not null)
        {
            var uiPath = Path.Combine(baseDirectory, manifest.UiFile);
            if (!File.Exists(uiPath))
            {
                throw new FileNotFoundException($"Manifest references a UI file that doesn't exist: '{manifest.UiFile}'.", uiPath);
            }

            var uiContent = File.ReadAllText(uiPath);
            manifest.Ui = string.Equals(Path.GetExtension(uiPath), ".xml", StringComparison.OrdinalIgnoreCase)
                ? UiDefinitionSerializer.FromXml(uiContent)
                : UiDefinitionSerializer.FromJson(uiContent);
        }

        if (manifest.Inbound?.KaitaiFile is { } kaitaiFile)
        {
            var kaitaiPath = Path.Combine(baseDirectory, kaitaiFile);
            if (!File.Exists(kaitaiPath))
            {
                throw new FileNotFoundException($"Manifest references a Kaitai file that doesn't exist: '{kaitaiFile}'.", kaitaiPath);
            }
        }

        return manifest;
    }
}
