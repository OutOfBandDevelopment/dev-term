using System.Globalization;
using System.Text;

namespace DevTerm.Core.StreamContent;

/// <summary>
/// A small, dev-term-owned HP-GL instruction interpreter that emits a static SVG file — the
/// "internal HP/GL-to-SVG converter" mechanism from
/// docs/design/proposals/stream-content-detection.md's "Raster/convert tool integration" section.
/// No external dependency, no network call, and (unlike the not-yet-built HPGL/PostScript/PCL
/// rendering presenter from docs/design/presenters.md §3, which aims at a live on-screen preview)
/// this only ever needs to produce one static SVG string for export.
/// </summary>
/// <remarks>
/// Recognizes <c>IN</c> (initialize — resets pen/mode and flushes), <c>SP n</c> (select pen — maps
/// to one of a small, fixed stroke-color palette, since physical pen colors aren't knowable from the
/// instruction stream alone), <c>PU</c>/<c>PD</c> (pen up/down, each optionally followed by one or
/// more comma-separated coordinate pairs), and <c>PA</c>/<c>PR</c> (switch to absolute/relative
/// coordinate interpretation, without otherwise changing state). Every other command is ignored.
/// <c>PU</c> always ends ("flushes") any open drawn path; consecutive <c>PD</c> commands with no
/// intervening <c>PU</c> chain into one continuous polyline rather than separate single-segment
/// paths. A path's stroke color is fixed at the pen number in effect when the path is *started*, not
/// when it's flushed, since <c>SP</c> can change the pen mid-path without an intervening <c>PU</c>/
/// <c>PD</c>.
/// </remarks>
public static class HpglToSvgConverter
{
    /// <summary>A small, fixed palette standing in for physical pen colors, which the instruction stream never actually specifies.</summary>
    private static readonly string[] _penColors =
    [
        "#000000", "#e6194b", "#3cb44b", "#4363d8",
        "#f58231", "#911eb4", "#46f0f0", "#f032e6",
    ];

    private readonly record struct Point(double X, double Y);

    private sealed class PenPath
    {
        public required int Pen { get; init; }

        public List<Point> Points { get; } = [];
    }

    /// <summary>
    /// Parses <paramref name="data"/> as HP-GL and renders it to an SVG document (UTF-8 encoded,
    /// since that's what every capture/export path in this codebase deals in). Never throws on
    /// malformed input — an instruction it can't parse is simply skipped, the same tolerance the
    /// rest of dev-term's wire-format parsing uses.
    /// </summary>
    public static string ConvertToSvg(ReadOnlySpan<byte> data)
    {
        var text = Encoding.ASCII.GetString(data);
        var paths = new List<PenPath>();
        PenPath? current = null;
        var pen = 1;
        var relative = false;
        double curX = 0, curY = 0;

        void Flush()
        {
            if (current is { Points.Count: > 1 })
            {
                paths.Add(current);
            }

            current = null;
        }

        foreach (var rawInstruction in text.Split(';'))
        {
            var instruction = rawInstruction.Trim();
            if (instruction.Length < 2)
            {
                continue;
            }

            var mnemonic = instruction[..2].ToUpperInvariant();
            var rest = instruction[2..];

            switch (mnemonic)
            {
                case "IN":
                    Flush();
                    pen = 1;
                    relative = false;
                    curX = 0;
                    curY = 0;
                    break;

                case "SP":
                    if (int.TryParse(rest.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var selected))
                    {
                        pen = selected;
                    }

                    break;

                case "PA":
                    relative = false;
                    ApplyCoordinates(rest, draw: false);
                    break;

                case "PR":
                    relative = true;
                    ApplyCoordinates(rest, draw: false);
                    break;

                case "PU":
                    Flush();
                    ApplyCoordinates(rest, draw: false);
                    break;

                case "PD":
                    ApplyCoordinates(rest, draw: true);
                    break;
            }
        }

        Flush();
        return Render(paths);

        void ApplyCoordinates(string rest, bool draw)
        {
            var values = ParseNumbers(rest);
            for (var i = 0; i + 1 < values.Count; i += 2)
            {
                var fromX = curX;
                var fromY = curY;

                if (relative)
                {
                    curX += values[i];
                    curY += values[i + 1];
                }
                else
                {
                    curX = values[i];
                    curY = values[i + 1];
                }

                if (draw)
                {
                    current ??= new PenPath { Pen = pen };
                    if (current.Points.Count == 0)
                    {
                        // The pen-down run starts at wherever the pen already was (the last PU/PA/PR
                        // position), not just at the first drawn point.
                        current.Points.Add(new Point(fromX, fromY));
                    }

                    current.Points.Add(new Point(curX, curY));
                }
                // A plain PU/PA/PR move never draws, but it does update curX/curY, establishing the
                // position an immediately-following PD should start its first segment from.
            }
        }
    }

    private static List<double> ParseNumbers(string rest)
    {
        var values = new List<double>();
        foreach (var token in rest.Split(','))
        {
            var trimmed = token.Trim();
            if (trimmed.Length > 0 && double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                values.Add(value);
            }
        }

        return values;
    }

    private static string Render(List<PenPath> paths)
    {
        const double margin = 20;

        if (paths.Count == 0)
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                   "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"100\" viewBox=\"0 0 100 100\"></svg>\n";
        }

        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;
        foreach (var path in paths)
        {
            foreach (var point in path.Points)
            {
                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
            }
        }

        var width = Math.Max(maxX - minX, 1) + (margin * 2);
        var height = Math.Max(maxY - minY, 1) + (margin * 2);

        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        builder.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width:F2}\" height=\"{height:F2}\" viewBox=\"0 0 {width:F2} {height:F2}\">\n");

        foreach (var path in paths)
        {
            var color = _penColors[((path.Pen % _penColors.Length) + _penColors.Length) % _penColors.Length];
            builder.Append("  <path d=\"");
            for (var i = 0; i < path.Points.Count; i++)
            {
                var point = path.Points[i];
                // HP-GL's Y axis increases upward; SVG's increases downward.
                var svgX = point.X - minX + margin;
                var svgY = height - (point.Y - minY + margin);
                builder.Append(CultureInfo.InvariantCulture, $"{(i == 0 ? "M" : "L")} {svgX:F2},{svgY:F2} ");
            }

            builder.Append(CultureInfo.InvariantCulture, $"\" fill=\"none\" stroke=\"{color}\" stroke-width=\"1\" />\n");
        }

        builder.Append("</svg>\n");
        return builder.ToString();
    }
}
