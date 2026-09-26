using Microsoft.Extensions.Configuration;

namespace DevTerm.Configuration;

/// <summary>
/// The process-wide current theme both front ends draw with, and the one place a theme gets
/// selected - at startup (<see cref="Initialize"/>) or live from View &gt; Theme (<see cref="Select"/>),
/// which raises <see cref="Changed"/> so every open window re-applies it without a restart.
/// </summary>
/// <remarks>
/// Until <see cref="Initialize"/> runs it holds <see cref="BuiltInThemes.Light"/> with only built-in
/// themes and no preferences store, so a window built under test gets deterministic colors regardless
/// of the machine's OS setting, and never writes the real <see cref="DevTermUserDataPaths.PreferencesFile"/>.
/// </remarks>
public static class ActiveTheme
{
    /// <summary>The configuration key behind <c>--theme</c> / <c>DEVTERM_THEME</c> - read directly, deliberately not a <see cref="CliOptions"/> property, since a theme is not part of a connection profile.</summary>
    public const string ConfigurationKey = "Theme";

    private static readonly Lock _lock = new();

    public static event EventHandler? Changed;

    public static DevTermTheme Current { get; private set; } = BuiltInThemes.Light;

    /// <summary>What was selected - <c>system</c> stays <c>system</c> here while <see cref="Current"/> is whichever built-in it resolved to.</summary>
    public static string Selection { get; private set; } = BuiltInThemes.LightName;

    public static ThemeCatalog Catalog { get; private set; } = ThemeCatalog.BuiltInOnly;

    /// <summary>Where <see cref="Select"/> persists a choice; null means don't persist (the default, and under test).</summary>
    public static AppPreferencesStore? Preferences { get; private set; }

    /// <summary>What the last <see cref="Initialize"/> returned - for a main window to show once it exists.</summary>
    public static IReadOnlyList<string> StartupProblems { get; private set; } = [];

    /// <summary>How <c>system</c> is resolved; replaceable for tests.</summary>
    public static Func<bool> PrefersDark { get; set; } = SystemThemeDetector.PrefersDark;

    /// <summary>
    /// Startup: loads the user's themes and preferences and selects, in precedence order,
    /// <c>--theme</c>/<c>DEVTERM_THEME</c>/an appsettings <c>Theme</c> (from <paramref name="configuration"/>),
    /// then the saved preference, then <c>system</c>. Doesn't persist a command-line choice - that's a
    /// one-run override. Returns every problem worth showing the user (bad theme files, an unknown
    /// theme name, an unreadable preferences file); none of them stop startup.
    /// </summary>
    public static IReadOnlyList<string> Initialize(IConfiguration? configuration, ThemeCatalog? catalog = null, AppPreferencesStore? preferences = null)
    {
        var problems = new List<string>();
        lock (_lock)
        {
            Catalog = catalog ?? ThemeCatalog.Load();
            Preferences = preferences ?? new AppPreferencesStore();
        }

        problems.AddRange(Catalog.Problems);
        var saved = Preferences.Load();
        if (Preferences.LoadProblem is { } loadProblem)
        {
            problems.Add(loadProblem);
        }

        var selection = configuration?[ConfigurationKey] is { Length: > 0 } fromConfiguration ? fromConfiguration : saved.Theme;
        if (Select(selection, persist: false) is { } warning)
        {
            problems.Add(warning);
        }

        StartupProblems = problems;
        return problems;
    }

    /// <summary>
    /// Makes <paramref name="selection"/> current and raises <see cref="Changed"/> (on the calling
    /// thread - a front end marshals to its UI thread itself if needed). With <paramref name="persist"/>,
    /// saves it as the preference. Returns a message if the name was unknown (and <c>system</c> used
    /// instead) or the preference couldn't be saved; otherwise null.
    /// </summary>
    public static string? Select(string? selection, bool persist = true)
    {
        var theme = Catalog.Resolve(selection, out var warning, PrefersDark);
        var resolvedSelection = warning is null && !string.IsNullOrWhiteSpace(selection)
            ? CanonicalName(selection.Trim())
            : BuiltInThemes.SystemName;

        lock (_lock)
        {
            Current = theme;
            Selection = resolvedSelection;
        }

        if (persist && Preferences is { } store)
        {
            var preferences = store.Load();
            preferences.Theme = resolvedSelection;
            warning ??= store.Save(preferences);
        }

        Changed?.Invoke(null, EventArgs.Empty);
        return warning;
    }

    /// <summary>Re-resolves <c>system</c> after the OS setting changed; a no-op for any other selection.</summary>
    public static void RefreshSystem()
    {
        if (string.Equals(Selection, BuiltInThemes.SystemName, StringComparison.OrdinalIgnoreCase)
            && Catalog.Resolve(BuiltInThemes.SystemName, out _, PrefersDark) != Current)
        {
            Select(BuiltInThemes.SystemName, persist: false);
        }
    }

    /// <summary>Back to the pre-<see cref="Initialize"/> state (Light, built-ins only, no persistence) - for tests.</summary>
    public static void Reset()
    {
        lock (_lock)
        {
            Catalog = ThemeCatalog.BuiltInOnly;
            Preferences = null;
            PrefersDark = SystemThemeDetector.PrefersDark;
            StartupProblems = [];
        }

        Select(BuiltInThemes.LightName, persist: false);
    }

    /// <summary>Loads a catalog (e.g. a test's own themes folder) without touching preferences.</summary>
    public static void UseCatalog(ThemeCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        lock (_lock)
        {
            Catalog = catalog;
        }
    }

    private static string CanonicalName(string selection) =>
        Catalog.SelectionNames.FirstOrDefault(name => string.Equals(name, selection, StringComparison.OrdinalIgnoreCase)) ?? selection;
}
