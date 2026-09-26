using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DevTerm.UiDefinitions;

namespace DevTerm.Wpf;

/// <summary>
/// Base of the WPF live display controls (bar graph, strip chart, vector plot) the control panel
/// renders: a fixed-size <see cref="FrameworkElement"/> that draws its shared
/// <see cref="LiveDisplayState"/> in <see cref="OnRender"/> and redraws whenever a
/// <c>ValuesChanged</c> batch changes it. The state — clamping, history, scaling, coordinates — is
/// the same one the TUI's character-cell renderer draws, so both front ends plot identically.
/// </summary>
internal abstract class LiveDisplayElement : FrameworkElement
{
    protected static readonly Brush MutedBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x8A, 0x89, 0x84)));
    protected static readonly Brush GridBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE4, 0xE3, 0xDF)));
    protected static readonly Brush TextBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x0B)));
    protected static readonly Brush SurfaceBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFC, 0xFC, 0xFB)));

    private static readonly Typeface _typeface = new("Segoe UI");

    protected LiveDisplayElement(LiveDisplayState state, double width, double height)
    {
        State = state;
        Width = width;
        Height = height;
        SnapsToDevicePixels = true;
    }

    public LiveDisplayState State { get; }

    /// <summary>Applies one <c>ValuesChanged</c> batch; redraws only when it changed what this plots.</summary>
    public void Apply(IReadOnlyDictionary<string, string> values)
    {
        if (State.ApplyAll(values))
        {
            InvalidateVisual();
        }
    }

    protected static Brush BrushFor(string hex) =>
        ChartPalette.TryParseHex(hex, out var c) ? Freeze(new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B))) : MutedBrush;

    protected FormattedText Text(string text, Brush brush, double size = 11) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static T Freeze<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}

/// <summary>A <see cref="BarGraphControl"/>: one horizontal bar per channel, label on the left and value on the right.</summary>
internal sealed class BarGraphElement : LiveDisplayElement
{
    private const double _rowHeight = 22;
    private const double _labelWidth = 48;
    private const double _barWidth = 220;

    public BarGraphElement(BarGraphState state)
        : base(state, _labelWidth + _barWidth + 90, Math.Max(state.Control.Channels.Count, 1) * _rowHeight)
    {
        Bars = state;
    }

    public BarGraphState Bars { get; }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var channels = Bars.Control.Channels;
        for (var row = 0; row < channels.Count; row++)
        {
            var channel = channels[row];
            var top = row * _rowHeight;
            var label = Text(channel.Label ?? channel.Id, TextBrush, 12);
            drawingContext.DrawText(label, new Point(0, top + ((_rowHeight - label.Height) / 2)));

            // The track, then the bar — 4px rounded end, 2px surface gap between rows.
            var track = new Rect(_labelWidth, top + 3, _barWidth, _rowHeight - 6);
            drawingContext.DrawRoundedRectangle(GridBrush, null, track, 4, 4);
            var fill = Bars.FractionOf(channel.Id) * _barWidth;
            if (fill > 0)
            {
                drawingContext.DrawRoundedRectangle(BrushFor(ChartPalette.ColorFor(channel, row)), null, new Rect(_labelWidth, top + 3, fill, _rowHeight - 6), 4, 4);
            }

            var valueText = Bars.ValueOf(channel.Id) is { } value ? ChartValue.Format(value, Bars.Control.Unit) : "—";
            var formatted = Text(valueText, TextBrush, 12);
            drawingContext.DrawText(formatted, new Point(_labelWidth + _barWidth + 8, top + ((_rowHeight - formatted.Height) / 2)));
        }
    }
}

/// <summary>A <see cref="StripChartControl"/>: a scrolling multi-trace line plot (newest at the right) with a value axis and a legend.</summary>
internal sealed class StripChartElement : LiveDisplayElement
{
    private const double _plotLeft = 44;
    private const double _plotWidth = 320;
    private const double _plotHeight = 120;
    private const double _legendHeight = 22;

    public StripChartElement(StripChartState state)
        : base(state, _plotLeft + _plotWidth + 8, _plotHeight + _legendHeight + 6)
    {
        Strip = state;
    }

    public StripChartState Strip { get; }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var (minimum, maximum) = Strip.Scale();
        var plot = new Rect(_plotLeft, 4, _plotWidth, _plotHeight);
        drawingContext.DrawRectangle(SurfaceBrush, new Pen(GridBrush, 1), plot);

        // Recessive gridlines at the quarters, with the value axis labeled at top, middle, bottom.
        var gridPen = new Pen(GridBrush, 1);
        for (var i = 1; i < 4; i++)
        {
            var y = plot.Top + (plot.Height * i / 4);
            drawingContext.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
        }

        foreach (var (fraction, value) in new[] { (0.0, maximum), (0.5, (minimum + maximum) / 2), (1.0, minimum) })
        {
            var label = Text(ChartValue.Format(value), MutedBrush, 10);
            drawingContext.DrawText(label, new Point(plot.Left - label.Width - 4, plot.Top + (plot.Height * fraction) - (label.Height / 2)));
        }

        var span = maximum - minimum;
        var capacity = Strip.Capacity;
        var legendX = plot.Left;
        for (var index = 0; index < Strip.Control.Channels.Count; index++)
        {
            var channel = Strip.Control.Channels[index];
            var brush = BrushFor(ChartPalette.ColorFor(channel, index));
            var samples = Strip.SamplesOf(channel.Id);
            if (samples.Count > 0)
            {
                var geometry = new StreamGeometry();
                using (var context = geometry.Open())
                {
                    for (var i = 0; i < samples.Count; i++)
                    {
                        var slot = capacity - samples.Count + i;
                        var x = plot.Left + (capacity <= 1 ? plot.Width : slot * plot.Width / (capacity - 1));
                        var f = span <= 0 ? 0.5 : Math.Clamp((samples[i] - minimum) / span, 0, 1);
                        var point = new Point(x, plot.Bottom - (f * plot.Height));
                        if (i == 0)
                        {
                            context.BeginFigure(point, isFilled: false, isClosed: false);
                        }
                        else
                        {
                            context.LineTo(point, isStroked: true, isSmoothJoin: true);
                        }
                    }
                }

                geometry.Freeze();
                drawingContext.PushClip(new RectangleGeometry(plot));
                drawingContext.DrawGeometry(null, new Pen(brush, 2) { LineJoin = PenLineJoin.Round }, geometry);
                drawingContext.Pop();
            }

            // Legend: a swatch in the series color, the name and latest value in text ink.
            var legendTop = plot.Bottom + 8;
            drawingContext.DrawRoundedRectangle(brush, null, new Rect(legendX, legendTop + 3, 10, 10), 2, 2);
            var latest = samples.Count > 0 ? ChartValue.Format(samples[^1], Strip.Control.Unit) : "—";
            var legend = Text($"{channel.Label ?? channel.Id}  {latest}", TextBrush, 11);
            drawingContext.DrawText(legend, new Point(legendX + 14, legendTop));
            legendX += 14 + legend.Width + 16;
        }
    }
}

/// <summary>A <see cref="VectorControl"/>: the current point and its fading trail on crosshair axes (a ring for polar, an oblique z axis for x/y/z), with a readout.</summary>
internal sealed class VectorElement : LiveDisplayElement
{
    private const double _plotSize = 150;
    private const double _readoutHeight = 18;

    public VectorElement(VectorState state)
        : base(state, _plotSize + 60, _plotSize + _readoutHeight + 4)
    {
        Vector = state;
    }

    public VectorState Vector { get; }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var control = Vector.Control;
        var plot = new Rect(0, 0, _plotSize, _plotSize);
        var center = new Point(plot.Width / 2, plot.Height / 2);
        var radius = (_plotSize / 2) - 6;
        var isXyz = control.Coordinates == CoordinateSystem.XYZ;
        var range = control.Range > 0 ? control.Range : 1;
        var scale = radius / range * (isXyz ? 0.7 : 1);

        drawingContext.DrawRectangle(SurfaceBrush, new Pen(GridBrush, 1), plot);
        var axisPen = new Pen(GridBrush, 1);
        drawingContext.DrawLine(axisPen, new Point(plot.Left, center.Y), new Point(plot.Right, center.Y));
        drawingContext.DrawLine(axisPen, new Point(center.X, plot.Top), new Point(center.X, plot.Bottom));

        Point Project((double X, double Y, double Z) p)
        {
            var zx = isXyz ? p.Z * 0.35 * Math.Cos(Math.PI / 4) : 0;
            var zy = isXyz ? p.Z * 0.35 * Math.Sin(Math.PI / 4) : 0;
            return new Point(center.X + ((p.X + zx) * scale), center.Y - ((p.Y + zy) * scale));
        }

        if (control.Coordinates == CoordinateSystem.Polar)
        {
            drawingContext.DrawEllipse(null, new Pen(MutedBrush, 1), center, radius, radius);
            drawingContext.DrawEllipse(null, new Pen(GridBrush, 1), center, radius / 2, radius / 2);
        }

        if (isXyz)
        {
            drawingContext.DrawLine(new Pen(MutedBrush, 1) { DashStyle = DashStyles.Dash }, Project((0, 0, -range)), Project((0, 0, range)));
        }

        var trail = Vector.Trail;
        for (var i = 0; i < trail.Count; i++)
        {
            // Older points fade out.
            var opacity = 0.15 + (0.5 * (i + 1) / trail.Count);
            drawingContext.PushOpacity(opacity);
            drawingContext.DrawEllipse(MutedBrush, null, Project(trail[i]), 2.5, 2.5);
            drawingContext.Pop();
        }

        if (Vector.HasPoint)
        {
            var point = Project(Vector.Point);
            Brush brush = Vector.Color is { } c
                ? new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B))
                : BrushFor(ChartPalette.Slots[0]);
            if (control.Coordinates == CoordinateSystem.Polar)
            {
                drawingContext.DrawLine(new Pen(brush, 2), center, point);
            }

            // A >=8px marker with a 2px surface ring so it stays legible over the trail.
            drawingContext.DrawEllipse(brush, new Pen(SurfaceBrush, 2), point, 5, 5);
        }

        drawingContext.DrawText(Text(Vector.Readout(), TextBrush, 11), new Point(0, _plotSize + 3));
    }
}
