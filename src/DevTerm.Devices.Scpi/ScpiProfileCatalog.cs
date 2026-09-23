using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DevTerm.Devices.Scpi;

/// <summary>
/// Loads <see cref="ScpiInstrumentProfile"/>s from JSON — the bundled starter set under
/// <c>Profiles/</c> (content files, copied next to the executable) plus any extra <c>*.json</c>
/// files a user drops in a <c>ScpiProfiles/</c> folder beside it. This drop-in-a-file mechanism is
/// the whole point: supporting another instrument is a new file, not a new build. Also exposes a
/// code-constructed <see cref="Generic"/> baseline (per device-control-modules.md's "SCPI
/// baseline... zero-authoring fallback" idea) that exists even with zero profile files present.
/// See docs/design/proposals/scpi-instrument-control.md.
/// </summary>
public static class ScpiProfileCatalog
{
    private const string DropInFolderName = "ScpiProfiles";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Every profile loaded from the bundled <c>Profiles/</c> folder and the drop-in <c>ScpiProfiles/</c> folder, in that order.</summary>
    public static IReadOnlyList<ScpiInstrumentProfile> All { get; } = Load(AppContext.BaseDirectory);

    /// <summary>
    /// Never auto-selected (<see cref="ScpiInstrumentProfile.IdnPattern"/> is null) — the "Generic
    /// (manual)" choice a user picks explicitly when no curated profile fits.
    /// </summary>
    public static ScpiInstrumentProfile Generic { get; } = BuildGenericProfile();

    /// <summary>
    /// Honestly-scoped auto-detect: SCPI has no universal "list supported commands" query, so this
    /// only regex-matches a <c>*IDN?</c> reply against each loaded profile's <see cref="ScpiInstrumentProfile.IdnPattern"/>,
    /// returning the first match or null (callers fall back to <see cref="Generic"/>).
    /// </summary>
    public static ScpiInstrumentProfile? TryMatchByIdn(string idnReply)
    {
        ArgumentNullException.ThrowIfNull(idnReply);

        foreach (var profile in All)
        {
            if (!string.IsNullOrEmpty(profile.IdnPattern) && Regex.IsMatch(idnReply, profile.IdnPattern, RegexOptions.IgnoreCase))
            {
                return profile;
            }
        }

        return null;
    }

    internal static List<ScpiInstrumentProfile> Load(string baseDirectory)
    {
        var profiles = new List<ScpiInstrumentProfile>();
        LoadFrom(Path.Combine(baseDirectory, "Profiles"), profiles);
        LoadFrom(Path.Combine(baseDirectory, DropInFolderName), profiles);
        return profiles;
    }

    private static void LoadFrom(string directory, List<ScpiInstrumentProfile> profiles)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.json").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var json = File.ReadAllText(file);
            var profile = JsonSerializer.Deserialize<ScpiInstrumentProfile>(json, SerializerOptions);
            if (profile is not null)
            {
                profiles.Add(profile);
            }
        }
    }

    private static ScpiInstrumentProfile BuildGenericProfile() => new()
    {
        Name = "Generic (manual)",
        Terminator = "\n",
        Commands =
        [
            new ScpiCommandDefinition { Id = "idn", Label = "Identify", Category = "Common", Template = "*IDN?", IsQuery = true },
            new ScpiCommandDefinition { Id = "rst", Label = "Reset", Category = "Common", Template = "*RST" },
            new ScpiCommandDefinition { Id = "cls", Label = "Clear Status", Category = "Common", Template = "*CLS" },
            new ScpiCommandDefinition { Id = "opc", Label = "Operation Complete?", Category = "Common", Template = "*OPC?", IsQuery = true },
        ],
    };
}
