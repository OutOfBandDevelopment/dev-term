using DevTerm.Configuration;

namespace DevTerm.Web;

/// <summary>The folder the Theme builder reads seeds from and saves into; the same place the desktop apps' View &gt; Theme lists.</summary>
public sealed class ThemeLibrary(string directory)
{
    public string Directory { get; } = directory;

    public ThemeCatalog Catalog() => ThemeCatalog.Load(Directory);

    /// <summary>Starts a builder from a seed name (light, dark, system or a user theme).</summary>
    public ThemeBuilderState Start(string seed, string name)
    {
        var theme = Catalog().Resolve(seed, out _, () => false);
        return new ThemeBuilderState(theme, name);
    }

    public string PathFor(string name) => Path.Combine(Directory, $"{name}.json");

    public bool Exists(string name) => File.Exists(PathFor(name));

    /// <summary>Validates the name and writes the theme; returns the problem, or null on success.</summary>
    public string? Save(ThemeBuilderState state, bool overwrite)
    {
        var name = state.Name.Trim();
        if (!ProfileName.IsValid(name))
        {
            return "Enter a valid theme name.";
        }

        if (BuiltInThemes.IsReservedName(name))
        {
            return $"'{name}' is reserved for a built-in theme; pick another name.";
        }

        if (Exists(name) && !overwrite)
        {
            return $"A theme named '{name}' already exists. Tick Overwrite to replace it.";
        }

        state.Name = name;
        System.IO.Directory.CreateDirectory(Directory);
        return state.Save(PathFor(name));
    }
}
