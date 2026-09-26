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
    /// Whether dark (black) text reads better than light (white) text on <paramref name="color"/>:
    /// whichever has the higher WCAG contrast ratio against it, from its relative luminance (linear
    /// sRGB, BT.709 weights) - so a yellow swatch gets black text and a navy one white. Black wins
    /// once the luminance reaches about 0.179. (A weighted sum of the raw 0-255 values used to be
    /// compared with 140, which gave an orange like #FF6600 white text at 2.9:1 where black is 7.2:1.)
    /// </summary>
    public static bool UseDarkText((byte R, byte G, byte B) color)
    {
        static double Linear(byte value)
        {
            var c = value / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        var luminance = (0.2126 * Linear(color.R)) + (0.7152 * Linear(color.G)) + (0.0722 * Linear(color.B));
        var againstBlack = (luminance + 0.05) / 0.05;
        var againstWhite = 1.05 / (luminance + 0.05);
        return againstBlack >= againstWhite;
    }
}
