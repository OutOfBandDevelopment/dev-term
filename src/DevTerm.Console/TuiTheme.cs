using DevTerm.Configuration;
using DevTerm.Core.Transports;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace DevTerm.Console;

/// <summary>
/// Maps a <see cref="DevTermTheme"/>'s roles onto Terminal.Gui v2.5.0. The window chrome goes through
/// Terminal.Gui's own named schemes (<c>Base</c>, <c>Menu</c>, <c>Dialog</c>, <c>Error</c>, <c>Accent</c> -
/// overridden with <see cref="SchemeManager.AddScheme"/>, which every view without its own scheme
/// picks up on its next draw, so a live switch is just <see cref="Apply"/> plus a redraw); dev-term's own
/// colors (status line, output highlighting, charts) are looked up per role where they're drawn.
/// </summary>
/// <remarks>
/// Terminal.Gui's built-in themes (<c>ThemeManager.Theme = "Dark"</c>, "Light", ...) weren't used: checked
/// directly against the installed package, their <c>Base</c> scheme leaves the background as
/// <c>None</c> (the terminal's own), so "Dark" draws light-gray text on whatever the terminal's
/// background is - unreadable on a light terminal. dev-term's themes set every background explicitly.
/// <see cref="SchemeManager"/> is process-wide, not per <c>IApplication</c>; overrides survive
/// <c>Application.Init</c> (checked), and <see cref="Restore"/> puts Terminal.Gui's own schemes back.
/// </remarks>
internal static class TuiTheme
{
    private static readonly string[] _schemeNames = ["Base", "Menu", "Dialog", "Error", "Accent"];
    private static readonly Lock _lock = new();
    private static Dictionary<string, Scheme>? _originals;

    /// <summary>The theme last applied, or null while Terminal.Gui's own (terminal-colored) schemes are in effect.</summary>
    public static DevTermTheme? Applied { get; private set; }

    public static Color ToColor(ThemeColor color) => new(color.R, color.G, color.B, 255);

    public static Attribute Attribute(DevTermTheme theme, ThemeRole foreground, ThemeRole background) =>
        new(ToColor(theme[foreground]), ToColor(theme[background]));

    /// <summary>Overrides Terminal.Gui's schemes from <paramref name="theme"/>. Callers redraw afterwards.</summary>
    public static void Apply(DevTermTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        lock (_lock)
        {
            _originals ??= SchemeManager.GetSchemesForCurrentTheme()
                .Where(pair => pair.Value is not null && _schemeNames.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value!);

            var window = Surface(theme, ThemeRole.Foreground, ThemeRole.Background);
            SchemeManager.AddScheme("Base", window);
            SchemeManager.AddScheme("Dialog", window);
            SchemeManager.AddScheme("Accent", window);
            SchemeManager.AddScheme("Menu", Surface(theme, ThemeRole.MenuForeground, ThemeRole.MenuBackground));
            SchemeManager.AddScheme("Error", Surface(theme, ThemeRole.Error, ThemeRole.Background));
            Applied = theme;
        }
    }

    /// <summary>Puts Terminal.Gui's own schemes back (tests; a no-op if nothing was applied).</summary>
    public static void Restore()
    {
        lock (_lock)
        {
            if (_originals is null)
            {
                return;
            }

            foreach (var (name, scheme) in _originals)
            {
                SchemeManager.AddScheme(name, scheme);
            }

            Applied = null;
        }
    }

    /// <summary>The status line's colors for <paramref name="state"/>: <c>StatusConnected</c>/<c>StatusConnectedText</c> etc.</summary>
    public static Attribute StatusAttribute(DevTermTheme theme, ConnectionState state) => state switch
    {
        ConnectionState.Open => Attribute(theme, ThemeRole.StatusConnectedText, ThemeRole.StatusConnected),
        ConnectionState.Opening => Attribute(theme, ThemeRole.StatusConnectingText, ThemeRole.StatusConnecting),
        _ => Attribute(theme, ThemeRole.StatusDisconnectedText, ThemeRole.StatusDisconnected),
    };

    /// <summary>A scheme for a status-style label: <paramref name="attribute"/> everywhere, so focus/hover can't swap in unthemed colors.</summary>
    public static Scheme Solid(Attribute attribute) => new(attribute);

    private static Scheme Surface(DevTermTheme theme, ThemeRole foreground, ThemeRole background)
    {
        var normal = Attribute(theme, foreground, background);
        var editable = Attribute(theme, ThemeRole.ControlForeground, ThemeRole.FieldBackground);
        var selected = Attribute(theme, ThemeRole.SelectionForeground, ThemeRole.SelectionBackground);
        return new Scheme(normal)
        {
            HotNormal = new Attribute(normal.Foreground, normal.Background, TextStyle.Underline),
            Focus = selected,
            HotFocus = new Attribute(selected.Foreground, selected.Background, TextStyle.Underline),
            Active = new Attribute(selected.Foreground, selected.Background, TextStyle.Bold),
            HotActive = new Attribute(selected.Foreground, selected.Background, TextStyle.Bold | TextStyle.Underline),
            Editable = editable,
            ReadOnly = editable,
            Disabled = new Attribute(ToColor(theme[ThemeRole.MutedForeground]), normal.Background),
            Highlight = new Attribute(normal.Foreground, ToColor(theme[ThemeRole.ControlHoverBackground])),
        };
    }
}
