namespace DevTerm.Configuration;

/// <summary>
/// A semantic color role - what a color is <em>for</em>, not where it's drawn. Both front ends look
/// colors up by role (WPF as <c>DynamicResource</c> brushes, the TUI as Terminal.Gui schemes and
/// attributes), so a theme is one table of these. A theme file names them in camelCase
/// (<c>"outputError": "#FF6B6B"</c>). See docs/design/theming.md.
/// </summary>
public enum ThemeRole
{
    /// <summary>Window/surface background.</summary>
    Background,

    /// <summary>Ordinary text on <see cref="Background"/>.</summary>
    Foreground,

    /// <summary>Secondary text (notes, subtitles, hints) on <see cref="Background"/>.</summary>
    MutedForeground,

    /// <summary>Background of input controls: text boxes, lists, combo boxes, buttons.</summary>
    ControlBackground,

    /// <summary>Text inside input controls.</summary>
    ControlForeground,

    /// <summary>Border of input controls.</summary>
    ControlBorder,

    /// <summary>
    /// Background of a text-entry field in the TUI, which has no border to mark where a field is - so
    /// it has to differ from <see cref="Background"/> (WPF's fields have borders and use <see cref="ControlBackground"/>).
    /// </summary>
    FieldBackground,

    /// <summary>Background of a control under the mouse.</summary>
    ControlHoverBackground,

    /// <summary>Background of the selected item (list, menu, combo box).</summary>
    SelectionBackground,

    /// <summary>Text of the selected item.</summary>
    SelectionForeground,

    /// <summary>Menu bar, drop-down menus and the status bar.</summary>
    MenuBackground,

    /// <summary>Text in menus and the status bar.</summary>
    MenuForeground,

    /// <summary>Focus and informational accents (the control panel's "ⓘ" icons).</summary>
    Accent,

    /// <summary>Error text outside the output pane: form validation, not-found hints, a panel's failed-send status.</summary>
    Error,

    /// <summary>Warning text (playback's warning lines).</summary>
    Warning,

    /// <summary>dev-term's own status lines in the output pane (<c>[dev-term] …</c>).</summary>
    OutputStatus,

    /// <summary>Error lines in the output pane (<c>[error] …</c>).</summary>
    OutputError,

    /// <summary>Connected: the WPF status dot, the TUI status line's background.</summary>
    StatusConnected,

    /// <summary>Text drawn on <see cref="StatusConnected"/> (the TUI status line).</summary>
    StatusConnectedText,

    /// <summary>Connecting: the WPF status dot, the TUI status line's background.</summary>
    StatusConnecting,

    /// <summary>Text drawn on <see cref="StatusConnecting"/>.</summary>
    StatusConnectingText,

    /// <summary>Disconnected: the WPF status dot, the TUI status line's background.</summary>
    StatusDisconnected,

    /// <summary>Text drawn on <see cref="StatusDisconnected"/>.</summary>
    StatusDisconnectedText,

    /// <summary>The logger's "● REC" indicator.</summary>
    Recording,

    /// <summary>The border around a color swatch (picked-color previews), so a swatch close to the background stays visible.</summary>
    SwatchBorder,

    /// <summary>The plot area of a chart (bar graph, strip chart, vector plot).</summary>
    ChartSurface,

    /// <summary>Chart gridlines, tracks and the plot border.</summary>
    ChartGrid,

    /// <summary>Chart labels, legends and readouts.</summary>
    ChartText,

    /// <summary>Chart axes, rings, trails and scale labels.</summary>
    ChartMuted,
}

/// <summary>
/// Which step of <c>ChartPalette</c>'s fixed, colorblind-safe categorical order a theme uses. A theme
/// never reorders or replaces the eight hues - it only picks the variant validated for its surface.
/// </summary>
public enum ChartPaletteVariant
{
    Light,
    Dark,
}

/// <summary>
/// One complete, immutable theme: a name plus a color for every <see cref="ThemeRole"/> and the chart
/// palette variant. Built-ins come from <see cref="BuiltInThemes"/>; user themes are loaded from JSON
/// by <see cref="ThemeFile"/>, starting from a built-in and overriding some roles. See
/// docs/design/theming.md.
/// </summary>
public sealed class DevTermTheme
{
    private readonly IReadOnlyDictionary<ThemeRole, ThemeColor> _colors;

    public DevTermTheme(string name, IReadOnlyDictionary<ThemeRole, ThemeColor> colors, ChartPaletteVariant chartPalette)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(colors);
        var missing = Enum.GetValues<ThemeRole>().Where(role => !colors.ContainsKey(role)).ToList();
        if (missing.Count > 0)
        {
            throw new ArgumentException($"Theme '{name}' is missing colors for: {string.Join(", ", missing)}.", nameof(colors));
        }

        Name = name;
        _colors = new Dictionary<ThemeRole, ThemeColor>(colors);
        ChartPalette = chartPalette;
    }

    /// <summary>The name shown in View &gt; Theme and accepted by <c>--theme</c>.</summary>
    public string Name { get; }

    public ChartPaletteVariant ChartPalette { get; }

    public ThemeColor this[ThemeRole role] => _colors[role];

    /// <summary>
    /// Whether this is a dark theme (its <see cref="ThemeRole.Background"/> is closer to black than
    /// white). WPF uses it to decide whether to swap in dev-term's own dark-capable control templates
    /// instead of the stock ones, whose chrome is hard-coded light (see docs/design/theming.md).
    /// </summary>
    public bool IsDark => this[ThemeRole.Background].RelativeLuminance() < 0.25;

    /// <summary>A copy with <paramref name="overrides"/> applied on top and a new name.</summary>
    public DevTermTheme With(string name, IReadOnlyDictionary<ThemeRole, ThemeColor> overrides, ChartPaletteVariant? chartPalette = null)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        var colors = new Dictionary<ThemeRole, ThemeColor>(_colors);
        foreach (var (role, color) in overrides)
        {
            colors[role] = color;
        }

        return new DevTermTheme(name, colors, chartPalette ?? ChartPalette);
    }

    /// <summary>
    /// The text/background pairs a reader has to be able to read, with the WCAG contrast each needs:
    /// 4.5:1 for ordinary text, 3:1 for secondary/large text and indicator fills. Checked for every
    /// built-in by the tests, and reported (not rejected) for a user theme - see <see cref="ContrastWarnings"/>.
    /// </summary>
    public static IReadOnlyList<(ThemeRole Text, ThemeRole Background, double Minimum)> ReadablePairs { get; } =
    [
        (ThemeRole.Foreground, ThemeRole.Background, 4.5),
        (ThemeRole.ControlForeground, ThemeRole.ControlBackground, 4.5),
        (ThemeRole.ControlForeground, ThemeRole.ControlHoverBackground, 4.5),
        (ThemeRole.ControlForeground, ThemeRole.FieldBackground, 4.5),
        (ThemeRole.SelectionForeground, ThemeRole.SelectionBackground, 4.5),
        (ThemeRole.MenuForeground, ThemeRole.MenuBackground, 4.5),
        (ThemeRole.MutedForeground, ThemeRole.Background, 3.0),
        (ThemeRole.Error, ThemeRole.Background, 4.5),
        (ThemeRole.Warning, ThemeRole.Background, 3.0),
        (ThemeRole.OutputStatus, ThemeRole.ControlBackground, 3.0),
        (ThemeRole.OutputError, ThemeRole.ControlBackground, 4.5),
        (ThemeRole.StatusConnectedText, ThemeRole.StatusConnected, 4.5),
        (ThemeRole.StatusConnectingText, ThemeRole.StatusConnecting, 4.5),
        (ThemeRole.StatusDisconnectedText, ThemeRole.StatusDisconnected, 4.5),
        (ThemeRole.Recording, ThemeRole.MenuBackground, 3.0),
        (ThemeRole.Accent, ThemeRole.Background, 3.0),
        (ThemeRole.ChartText, ThemeRole.ChartSurface, 4.5),
        (ThemeRole.ChartMuted, ThemeRole.ChartSurface, 3.0),
    ];

    /// <summary>One message per <see cref="ReadablePairs"/> entry this theme falls short on - empty for a readable theme.</summary>
    public IReadOnlyList<string> ContrastWarnings() =>
    [
        .. ReadablePairs
            .Select(pair => (pair.Text, pair.Background, pair.Minimum, Ratio: ThemeColor.ContrastRatio(this[pair.Text], this[pair.Background])))
            .Where(pair => pair.Ratio < pair.Minimum)
            .Select(pair => FormattableString.Invariant(
                $"Theme '{Name}': {ThemeFile.RoleName(pair.Text)} on {ThemeFile.RoleName(pair.Background)} has contrast {pair.Ratio:0.0}:1 (needs {pair.Minimum:0.0}:1) and may be hard to read.")),
    ];
}
