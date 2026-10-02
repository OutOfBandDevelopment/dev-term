namespace DevTerm.Configuration;

/// <summary>
/// In-progress edits for a new user theme, seeded from any existing theme (a built-in or another
/// user theme) — framework-agnostic so the TUI and WPF theme builder screens share this logic
/// instead of reimplementing color tracking, live-preview building, and saving twice. A built-in is
/// never edited in place (it's code, not a file): <see cref="Save"/> always writes a brand new user
/// theme file, diffed against whichever of <see cref="BuiltInThemes.Light"/>/<see cref="BuiltInThemes.Dark"/>
/// the resulting theme is closer to (<see cref="DevTermTheme.IsDark"/>) — not against <see cref="Seed"/>
/// itself, so seeding from another user theme still produces a file valid against <see cref="ThemeFile.Parse"/>'s
/// <c>basedOn: "light"|"dark"</c> grammar. See docs/design/features/theme-builder.md.
/// </summary>
public sealed class ThemeBuilderState
{
    private readonly Dictionary<ThemeRole, ThemeColor> _overrides = [];

    public ThemeBuilderState(DevTermTheme seed, string name)
    {
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Seed = seed;
        Name = name;
        ChartPalette = seed.ChartPalette;
    }

    /// <summary>The theme this builder started from - every role not yet overridden still reads from here.</summary>
    public DevTermTheme Seed { get; }

    public string Name { get; set; }

    public ChartPaletteVariant ChartPalette { get; set; }

    /// <summary>The roles edited this session, each different from <see cref="Seed"/>.</summary>
    public IReadOnlyDictionary<ThemeRole, ThemeColor> Overrides => _overrides;

    /// <summary><paramref name="role"/>'s current color - an override if set, otherwise <see cref="Seed"/>'s.</summary>
    public ThemeColor this[ThemeRole role] => _overrides.TryGetValue(role, out var color) ? color : Seed[role];

    public bool IsOverridden(ThemeRole role) => _overrides.ContainsKey(role);

    /// <summary>Sets <paramref name="role"/>'s color; clears the override instead if it now matches <see cref="Seed"/> again.</summary>
    public void Set(ThemeRole role, ThemeColor color)
    {
        if (color == Seed[role])
        {
            _overrides.Remove(role);
        }
        else
        {
            _overrides[role] = color;
        }
    }

    public void ResetToSeed(ThemeRole role) => _overrides.Remove(role);

    /// <summary>The theme as edited so far - for live preview (<see cref="ActiveTheme.Preview"/>) and <see cref="ContrastWarnings"/>.</summary>
    public DevTermTheme Build() => Seed.With(Name, _overrides, ChartPalette);

    /// <summary>Contrast problems in the theme as edited so far; empty once every readable pair is fixed.</summary>
    public IReadOnlyList<string> ContrastWarnings() => Build().ContrastWarnings();

    /// <summary>
    /// Writes this as a new user theme file at <paramref name="path"/> - see <see cref="ThemeFile.Save"/>.
    /// Diffs the full built theme (not just <see cref="Overrides"/>) against the canonical light/dark
    /// base, so a role that already differed in <see cref="Seed"/> (e.g. seeded from another user theme)
    /// is still written, not silently dropped.
    /// </summary>
    public string? Save(string path)
    {
        var built = Build();
        var canonicalBase = built.IsDark ? BuiltInThemes.Dark : BuiltInThemes.Light;
        var canonicalBaseName = built.IsDark ? BuiltInThemes.DarkName : BuiltInThemes.LightName;
        var diff = Enum.GetValues<ThemeRole>()
            .Where(role => built[role] != canonicalBase[role])
            .ToDictionary(role => role, role => built[role]);
        return ThemeFile.Save(path, Name, canonicalBaseName, ChartPalette, diff);
    }
}
