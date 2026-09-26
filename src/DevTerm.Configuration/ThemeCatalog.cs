namespace DevTerm.Configuration;

/// <summary>
/// Every theme a front end can offer: the built-ins plus each valid user theme file (<c>*.json</c>)
/// in the themes directory, and the problems found loading the rest. Loading never throws - a bad
/// or missing directory or file becomes a <see cref="Problems"/> entry. See docs/design/theming.md.
/// </summary>
public sealed class ThemeCatalog
{
    private ThemeCatalog(IReadOnlyList<DevTermTheme> userThemes, IReadOnlyList<string> problems)
    {
        UserThemes = userThemes;
        Problems = problems;
    }

    /// <summary>Only the built-ins - what a front end has before (or without) loading the user's themes, e.g. under test.</summary>
    public static ThemeCatalog BuiltInOnly { get; } = new([], []);

    /// <summary>Valid user themes, in file-name order.</summary>
    public IReadOnlyList<DevTermTheme> UserThemes { get; }

    /// <summary>Why each rejected file was rejected, and any contrast warnings for accepted ones.</summary>
    public IReadOnlyList<string> Problems { get; }

    /// <summary>The names View &gt; Theme lists: <c>light</c>, <c>dark</c>, <c>system</c>, then user themes.</summary>
    public IReadOnlyList<string> SelectionNames =>
        [BuiltInThemes.LightName, BuiltInThemes.DarkName, BuiltInThemes.SystemName, .. UserThemes.Select(theme => theme.Name)];

    /// <summary>Loads every <c>*.json</c> in <paramref name="themesDirectory"/> (default <see cref="DevTermUserDataPaths.ThemesDirectory"/>); a missing directory is simply no user themes.</summary>
    public static ThemeCatalog Load(string? themesDirectory = null)
    {
        var directory = themesDirectory ?? DevTermUserDataPaths.ThemesDirectory;
        if (!Directory.Exists(directory))
        {
            return BuiltInOnly;
        }

        IEnumerable<string> files;
        try
        {
            files = [.. Directory.EnumerateFiles(directory, "*.json").OrderBy(path => path, StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ThemeCatalog([], [$"Could not list themes in {directory}: {ex.Message}"]);
        }

        var themes = new List<DevTermTheme>();
        var problems = new List<string>();
        foreach (var file in files)
        {
            var result = ThemeFile.Load(file);
            problems.AddRange(result.Errors);
            if (result.Theme is not { } theme)
            {
                continue;
            }

            if (themes.Any(existing => string.Equals(existing.Name, theme.Name, StringComparison.OrdinalIgnoreCase)))
            {
                problems.Add($"{Path.GetFileName(file)}: another theme file is already named \"{theme.Name}\"; this one was skipped.");
                continue;
            }

            problems.AddRange(result.Warnings);
            themes.Add(theme);
        }

        return new ThemeCatalog(themes, problems);
    }

    /// <summary>
    /// The theme <paramref name="selection"/> names: <c>light</c>, <c>dark</c>, <c>system</c> (resolved
    /// by <paramref name="prefersDark"/>, default <see cref="SystemThemeDetector.PrefersDark"/>) or a
    /// user theme's name, case-insensitively. Empty means <c>system</c>. An unknown name falls back to
    /// <c>system</c> with <paramref name="warning"/> saying so, rather than failing.
    /// </summary>
    public DevTermTheme Resolve(string? selection, out string? warning, Func<bool>? prefersDark = null)
    {
        warning = null;
        var name = string.IsNullOrWhiteSpace(selection) ? BuiltInThemes.SystemName : selection.Trim();
        if (string.Equals(name, BuiltInThemes.LightName, StringComparison.OrdinalIgnoreCase))
        {
            return BuiltInThemes.Light;
        }

        if (string.Equals(name, BuiltInThemes.DarkName, StringComparison.OrdinalIgnoreCase))
        {
            return BuiltInThemes.Dark;
        }

        if (UserThemes.FirstOrDefault(theme => string.Equals(theme.Name, name, StringComparison.OrdinalIgnoreCase)) is { } user)
        {
            return user;
        }

        if (!string.Equals(name, BuiltInThemes.SystemName, StringComparison.OrdinalIgnoreCase))
        {
            warning = $"Unknown theme \"{name}\" (expected light, dark, system, or a theme file's name under {DevTermUserDataPaths.ThemesDirectory}); using system.";
        }

        return (prefersDark ?? SystemThemeDetector.PrefersDark)() ? BuiltInThemes.Dark : BuiltInThemes.Light;
    }
}
