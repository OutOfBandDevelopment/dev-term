using System.Text;
using DevTerm.UiDefinitions;

namespace DevTerm.Console;

/// <summary>One character cell of a chart: its glyph and an optional <c>#RRGGBB</c> foreground (null draws in the view's normal color).</summary>
internal readonly record struct CellGlyph(char Char, string? Color);

/// <summary>A fixed-size grid of <see cref="CellGlyph"/>s — what a TUI chart renders to, before <see cref="CellCanvasView"/> draws it.</summary>
internal sealed class CellGrid
{
    private readonly CellGlyph[,] _cells;

    public CellGrid(int width, int height)
    {
        Width = Math.Max(width, 1);
        Height = Math.Max(height, 1);
        _cells = new CellGlyph[Width, Height];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                _cells[x, y] = new CellGlyph(' ', null);
            }
        }
    }

    public int Width { get; }

    public int Height { get; }

    public CellGlyph this[int x, int y] => _cells[x, y];

    public void Set(int x, int y, char c, string? color)
    {
        if (x >= 0 && x < Width && y >= 0 && y < Height)
        {
            _cells[x, y] = new CellGlyph(c, color);
        }
    }

    /// <summary>Writes <paramref name="text"/> left to right from (x, y), clipped at the right edge; returns the column after it.</summary>
    public int Write(int x, int y, string text, string? color = null)
    {
        foreach (var c in text)
        {
            Set(x++, y, c, color);
        }

        return x;
    }

    /// <summary>The grid's text, one string per row (trailing spaces kept) — what tests assert on.</summary>
    public IReadOnlyList<string> Lines()
    {
        var lines = new List<string>(Height);
        for (var y = 0; y < Height; y++)
        {
            var builder = new StringBuilder(Width);
            for (var x = 0; x < Width; x++)
            {
                builder.Append(_cells[x, y].Char);
            }

            lines.Add(builder.ToString());
        }

        return lines;
    }
}

/// <summary>
/// A braille-dot canvas: each character cell holds a 2×4 grid of dots (U+2800–U+28FF), so a
/// terminal plot gets 2× horizontal and 4× vertical resolution — with a cell about twice as tall
/// as it is wide, the dots come out roughly square. A cell's color is the last one plotted into it.
/// </summary>
internal sealed class BrailleCanvas
{
    // Dot bit for (column, row) within a cell — the Unicode braille pattern numbering.
    private static readonly int[,] _dotBits = { { 0x01, 0x02, 0x04, 0x40 }, { 0x08, 0x10, 0x20, 0x80 } };

    private readonly int[,] _bits;
    private readonly string?[,] _colors;

    public BrailleCanvas(int widthCells, int heightCells)
    {
        WidthCells = Math.Max(widthCells, 1);
        HeightCells = Math.Max(heightCells, 1);
        _bits = new int[WidthCells, HeightCells];
        _colors = new string?[WidthCells, HeightCells];
    }

    public int WidthCells { get; }

    public int HeightCells { get; }

    public int DotWidth => WidthCells * 2;

    public int DotHeight => HeightCells * 4;

    public void Plot(int x, int y, string? color)
    {
        if (x < 0 || y < 0 || x >= DotWidth || y >= DotHeight)
        {
            return;
        }

        _bits[x / 2, y / 4] |= _dotBits[x % 2, y % 4];
        _colors[x / 2, y / 4] = color;
    }

    /// <summary>A straight line of dots between two points (Bresenham).</summary>
    public void Line(int x0, int y0, int x1, int y1, string? color)
    {
        var dx = Math.Abs(x1 - x0);
        var dy = -Math.Abs(y1 - y0);
        var sx = x0 < x1 ? 1 : -1;
        var sy = y0 < y1 ? 1 : -1;
        var error = dx + dy;
        while (true)
        {
            Plot(x0, y0, color);
            if (x0 == x1 && y0 == y1)
            {
                return;
            }

            var e2 = 2 * error;
            if (e2 >= dy)
            {
                error += dy;
                x0 += sx;
            }

            if (e2 <= dx)
            {
                error += dx;
                y0 += sy;
            }
        }
    }

    /// <summary>Copies every cell that has at least one dot into <paramref name="grid"/> at (<paramref name="left"/>, <paramref name="top"/>); empty cells leave what's already there (axes, labels).</summary>
    public void DrawInto(CellGrid grid, int left, int top)
    {
        for (var y = 0; y < HeightCells; y++)
        {
            for (var x = 0; x < WidthCells; x++)
            {
                if (_bits[x, y] != 0)
                {
                    grid.Set(left + x, top + y, (char)(0x2800 + _bits[x, y]), _colors[x, y]);
                }
            }
        }
    }
}

/// <summary>
/// Character-cell renderings of the live display controls for the TUI: block-character bars, a
/// braille strip chart, and braille vector/coordinate plots. Pure functions of the shared
/// <see cref="LiveDisplayState"/>, so they're tested directly, without a terminal.
/// </summary>
internal static class CellCharts
{
    /// <summary>Muted ink for axes, rings and trails.</summary>
    internal const string Muted = "#8A8984";

    internal const int BarWidth = 30;
    internal const int StripPlotWidth = 40;
    internal const int StripPlotHeight = 8;
    internal const int VectorWidth = 21;
    internal const int VectorHeight = 9;

    private const string _eighths = " ▏▎▍▌▋▊▉█";

    /// <summary>One row per channel: <c>A ▕██████▌        ▏ 42.1 %</c> — the bar in the channel's color, the value in the normal ink.</summary>
    public static CellGrid RenderBarGraph(BarGraphState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var channels = state.Control.Channels;
        var labelWidth = channels.Count == 0 ? 0 : channels.Max(c => (c.Label ?? c.Id).Length);
        var grid = new CellGrid(labelWidth + BarWidth + 16, Math.Max(channels.Count, 1));
        for (var row = 0; row < channels.Count; row++)
        {
            var channel = channels[row];
            var x = grid.Write(0, row, (channel.Label ?? channel.Id).PadRight(labelWidth));
            x = grid.Write(x, row, " ▕", Muted);
            var color = ChartPalette.ColorFor(channel, row);
            var eighths = (int)Math.Round(state.FractionOf(channel.Id) * BarWidth * 8);
            for (var i = 0; i < BarWidth; i++)
            {
                var filled = Math.Clamp(eighths - (i * 8), 0, 8);
                grid.Set(x + i, row, _eighths[filled], color);
            }

            x = grid.Write(x + BarWidth, row, "▏ ", Muted);
            grid.Write(x, row, state.ValueOf(channel.Id) is { } value ? ChartValue.Format(value, state.Control.Unit) : "—");
        }

        return grid;
    }

    /// <summary>
    /// A braille line plot of every channel's history (newest at the right edge), a value axis with
    /// its top/bottom labels on the left, and a legend row naming each channel with its latest value.
    /// </summary>
    public static CellGrid RenderStripChart(StripChartState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var (minimum, maximum) = state.Scale();
        var top = ChartValue.Format(maximum);
        var bottom = ChartValue.Format(minimum);
        var axisWidth = Math.Max(top.Length, bottom.Length) + 2;
        var grid = new CellGrid(axisWidth + StripPlotWidth, StripPlotHeight + 1);

        for (var row = 0; row < StripPlotHeight; row++)
        {
            var label = row == 0 ? top : row == StripPlotHeight - 1 ? bottom : string.Empty;
            grid.Write(0, row, label.PadLeft(axisWidth - 2));
            grid.Set(axisWidth - 1, row, row == 0 || row == StripPlotHeight - 1 ? '┤' : '│', Muted);
        }

        var canvas = new BrailleCanvas(StripPlotWidth, StripPlotHeight);
        var capacity = state.Capacity;
        var span = maximum - minimum;
        for (var index = 0; index < state.Control.Channels.Count; index++)
        {
            var channel = state.Control.Channels[index];
            var color = ChartPalette.ColorFor(channel, index);
            var samples = state.SamplesOf(channel.Id);
            (int X, int Y)? previous = null;
            for (var i = 0; i < samples.Count; i++)
            {
                var slot = capacity - samples.Count + i;
                var x = capacity <= 1 ? canvas.DotWidth - 1 : (int)Math.Round(slot * (canvas.DotWidth - 1) / (double)(capacity - 1));
                var fraction = span <= 0 ? 0.5 : Math.Clamp((samples[i] - minimum) / span, 0, 1);
                var y = (int)Math.Round((1 - fraction) * (canvas.DotHeight - 1));
                if (previous is { } p)
                {
                    canvas.Line(p.X, p.Y, x, y, color);
                }
                else
                {
                    canvas.Plot(x, y, color);
                }

                previous = (x, y);
            }
        }

        canvas.DrawInto(grid, axisWidth, 0);

        var legendX = axisWidth;
        for (var index = 0; index < state.Control.Channels.Count; index++)
        {
            var channel = state.Control.Channels[index];
            var samples = state.SamplesOf(channel.Id);
            legendX = grid.Write(legendX, StripPlotHeight, "■", ChartPalette.ColorFor(channel, index));
            var latest = samples.Count > 0 ? ChartValue.Format(samples[^1], state.Control.Unit) : "—";
            legendX = grid.Write(legendX, StripPlotHeight, $" {channel.Label ?? channel.Id} {latest}   ");
        }

        return grid;
    }

    /// <summary>
    /// A braille plot of the current point (a 2×2 dot block in the live h/s/v color, or the first
    /// palette color) and its fading trail, on crosshair axes — plus a ring at <c>Range</c> for polar
    /// and an oblique z axis for x/y/z — with a readout row below (<c>x=0.5 y=-0.25</c>).
    /// </summary>
    public static CellGrid RenderVector(VectorState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var grid = new CellGrid(VectorWidth + 9, VectorHeight + 1);
        var canvas = new BrailleCanvas(VectorWidth, VectorHeight);
        var centerX = canvas.DotWidth / 2;
        var centerY = canvas.DotHeight / 2;
        var radius = (Math.Min(canvas.DotWidth, canvas.DotHeight) / 2) - 1;
        var control = state.Control;

        // Crosshair axes as box-drawing characters, underneath any dots.
        var midColumn = centerX / 2;
        var midRow = centerY / 4;
        for (var x = 0; x < VectorWidth; x++)
        {
            grid.Set(x, midRow, '─', Muted);
        }

        for (var y = 0; y < VectorHeight; y++)
        {
            grid.Set(midColumn, y, y == midRow ? '┼' : '│', Muted);
        }

        // The point spans ±Range; x/y/z shrinks the x/y extent so the oblique z offset still fits.
        var isXyz = control.Coordinates == CoordinateSystem.XYZ;
        var range = control.Range > 0 ? control.Range : 1;
        var scale = radius / range * (isXyz ? 0.7 : 1);

        (int X, int Y) Project((double X, double Y, double Z) p)
        {
            var zx = isXyz ? p.Z * 0.35 * Math.Cos(Math.PI / 4) : 0;
            var zy = isXyz ? p.Z * 0.35 * Math.Sin(Math.PI / 4) : 0;
            return ((int)Math.Round(centerX + ((p.X + zx) * scale)), (int)Math.Round(centerY - ((p.Y + zy) * scale)));
        }

        if (control.Coordinates == CoordinateSystem.Polar)
        {
            for (var degrees = 0; degrees < 360; degrees += 4)
            {
                var radians = degrees * Math.PI / 180;
                canvas.Plot((int)Math.Round(centerX + (radius * Math.Cos(radians))), (int)Math.Round(centerY - (radius * Math.Sin(radians))), Muted);
            }
        }

        if (isXyz)
        {
            var end = Project((0, 0, range));
            var start = Project((0, 0, -range));
            canvas.Line(start.X, start.Y, end.X, end.Y, Muted);
        }

        foreach (var point in state.Trail)
        {
            var (x, y) = Project(point);
            canvas.Plot(x, y, Muted);
        }

        if (state.HasPoint)
        {
            var color = state.Color is { } c ? ToHex(c) : ChartPalette.Slots[0];
            var (x, y) = Project(state.Point);
            if (control.Coordinates == CoordinateSystem.Polar)
            {
                canvas.Line(centerX, centerY, x, y, color);
            }

            canvas.Plot(x, y, color);
            canvas.Plot(x + 1, y, color);
            canvas.Plot(x, y + 1, color);
            canvas.Plot(x + 1, y + 1, color);
        }

        canvas.DrawInto(grid, 0, 0);
        grid.Write(0, VectorHeight, state.Readout());
        return grid;
    }

    private static string ToHex((byte R, byte G, byte B) color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
