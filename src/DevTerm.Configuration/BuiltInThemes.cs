namespace DevTerm.Configuration;

/// <summary>
/// The themes that ship with dev-term, plus the <c>system</c> selection that picks between them.
/// <see cref="Light"/> keeps the colors the front ends hard-coded before theming existed (WPF's
/// stock control chrome, the green/amber/red status line), so it looks the way dev-term always has -
/// except <see cref="ThemeRole.Accent"/>/<see cref="ThemeRole.Warning"/>, whose legacy values (SteelBlue,
/// DarkGoldenrod) read as text in Playback's sent/note lines at only 4.1:1/3.3:1, below the 4.5:1 text
/// threshold; deepened just enough to clear it (2026-09-29, see docs/design/theming.md). <see cref="Dark"/>
/// is new.
/// </summary>
public static class BuiltInThemes
{
    public const string LightName = "light";
    public const string DarkName = "dark";

    /// <summary>Not a theme of its own: resolves to <see cref="Light"/> or <see cref="Dark"/> by the OS setting (<see cref="SystemThemeDetector"/>).</summary>
    public const string SystemName = "system";

    /// <summary>The TUI-only "terminal" selection: keep the terminal's own colors (see <c>TuiTheme.ApplyActive</c>); dev-term's own role colors (status line, charts) use light or dark by the terminal's <c>COLORFGBG</c> hint.</summary>
    public const string TerminalName = "terminal";

    public static DevTermTheme Light { get; } = Build(LightName, ChartPaletteVariant.Light, new()
    {
        [ThemeRole.Background] = "#FFFFFF",
        [ThemeRole.Foreground] = "#000000",
        [ThemeRole.MutedForeground] = "#696969",
        [ThemeRole.ControlBackground] = "#FFFFFF",
        [ThemeRole.ControlForeground] = "#000000",
        [ThemeRole.ControlBorder] = "#ABADB3",
        [ThemeRole.FieldBackground] = "#E4E4E4",
        [ThemeRole.ControlHoverBackground] = "#E5F1FB",
        [ThemeRole.SelectionBackground] = "#CCE8FF",
        [ThemeRole.SelectionForeground] = "#000000",
        [ThemeRole.MenuBackground] = "#F0F0F0",
        [ThemeRole.MenuForeground] = "#000000",
        [ThemeRole.Accent] = "#2E6DA4",
        [ThemeRole.Error] = "#8B0000",
        [ThemeRole.Warning] = "#8F6A09",
        [ThemeRole.OutputStatus] = "#696969",
        [ThemeRole.OutputError] = "#8B0000",
        [ThemeRole.StatusConnected] = "#78C878",
        [ThemeRole.StatusConnectedText] = "#000000",
        [ThemeRole.StatusConnecting] = "#E6C85A",
        [ThemeRole.StatusConnectingText] = "#000000",
        [ThemeRole.StatusDisconnected] = "#AA2828",
        [ThemeRole.StatusDisconnectedText] = "#FFFFFF",
        [ThemeRole.Recording] = "#B22222",
        [ThemeRole.SwatchBorder] = "#000000",
        [ThemeRole.ChartSurface] = "#FCFCFB",
        [ThemeRole.ChartGrid] = "#E4E3DF",
        [ThemeRole.ChartText] = "#0B0B0B",
        [ThemeRole.ChartMuted] = "#8A8984",
    });

    /// <summary>
    /// <see cref="ThemeRole.SelectionBackground"/> (<c>#2A66C2</c>) was brightened from the original
    /// VS-Code-style <c>#264F78</c> on 2026-09-30: under Terminal.Gui's legacy-conhost 16-color downgrade
    /// (see docs/design/theming.md), the old value's nearest ANSI-16 color was the same "DarkGray" that
    /// <see cref="ThemeRole.FieldBackground"/>/<see cref="ThemeRole.ControlHoverBackground"/> also snap to,
    /// so a focused field rendered the same background as a merely-editable one - "faint focus". The new
    /// value's nearest ANSI-16 color is "BrightBlue" instead, verified by
    /// <c>DevTerm.Console.Tests.LegacyConsole16ColorTests</c>.
    /// </summary>
    public static DevTermTheme Dark { get; } = Build(DarkName, ChartPaletteVariant.Dark, new()
    {
        [ThemeRole.Background] = "#1E1E1E",
        [ThemeRole.Foreground] = "#E6E6E6",
        [ThemeRole.MutedForeground] = "#A0A0A0",
        [ThemeRole.ControlBackground] = "#2B2B2B",
        [ThemeRole.ControlForeground] = "#E6E6E6",
        [ThemeRole.ControlBorder] = "#5A5A5A",
        [ThemeRole.FieldBackground] = "#3C3C3C",
        [ThemeRole.ControlHoverBackground] = "#3A3D41",
        [ThemeRole.SelectionBackground] = "#2A66C2",
        [ThemeRole.SelectionForeground] = "#FFFFFF",
        [ThemeRole.MenuBackground] = "#252526",
        [ThemeRole.MenuForeground] = "#E6E6E6",
        [ThemeRole.Accent] = "#6CB6FF",
        [ThemeRole.Error] = "#FF7B72",
        [ThemeRole.Warning] = "#E3B341",
        [ThemeRole.OutputStatus] = "#9A9A9A",
        [ThemeRole.OutputError] = "#FF6B6B",
        [ThemeRole.StatusConnected] = "#2E7D32",
        [ThemeRole.StatusConnectedText] = "#FFFFFF",
        [ThemeRole.StatusConnecting] = "#C99A06",
        [ThemeRole.StatusConnectingText] = "#000000",
        [ThemeRole.StatusDisconnected] = "#C62828",
        [ThemeRole.StatusDisconnectedText] = "#FFFFFF",
        [ThemeRole.Recording] = "#FF6B6B",
        [ThemeRole.SwatchBorder] = "#C0C0C0",
        [ThemeRole.ChartSurface] = "#1A1A19",
        [ThemeRole.ChartGrid] = "#2C2C2A",
        [ThemeRole.ChartText] = "#FFFFFF",
        [ThemeRole.ChartMuted] = "#898781",
    });

    /// <summary><see cref="Light"/> and <see cref="Dark"/>, in menu order.</summary>
    public static IReadOnlyList<DevTermTheme> All { get; } = [Light, Dark];

    /// <summary>Whether <paramref name="name"/> is reserved for a built-in (including <see cref="SystemName"/>), so a user theme can't take it.</summary>
    public static bool IsReservedName(string name) =>
        string.Equals(name, LightName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, DarkName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, SystemName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, TerminalName, StringComparison.OrdinalIgnoreCase);

    private static DevTermTheme Build(string name, ChartPaletteVariant chartPalette, Dictionary<ThemeRole, string> colors) =>
        new(name, colors.ToDictionary(pair => pair.Key, pair => ThemeColor.Parse(pair.Value)), chartPalette);
}
