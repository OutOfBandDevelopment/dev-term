using System.Text.Json;

namespace DevTerm.Configuration;

/// <summary>The outcome of loading one user theme file: the theme when it's valid, and every problem found either way.</summary>
/// <param name="Theme">The loaded theme, or <see langword="null"/> if the file was rejected.</param>
/// <param name="Errors">Why the file was rejected - empty when <paramref name="Theme"/> is set.</param>
/// <param name="Warnings">Problems that don't reject the file (low contrast) - worth showing, not fatal.</param>
public sealed record ThemeLoadResult(DevTermTheme? Theme, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

/// <summary>
/// Reads and validates a user theme file - JSON under <see cref="DevTermUserDataPaths.ThemesDirectory"/>:
/// <code>
/// {
///   "name": "Solarized Dark",
///   "basedOn": "dark",
///   "chartPalette": "dark",
///   "colors": { "background": "#002B36", "foreground": "#EEE8D5", "outputError": "#DC322F" }
/// }
/// </code>
/// Every field is optional except that the file must be a JSON object: <c>name</c> defaults to the
/// file name, <c>basedOn</c> to <c>light</c>, <c>chartPalette</c> to the base's. Roles left out keep
/// the base theme's color. A bad file never throws - it comes back with <see cref="ThemeLoadResult.Errors"/>
/// saying what's wrong (all of it, not just the first problem) so the front end can report it and
/// keep going on a built-in. See docs/design/theming.md.
/// </summary>
public static class ThemeFile
{
    private static readonly Dictionary<string, ThemeRole> _rolesByName =
        Enum.GetValues<ThemeRole>().ToDictionary(RoleName, role => role, StringComparer.OrdinalIgnoreCase);

    /// <summary>The camelCase name a theme file uses for <paramref name="role"/> (<c>outputError</c>).</summary>
    public static string RoleName(ThemeRole role)
    {
        var name = role.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    /// <summary>Loads <paramref name="path"/>; an unreadable file is an error result, not an exception.</summary>
    public static ThemeLoadResult Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ThemeLoadResult(null, [$"{Path.GetFileName(path)}: could not be read ({ex.Message})."], []);
        }

        return Parse(json, Path.GetFileNameWithoutExtension(path), Path.GetFileName(path));
    }

    /// <summary>Parses theme JSON; <paramref name="defaultName"/> is used when the file has no <c>name</c>, <paramref name="source"/> prefixes every message.</summary>
    public static ThemeLoadResult Parse(string json, string defaultName, string source)
    {
        ArgumentNullException.ThrowIfNull(json);
        var errors = new List<string>();
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            var where = ex.LineNumber is { } line ? $" (line {line + 1})" : string.Empty;
            return new ThemeLoadResult(null, [$"{source}: not valid JSON{where}: {ex.Message}"], []);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new ThemeLoadResult(null, [$"{source}: a theme file must be a JSON object ({{ \"name\": ..., \"colors\": {{ ... }} }})."], []);
            }

            var name = defaultName;
            var baseTheme = BuiltInThemes.Light;
            ChartPaletteVariant? chartPalette = null;
            var overrides = new Dictionary<ThemeRole, ThemeColor>();

            foreach (var property in root.EnumerateObject())
            {
                switch (property.Name.ToLowerInvariant())
                {
                    case "name":
                        if (property.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.Value.GetString()))
                        {
                            errors.Add($"{source}: \"name\" must be a non-empty string.");
                        }
                        else
                        {
                            name = property.Value.GetString()!.Trim();
                        }

                        break;

                    case "basedon":
                        var baseName = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                        if (string.Equals(baseName, BuiltInThemes.DarkName, StringComparison.OrdinalIgnoreCase))
                        {
                            baseTheme = BuiltInThemes.Dark;
                        }
                        else if (!string.Equals(baseName, BuiltInThemes.LightName, StringComparison.OrdinalIgnoreCase))
                        {
                            errors.Add($"{source}: \"basedOn\" must be \"light\" or \"dark\", not {property.Value.GetRawText()}.");
                        }

                        break;

                    case "chartpalette":
                        var variant = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                        if (string.Equals(variant, "light", StringComparison.OrdinalIgnoreCase))
                        {
                            chartPalette = ChartPaletteVariant.Light;
                        }
                        else if (string.Equals(variant, "dark", StringComparison.OrdinalIgnoreCase))
                        {
                            chartPalette = ChartPaletteVariant.Dark;
                        }
                        else
                        {
                            errors.Add($"{source}: \"chartPalette\" must be \"light\" or \"dark\", not {property.Value.GetRawText()}.");
                        }

                        break;

                    case "colors":
                        ReadColors(property.Value, source, overrides, errors);
                        break;

                    default:
                        errors.Add($"{source}: unknown setting \"{property.Name}\" (expected name, basedOn, chartPalette, colors).");
                        break;
                }
            }

            if (BuiltInThemes.IsReservedName(name))
            {
                errors.Add($"{source}: the name \"{name}\" is reserved for a built-in theme; pick another.");
            }

            if (errors.Count > 0)
            {
                return new ThemeLoadResult(null, errors, []);
            }

            var theme = baseTheme.With(name, overrides, chartPalette);
            return new ThemeLoadResult(theme, [], theme.ContrastWarnings());
        }
    }

    private static void ReadColors(JsonElement colors, string source, Dictionary<ThemeRole, ThemeColor> overrides, List<string> errors)
    {
        if (colors.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{source}: \"colors\" must be an object of role names to \"#RRGGBB\" colors.");
            return;
        }

        foreach (var color in colors.EnumerateObject())
        {
            if (!_rolesByName.TryGetValue(color.Name, out var role))
            {
                errors.Add($"{source}: unknown color role \"{color.Name}\". Known roles: {string.Join(", ", Enum.GetValues<ThemeRole>().Select(RoleName))}.");
                continue;
            }

            if (color.Value.ValueKind != JsonValueKind.String || !ThemeColor.TryParse(color.Value.GetString(), out var parsed))
            {
                errors.Add($"{source}: \"{color.Name}\" must be a \"#RRGGBB\" color, not {color.Value.GetRawText()}.");
                continue;
            }

            overrides[role] = parsed;
        }
    }
}
