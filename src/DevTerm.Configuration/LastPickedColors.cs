using System.Collections.Concurrent;
using System.Globalization;

namespace DevTerm.Configuration;

/// <summary>
/// The last color picked for each color-picker button (keyed by the button's control id, e.g.
/// the Busylight panel's custom-color button), so reopening the picker - or closing and reopening
/// the whole control panel - starts from the color last sent instead of resetting to white, and the
/// panel's swatch next to the button can show it. Shared by the TUI's <c>ControlPanelMode</c> and
/// WPF's <c>ControlPanelWindow</c>. Kept for the life of the process; not persisted across restarts.
/// </summary>
public static class LastPickedColors
{
    private static readonly ConcurrentDictionary<string, (byte R, byte G, byte B)> _colors = new(StringComparer.Ordinal);

    /// <summary>White until something has been picked for <paramref name="buttonId"/>.</summary>
    public static (byte R, byte G, byte B) Get(string buttonId) =>
        _colors.TryGetValue(buttonId, out var color) ? color : ((byte)255, (byte)255, (byte)255);

    /// <summary>Whether a color has been picked for <paramref name="buttonId"/> yet (the swatch only shows once one has).</summary>
    public static bool TryGet(string buttonId, out (byte R, byte G, byte B) color) => _colors.TryGetValue(buttonId, out color);

    public static void Set(string buttonId, (byte R, byte G, byte B) color) => _colors[buttonId] = color;

    /// <summary>Forgets <paramref name="buttonId"/>'s color, so its swatch is hidden again (the state is process-wide - screenshot tests use this to start from a known state).</summary>
    public static void Forget(string buttonId) => _colors.TryRemove(buttonId, out _);

    /// <summary><c>#RRGGBB</c>, what a swatch shows as its text.</summary>
    public static string ToHex((byte R, byte G, byte B) color) =>
        string.Create(CultureInfo.InvariantCulture, $"#{color.R:X2}{color.G:X2}{color.B:X2}");

    /// <summary>
    /// Whether dark (black) text reads better than light (white) text on <paramref name="color"/>,
    /// by relative luminance (ITU-R BT.709 weights) - so a yellow swatch gets black text and a navy
    /// one white.
    /// </summary>
    public static bool UseDarkText((byte R, byte G, byte B) color) =>
        (0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B) > 140;
}
