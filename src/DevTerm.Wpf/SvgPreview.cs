using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Xml;
using System.Xml.Linq;

namespace DevTerm.Wpf;

/// <summary>
/// Draws the small SVG subset dev-term's own converters write (and most simple plots use) as a WPF
/// <see cref="DrawingImage"/>: <c>path</c>, <c>line</c>, <c>polyline</c>, <c>polygon</c>, <c>rect</c>,
/// <c>circle</c> and <c>ellipse</c>, with <c>stroke</c>, <c>fill</c> and <c>stroke-width</c>, scaled by
/// the document's <c>viewBox</c> and drawn on white (lines are never thinner than 1/250 of the drawing, so a plot in plotter units stays visible). Transforms, gradients, text, CSS and
/// <c>use</c> are not drawn: WPF has no SVG decoder and dev-term takes no dependency for one, so
/// anything else is skipped rather than guessed at.
/// </summary>
internal static class SvgPreview
{
    /// <summary>Renders <paramref name="svg"/>, or returns <see langword="null"/> with why it couldn't.</summary>
    public static DrawingImage? TryRender(string svg, out string? error)
    {
        ArgumentNullException.ThrowIfNull(svg);
        try
        {
            var root = XDocument.Parse(svg).Root;
            if (root is null || root.Name.LocalName != "svg")
            {
                error = "it isn't an SVG document.";
                return null;
            }

            var bounds = Bounds(root);
            // A plot in plotter units (HP-GL: thousands across) with a 1-unit pen is a hairline once scaled to fit, so keep lines visible.
            var minStroke = Math.Max(bounds.Width, bounds.Height) / 250;
            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(bounds)));
            foreach (var element in root.Descendants())
            {
                if (Shape(element) is { } geometry)
                {
                    group.Children.Add(new GeometryDrawing(Fill(element), Stroke(element, minStroke), geometry));
                }
            }

            group.ClipGeometry = new RectangleGeometry(bounds);
            group.Freeze();
            var image = new DrawingImage(group);
            image.Freeze();
            error = null;
            return image;
        }
        catch (Exception ex) when (ex is XmlException or FormatException or InvalidOperationException or OverflowException)
        {
            error = ex.Message;
            return null;
        }
    }

    private static Rect Bounds(XElement svg)
    {
        if (svg.Attribute("viewBox")?.Value is { } viewBox)
        {
            var parts = viewBox.Split([' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 4 && parts.All(p => double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
            {
                var v = parts.Select(p => double.Parse(p, CultureInfo.InvariantCulture)).ToArray();
                if (v[2] > 0 && v[3] > 0)
                {
                    return new Rect(v[0], v[1], v[2], v[3]);
                }
            }
        }

        return new Rect(0, 0, Number(svg, "width", 100), Number(svg, "height", 100));
    }

    private static Geometry? Shape(XElement e) => e.Name.LocalName switch
    {
        "path" when e.Attribute("d")?.Value is { } d => Geometry.Parse(d),
        "line" => new LineGeometry(new Point(Number(e, "x1", 0), Number(e, "y1", 0)), new Point(Number(e, "x2", 0), Number(e, "y2", 0))),
        "rect" => new RectangleGeometry(new Rect(Number(e, "x", 0), Number(e, "y", 0), Number(e, "width", 0), Number(e, "height", 0)), Number(e, "rx", 0), Number(e, "ry", Number(e, "rx", 0))),
        "circle" => new EllipseGeometry(new Point(Number(e, "cx", 0), Number(e, "cy", 0)), Number(e, "r", 0), Number(e, "r", 0)),
        "ellipse" => new EllipseGeometry(new Point(Number(e, "cx", 0), Number(e, "cy", 0)), Number(e, "rx", 0), Number(e, "ry", 0)),
        "polyline" or "polygon" => Poly(e),
        _ => null,
    };

    private static Geometry? Poly(XElement e)
    {
        var numbers = (e.Attribute("points")?.Value ?? string.Empty)
            .Split([' ', ',', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(p => double.Parse(p, CultureInfo.InvariantCulture))
            .ToList();
        if (numbers.Count < 4)
        {
            return null;
        }

        var figure = new PathFigure { StartPoint = new Point(numbers[0], numbers[1]), IsClosed = e.Name.LocalName == "polygon", IsFilled = true };
        for (var i = 2; i + 1 < numbers.Count; i += 2)
        {
            figure.Segments.Add(new LineSegment(new Point(numbers[i], numbers[i + 1]), true));
        }

        return new PathGeometry([figure]);
    }

    private static double Number(XElement e, string name, double fallback) =>
        e.Attribute(name)?.Value is { } text && double.TryParse(text.Replace("px", string.Empty, StringComparison.Ordinal), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    /// <summary>SVG's default fill is black; a line has nothing to fill, and <c>fill="none"</c> means none.</summary>
    private static Brush? Fill(XElement e) => e.Name.LocalName is "line" ? null : Color(e.Attribute("fill")?.Value, Brushes.Black);

    private static Pen? Stroke(XElement e, double minWidth)
    {
        var brush = Color(e.Attribute("stroke")?.Value, null);
        return brush is null ? null : new Pen(brush, Math.Max(Number(e, "stroke-width", 1), minWidth)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
    }

    private static Brush? Color(string? value, Brush? fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (value.Trim().Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            return (Brush?)new BrushConverter().ConvertFromInvariantString(value.Trim()) ?? fallback;
        }
        catch (FormatException)
        {
            return fallback;
        }
    }
}
