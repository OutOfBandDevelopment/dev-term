using System.Globalization;

namespace DevTerm.Configuration;

/// <summary>
/// One opaque RGB color in a <see cref="DevTermTheme"/>, written <c>#RRGGBB</c> in theme files.
/// Front ends convert it to their own color type (a WPF <c>SolidColorBrush</c>, a Terminal.Gui
/// <c>Color</c>); this type stays UI-framework-free so both share one theme model.
/// </summary>
public readonly record struct ThemeColor(byte R, byte G, byte B)
{
    /// <summary><c>#RRGGBB</c>, upper-case.</summary>
    public string ToHex() => string.Create(CultureInfo.InvariantCulture, $"#{R:X2}{G:X2}{B:X2}");

    public override string ToString() => ToHex();

    /// <summary>Parses <c>#RRGGBB</c> (the leading <c>#</c> is required, so a typo like a bare color name is rejected rather than guessed at).</summary>
    public static bool TryParse(string? text, out ThemeColor color)
    {
        color = default;
        var trimmed = text?.Trim();
        if (trimmed is not { Length: 7 } || trimmed[0] != '#'
            || !byte.TryParse(trimmed.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            || !byte.TryParse(trimmed.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            || !byte.TryParse(trimmed.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return false;
        }

        color = new ThemeColor(r, g, b);
        return true;
    }

    /// <summary>Parses <c>#RRGGBB</c>, throwing <see cref="FormatException"/> otherwise - for built-in theme tables, where a bad value is a bug.</summary>
    public static ThemeColor Parse(string text) =>
        TryParse(text, out var color) ? color : throw new FormatException($"'{text}' is not a #RRGGBB color.");

    /// <summary>WCAG 2 relative luminance, 0 (black) to 1 (white).</summary>
    public double RelativeLuminance()
    {
        static double Channel(byte value)
        {
            var c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(R)) + (0.7152 * Channel(G)) + (0.0722 * Channel(B));
    }

    /// <summary>WCAG 2 contrast ratio between two colors, 1 (identical) to 21 (black on white).</summary>
    public static double ContrastRatio(ThemeColor first, ThemeColor second)
    {
        var a = first.RelativeLuminance();
        var b = second.RelativeLuminance();
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }
}
