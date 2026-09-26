using System.Globalization;

namespace DevTerm.UiDefinitions;

/// <summary>
/// The default categorical colors for chart channels, in a fixed order (never cycled): channel n
/// without its own <see cref="ChartChannel.Color"/> takes slot n. The eight hues are a validated
/// colorblind-safe categorical order (blue, orange, aqua, yellow, magenta, green, violet, red); a
/// ninth-or-later channel falls back to a neutral gray rather than repeating a hue, so identity is
/// carried by its legend label instead. Shared by both front ends so a channel is the same color in
/// the TUI and WPF. A dark theme swaps in <see cref="DarkSlots"/> - the same eight hues in the same
/// order, each stepped for a dark chart surface (validated as a set against <c>#1A1A19</c>) - never a
/// different order or palette, since the order is what keeps adjacent series CVD-distinguishable.
/// </summary>
public static class ChartPalette
{
    public static readonly IReadOnlyList<string> Slots =
    [
        "#2A78D6",
        "#EB6834",
        "#1BAF7A",
        "#EDA100",
        "#E87BA4",
        "#008300",
        "#4A3AA7",
        "#E34948",
    ];

    /// <summary><see cref="Slots"/> stepped for a dark surface: same hues, same order.</summary>
    public static readonly IReadOnlyList<string> DarkSlots =
    [
        "#3987E5",
        "#D95926",
        "#199E70",
        "#C98500",
        "#D55181",
        "#008300",
        "#9085E9",
        "#E66767",
    ];

    public const string Overflow = "#8A8984";

    /// <summary><see cref="Slots"/> or <see cref="DarkSlots"/>.</summary>
    public static IReadOnlyList<string> SlotsFor(bool dark) => dark ? DarkSlots : Slots;

    /// <summary>The <c>#RRGGBB</c> color for the <paramref name="index"/>th channel of a chart, from the light palette.</summary>
    public static string ColorFor(ChartChannel channel, int index) => ColorFor(channel, index, dark: false);

    /// <summary>The <c>#RRGGBB</c> color for the <paramref name="index"/>th channel of a chart, from <see cref="SlotsFor"/>(<paramref name="dark"/>). A channel's own explicit color always wins.</summary>
    public static string ColorFor(ChartChannel channel, int index, bool dark)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (channel.Color is { } explicitColor && TryParseHex(explicitColor, out _))
        {
            return explicitColor;
        }

        var slots = SlotsFor(dark);
        return index >= 0 && index < slots.Count ? slots[index] : Overflow;
    }

    /// <summary>Parses <c>#RRGGBB</c> (or <c>RRGGBB</c>).</summary>
    public static bool TryParseHex(string? hex, out (byte R, byte G, byte B) color)
    {
        color = default;
        var text = hex?.Trim().TrimStart('#');
        if (text is not { Length: 6 }
            || !byte.TryParse(text.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            || !byte.TryParse(text.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            || !byte.TryParse(text.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return false;
        }

        color = (r, g, b);
        return true;
    }

    /// <summary>Standard HSV→RGB; hue in degrees (wrapped), saturation/value clamped to [0, 1].</summary>
    public static (byte R, byte G, byte B) FromHsv(double hue, double saturation, double value)
    {
        var h = ((hue % 360) + 360) % 360;
        var s = Math.Clamp(saturation, 0, 1);
        var v = Math.Clamp(value, 0, 1);
        var c = v * s;
        var hPrime = h / 60.0;
        var x = c * (1 - Math.Abs((hPrime % 2) - 1));
        var (r1, g1, b1) = hPrime switch
        {
            < 1 => (c, x, 0.0),
            < 2 => (x, c, 0.0),
            < 3 => (0.0, c, x),
            < 4 => (0.0, x, c),
            < 5 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };

        var m = v - c;
        return ((byte)Math.Round((r1 + m) * 255), (byte)Math.Round((g1 + m) * 255), (byte)Math.Round((b1 + m) * 255));
    }
}
