using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using Terminal.Gui.App;

[assembly: SupportedOSPlatform("windows")]

namespace DevTerm.Console.Tests;

/// <summary>
/// Renders the current Terminal.Gui screen buffer to a real PNG — a proper screenshot, not the
/// plain-text <see cref="TuiTestRunner.DumpBuffer"/> capture docs used before. Each cell's real
/// foreground/background <c>Terminal.Gui.Drawing.Color</c> (confirmed via reflection to be real
/// RGB, not just a 16-color name — <c>Cell.Attribute?.Foreground/Background</c>) is drawn as a
/// filled rectangle plus its grapheme, using <c>System.Drawing.Common</c> — Windows-only, same as
/// the rest of this repo's Windows-specific test infra (WPF).
///
/// Confirmed empirically: the headless "dotnet" driver reports <c>fg=(255,255,255) bg=(255,255,255)</c>
/// (white-on-white) for any cell still on the default, unstyled color scheme — there's no real
/// terminal behind headless mode to resolve an actual theme, so plain <c>Label</c> text and the
/// window border came back completely invisible until this was found (rendered fine in
/// <see cref="TuiTestRunner.DumpBuffer"/>'s plain-text dump, since that only reads
/// <c>Cell.Grapheme</c>, never color). Only cells with a real, distinguishable style — the menu
/// bar's highlight, a focused field — report fg != bg. So: trust the driver's reported colors only
/// when they actually differ; otherwise fall back to plain black-on-white.
/// </summary>
internal static class TuiScreenshot
{
    private const int CellWidth = 9;
    private const int CellHeight = 18;

    public static void Save(string path)
    {
        var buffer = Application.Driver!.GetOutputBuffer();
        var width = buffer.Cols * CellWidth;
        var height = buffer.Rows * CellHeight;

        using var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        using var font = new Font("Cascadia Mono", CellHeight - 4f, FontStyle.Regular, GraphicsUnit.Pixel);
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        graphics.Clear(Color.White);

        for (var row = 0; row < buffer.Rows; row++)
        {
            for (var col = 0; col < buffer.Cols; col++)
            {
                var cell = buffer.Contents[row, col];
                var x = col * CellWidth;
                var y = row * CellHeight;

                var (background, foreground) = ResolveColors(cell.Attribute);

                using (var backgroundBrush = new SolidBrush(background))
                {
                    graphics.FillRectangle(backgroundBrush, x, y, CellWidth, CellHeight);
                }

                if (!string.IsNullOrWhiteSpace(cell.Grapheme))
                {
                    using var foregroundBrush = new SolidBrush(foreground);
                    graphics.DrawString(cell.Grapheme, font, foregroundBrush, x, y - 2);
                }
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        bitmap.Save(path, ImageFormat.Png);
    }

    private static (Color Background, Color Foreground) ResolveColors(Terminal.Gui.Drawing.Attribute? attribute)
    {
        if (attribute is not { } value)
        {
            return (Color.White, Color.Black);
        }

        var background = Color.FromArgb(value.Background.R, value.Background.G, value.Background.B);
        var foreground = Color.FromArgb(value.Foreground.R, value.Foreground.G, value.Foreground.B);

        return background == foreground ? (Color.White, Color.Black) : (background, foreground);
    }
}
