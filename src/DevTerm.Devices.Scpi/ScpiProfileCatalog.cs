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
/// See docs/design/features/scpi-instrument-control.md.
/// </summary>
public static class ScpiProfileCatalog
{
    private const string _dropInFolderName = "ScpiProfiles";

    /// <summary>
    /// The synthetic "pick a profile at runtime" choice both front ends' SCPI instrument pickers
    /// offer alongside <see cref="Generic"/>'s own name and the real, loaded profile names — centralized
    /// here (rather than independently redeclared per front end) so a saved <c>CliOptions.ScpiProfile</c>
    /// choice and the live picker dialog always agree on the exact same literal.
    /// </summary>
    public const string AutoDetectChoiceName = "Auto-detect (*IDN?)";

    private static readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly List<ScpiInstrumentProfile> _all;
    private static readonly List<string> _loadErrors;

    static ScpiProfileCatalog()
    {
        _all = Load(AppContext.BaseDirectory, out _loadErrors);
    }

    /// <summary>
    /// Every profile loaded from the bundled <c>Profiles/</c> folder, the drop-in <c>ScpiProfiles/</c>
    /// folder next to the executable, and a per-user <c>~/.dev-term/scpi-profiles</c> folder, in that
    /// order — mirrors <c>DevTermUserDataPaths</c>'s <c>~/.dev-term/&lt;subfolder&gt;</c> convention for
    /// device manifests/connection profiles, computed locally here rather than shared from
    /// <c>DevTerm.Configuration</c> (which references this project, not the other way around).
    /// </summary>
    public static IReadOnlyList<ScpiInstrumentProfile> All => _all;

    /// <summary>
    /// One entry per profile file that failed to load (bad JSON, unreadable file), skipped rather
    /// than left to fail the whole catalog — see
    /// docs/bugs/fixed/023-scpi-profile-catalog-bad-file.md. A picker/settings screen can surface
    /// this; nothing does yet.
    /// </summary>
    public static IReadOnlyList<string> LoadErrors => _loadErrors;

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

    internal static List<ScpiInstrumentProfile> Load(string baseDirectory) => Load(baseDirectory, out _);

    /// <summary>
    /// Same as <see cref="Load(string)"/>, but also reports which files (if any) failed to load
    /// instead of throwing — see <see cref="LoadFrom"/>.
    /// </summary>
    internal static List<ScpiInstrumentProfile> Load(string baseDirectory, out List<string> errors)
    {
        var profiles = new List<ScpiInstrumentProfile>();
        errors = [];
        LoadFrom(Path.Combine(baseDirectory, "Profiles"), profiles, errors);
        LoadFrom(Path.Combine(baseDirectory, _dropInFolderName), profiles, errors);
        LoadFrom(UserProfilesDirectory, profiles, errors);
        return profiles;
    }

    /// <summary>
    /// <c>~/.dev-term/scpi-profiles</c> — a per-user drop-in folder alongside the app-folder one
    /// above, for a profile a user wants available regardless of which build/install of dev-term
    /// they're running (same rationale as <c>DevTermUserDataPaths.ProfilesDirectory</c>/
    /// <c>UserManifestsDirectory</c>).
    /// </summary>
    internal static string UserProfilesDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dev-term", "scpi-profiles");

    private static void LoadFrom(string directory, List<ScpiInstrumentProfile> profiles, List<string> errors)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.json").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var json = File.ReadAllText(file);
                var profile = JsonSerializer.Deserialize<ScpiInstrumentProfile>(json, _serializerOptions);
                if (profile is not null)
                {
                    profiles.Add(profile);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
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
