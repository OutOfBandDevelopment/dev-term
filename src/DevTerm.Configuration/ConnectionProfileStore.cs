using System.IO.Compression;
using Microsoft.Extensions.Configuration;

namespace DevTerm.Configuration;

/// <summary>How <see cref="ConnectionProfileStore.ImportZip"/> handles a zip entry whose name already matches a saved profile.</summary>
public enum ZipImportConflictResolution
{
    /// <summary>Leave the existing profile alone; don't import this entry.</summary>
    Skip,

    /// <summary>Import this entry under a new, non-conflicting name (e.g. <c>"name (2)"</c>).</summary>
    Rename,

    /// <summary>Overwrite the existing profile with this entry.</summary>
    Replace,
}

/// <summary>Counts from a completed <see cref="ConnectionProfileStore.ImportZip"/> call, for a front end's summary status message.</summary>
public readonly record struct ZipImportResult(int Imported, int Skipped, int Renamed);

/// <summary>
/// Saves/lists/loads named connection profiles — <see cref="CliOptions"/>-shaped JSON files under
/// <see cref="DevTermUserDataPaths.ProfilesDirectory"/> by default, separate from the single
/// untracked <c>appsettings.Local.json</c> default profile each front end's own build output has.
/// See docs/design/connection-profiles.md.
/// </summary>
public sealed class ConnectionProfileStore(string? profilesDirectory = null)
{
    private readonly string _profilesDirectory = profilesDirectory ?? DevTermUserDataPaths.ProfilesDirectory;

    /// <summary>The directory this store saves/lists/loads/deletes profiles in — exposed so a caller can watch it directly (see <see cref="ConnectionEditorViewModel"/>'s file-watcher-backed auto-refresh) without duplicating the same default-directory logic.</summary>
    public string ProfilesDirectory => _profilesDirectory;

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

    /// <summary>
    /// The name of the saved profile that <paramref name="options"/> exactly matches, or
    /// <see langword="null"/> if none does — how a front end tells "running a saved profile" from "running
    /// some other configured connection" without tracking a profile name alongside every
    /// <see cref="CliOptions"/> (which would go stale the moment a field was edited, and has nothing to
    /// say about the untracked default profile a run starts from). "Exactly" is
    /// <see cref="DevTermConfiguration.ToProfileJson"/> equality — the connection-relevant subset, so
    /// run-mode flags and the older-profile presenter/parser fallbacks don't cause a false mismatch.
    /// If several profiles are identical, the first alphabetically wins. An unreadable profile is
    /// skipped rather than failing the title.
    /// </summary>
    public string? FindName(CliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var wanted = DevTermConfiguration.ToProfileJson(options);
        foreach (var name in List())
        {
            try
            {
                if (DevTermConfiguration.ToProfileJson(Load(name)) == wanted)
                {
                    return name;
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
            {
            }
        }

        return null;
    }

    public CliOptions Load(string name)
    {
        var path = GetPath(name);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"No connection profile named '{name}' was found.", path);
        }

        return LoadFromFile(path);
    }

    public void Save(string name, CliOptions options)
    {
        Directory.CreateDirectory(_profilesDirectory);
        File.WriteAllText(GetPath(name), DevTermConfiguration.ToProfileJson(options));
    }

    /// <summary>
    /// Reads a <see cref="CliOptions"/>-shaped JSON file directly by path, rather than by name from
    /// this store's own <see cref="DevTermUserDataPaths.ProfilesDirectory"/> — for importing a
    /// profile exported/shared as a standalone file (see <see cref="ExportToFile"/>), which uses
    /// the exact same shape, so an imported file is also just a normal profile once saved with
    /// <see cref="Save"/>.
    /// </summary>
    public static CliOptions LoadFromFile(string path)
    {
        // The same Microsoft.Extensions.Configuration.Json + Bind() pipeline that loads
        // appsettings.Local.json, not a separate parser — a profile is just a CliOptions-shaped
        // JSON file (see DevTermConfiguration.ToProfileJson).
        var configuration = new ConfigurationBuilder().AddJsonFile(path, optional: false).Build();
        var options = new CliOptions();
        DevTermConfiguration.Bind(configuration, options);
        return options;
    }

    /// <summary>Writes <paramref name="options"/> to <paramref name="path"/> as a standalone JSON file, the same shape <see cref="Save"/> writes under a profile name — for exporting/sharing a profile outside <see cref="DevTermUserDataPaths.ProfilesDirectory"/>.</summary>
    public static void ExportToFile(string path, CliOptions options) =>
        File.WriteAllText(path, DevTermConfiguration.ToProfileJson(options));

    /// <summary>
    /// Writes several saved profiles to a single zip file at <paramref name="zipPath"/>, one
    /// <c>{name}.json</c> entry per name — the exact bytes already on disk, not a re-serialized
    /// round trip, so exporting doesn't reformat a profile someone else hand-edited. For "export
    /// selected"/"export all" from a front end's multi-select profiles list; see
    /// <see cref="ConnectionEditorViewModel.SelectedProfileNames"/>.
    /// </summary>
    public void ExportZip(string zipPath, IEnumerable<string> names)
    {
        if (File.Exists(zipPath))
        {
            // ZipFile.Open(..., Create) throws if the file already exists - Export/Save As already
            // let the user pick an existing filename to overwrite, same as the single-profile
            // ExportToFile above (a plain unconditional File.WriteAllText).
            File.Delete(zipPath);
        }

        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var name in names)
        {
            var path = GetPath(name);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"No connection profile named '{name}' was found.", path);
            }

            archive.CreateEntryFromFile(path, $"{name}.json");
        }
    }

    /// <summary>
    /// Reads every <c>*.json</c> entry from a zip previously written by <see cref="ExportZip"/> (or
    /// hand-built the same way) into this store, asking <paramref name="resolveConflict"/> what to
    /// do whenever an entry's name already matches a saved profile. Left <see langword="null"/>,
    /// every conflict resolves to <see cref="ZipImportConflictResolution.Replace"/> — same "proceed
    /// without asking" convention as <see cref="ConnectionEditorViewModel.ConfirmOverwrite"/> when a
    /// caller doesn't wire a real dialog (e.g. a test).
    /// </summary>
    public ZipImportResult ImportZip(string zipPath, Func<string, ZipImportConflictResolution>? resolveConflict = null)
    {
        Directory.CreateDirectory(_profilesDirectory);
        var existing = new HashSet<string>(List(), StringComparer.OrdinalIgnoreCase);
        var imported = 0;
        var skipped = 0;
        var renamed = 0;

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries.Where(e => e.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            var name = Path.GetFileNameWithoutExtension(entry.Name);
            var targetName = name;

            if (existing.Contains(name))
            {
                var resolution = resolveConflict?.Invoke(name) ?? ZipImportConflictResolution.Replace;
                if (resolution == ZipImportConflictResolution.Skip)
                {
                    skipped++;
                    continue;
                }

                if (resolution == ZipImportConflictResolution.Rename)
                {
                    targetName = MakeUniqueName(name, existing);
                    renamed++;
                }
            }

            using var reader = new StreamReader(entry.Open());
            File.WriteAllText(GetPath(targetName), reader.ReadToEnd());
            existing.Add(targetName);
            imported++;
        }

        return new ZipImportResult(imported, skipped, renamed);
    }

    private static string MakeUniqueName(string name, HashSet<string> existing)
    {
        var suffix = 2;
        string candidate;
        do
        {
            candidate = $"{name} ({suffix})";
            suffix++;
        }
        while (existing.Contains(candidate));

        return candidate;
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
