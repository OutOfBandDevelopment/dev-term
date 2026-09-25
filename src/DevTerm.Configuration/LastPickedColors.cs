using System.Collections.Concurrent;

namespace DevTerm.Configuration;

/// <summary>
/// The last color picked for each color-picker button (keyed by the button's control id, e.g.
/// the Busylight panel's custom-color button), so reopening the picker - or closing and reopening
/// the whole control panel - starts from the color last sent instead of resetting to white. Shared
/// by the TUI's <c>ControlPanelMode</c> and WPF's <c>ControlPanelWindow</c>. Kept for the life of
/// the process; not persisted across restarts.
/// </summary>
public static class LastPickedColors
{
    private static readonly ConcurrentDictionary<string, (byte R, byte G, byte B)> _colors = new(StringComparer.Ordinal);

    /// <summary>White until something has been picked for <paramref name="buttonId"/>.</summary>
    public static (byte R, byte G, byte B) Get(string buttonId) =>
        _colors.TryGetValue(buttonId, out var color) ? color : ((byte)255, (byte)255, (byte)255);

    public static void Set(string buttonId, (byte R, byte G, byte B) color) => _colors[buttonId] = color;
}
