using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
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

    /// <summary>
    /// A manifest zip beyond either cap is refused outright rather than extracted — both caps are
    /// far above any real bundled manifest (a handful of files: device.json, a UI file, maybe a
    /// Kaitai file) and exist only to stop a hostile zip from exhausting disk space or inodes.
    /// </summary>
    private const int _maxZipEntryCount = 500;

    private const long _maxZipTotalUncompressedBytes = 100 * 1024 * 1024;

    /// <summary>Loads and validates (see <see cref="DeviceManifestValidator"/>) the manifest at <paramref name="path"/>; an invalid one throws <see cref="DeviceManifestValidationException"/>.</summary>
    public static DeviceManifest Load(string path) => Load(path, validate: true, out _);

    /// <summary>
    /// Loads the manifest at <paramref name="path"/> and reports the manifest file it actually read
    /// (a folder's or an extracted zip's <c>device.json</c>). <paramref name="validate"/> false skips
    /// <see cref="DeviceManifestValidator"/> — the manifest editor opens a broken manifest so it can
    /// be fixed, and validates before it saves instead. A missing referenced UI or Kaitai file still
    /// fails the load either way.
    /// </summary>
    public static DeviceManifest Load(string path, bool validate, out string manifestFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        manifestFile = ResolveManifestFile(path);
        var manifest = LoadFromFile(manifestFile);
        if (validate)
        {
            // The same checks the manifest editor runs before it saves.
            var validation = DeviceManifestValidator.Validate(manifest);
            if (!validation.IsValid)
            {
                throw new DeviceManifestValidationException(validation.Errors);
            }
        }

        return manifest;
    }

    private static string ResolveManifestFile(string path)
    {
        if (Directory.Exists(path))
        {
            return ManifestFileIn(path);
        }

        if (string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            return ManifestFileIn(ExtractZip(path));
        }

        if (File.Exists(path))
        {
            return path;
        }

        throw new FileNotFoundException($"No device manifest found at '{path}'.", path);
    }

    /// <summary>
    /// Extracts the manifest zip at <paramref name="path"/> into a folder reused across repeated
    /// opens of the same zip (identified by its full path, length and last-write time), instead of a
    /// fresh randomly-named one every time — the previous behavior leaked a folder on every load,
    /// since <see cref="Editing.ManifestEditorViewModel"/> keeps the extracted folder around as
    /// <c>SourceDirectory</c> for a later save and never deletes it itself. Rejects the zip outright,
    /// without extracting anything, if it has more entries or more total uncompressed content than
    /// any real manifest bundle would.
    /// </summary>
    private static string ExtractZip(string path)
    {
        var info = new FileInfo(path);
        long totalUncompressedBytes = 0;
        var entryCount = 0;
        using (var archive = ZipFile.OpenRead(path))
        {
            foreach (var entry in archive.Entries)
            {
                entryCount++;
                totalUncompressedBytes += entry.Length;
                if (entryCount > _maxZipEntryCount || totalUncompressedBytes > _maxZipTotalUncompressedBytes)
                {
                    throw new InvalidDataException(
                        $"The manifest zip '{path}' has more than {_maxZipEntryCount} entries or more than {_maxZipTotalUncompressedBytes} bytes uncompressed; refusing to extract it.");
                }
            }
        }

        var key = $"{Path.GetFullPath(path)}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        var keyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        var extractDirectory = Path.Combine(Path.GetTempPath(), "devterm-manifests", keyHash);
        if (Directory.Exists(extractDirectory))
        {
            Directory.Delete(extractDirectory, recursive: true);
        }

        Directory.CreateDirectory(extractDirectory);
        ZipFile.ExtractToDirectory(path, extractDirectory);
        return extractDirectory;
    }

    private static string ManifestFileIn(string directoryPath)
    {
        var manifestPath = Path.Combine(directoryPath, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException($"Expected a '{ManifestFileName}' inside '{directoryPath}'.", manifestPath);
        }

        return manifestPath;
    }

    private static DeviceManifest LoadFromFile(string manifestPath)
    {
        var manifest = DeviceManifestSerializer.FromJson(File.ReadAllText(manifestPath));
        var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;

        if (manifest.Ui is null && manifest.UiFile is not null)
        {
            var uiPath = ManifestRelativePath.CombineSafely(baseDirectory, manifest.UiFile, "UiFile");
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
            var kaitaiPath = ManifestRelativePath.CombineSafely(baseDirectory, kaitaiFile, "KaitaiFile");
            if (!File.Exists(kaitaiPath))
            {
                throw new FileNotFoundException($"Manifest references a Kaitai file that doesn't exist: '{kaitaiFile}'.", kaitaiPath);
            }
        }

        return manifest;
    }
}
