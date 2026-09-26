using DevTerm.UiDefinitions;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;

namespace DevTerm.Console;

/// <summary>
/// Draws a live display control (bar graph, strip chart, vector plot) in the TUI control panel: its
/// <see cref="LiveDisplayState"/> is fed the structured presenter's values, and each redraw renders
/// it through <see cref="CellCharts"/> and paints the resulting <see cref="CellGrid"/> cell by cell,
/// each in its own color — one <c>Label</c> can only have one color, and a chart needs one per
/// channel. Fixed-size: the size of what <see cref="Render"/> produces.
/// </summary>
internal sealed class CellCanvasView : View
{
    private readonly Func<CellGrid> _render;

    public CellCanvasView(LiveDisplayState state, Func<CellGrid> render)
    {
        State = state;
        _render = render;
        var initial = render();
        Width = initial.Width;
        Height = initial.Height;
        CanFocus = false;
    }

    public LiveDisplayState State { get; }

    /// <summary>The chart as it would draw right now — for tests, and for <see cref="OnDrawingContent"/>.</summary>
    public CellGrid Render() => _render();

    /// <summary>Applies one <c>ValuesChanged</c> batch; redraws only when it changed something this chart plots.</summary>
    public void Apply(IReadOnlyDictionary<string, string> values)
    {
        if (State.ApplyAll(values))
        {
            SetNeedsDraw();
        }
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var grid = Render();
        var normal = GetAttributeForRole(VisualRole.Normal);
        for (var y = 0; y < grid.Height && y < Viewport.Height; y++)
        {
            for (var x = 0; x < grid.Width && x < Viewport.Width; x++)
            {
                var cell = grid[x, y];
                var foreground = cell.Color is { } hex && ChartPalette.TryParseHex(hex, out var rgb)
                    ? new Color(rgb.R, rgb.G, rgb.B, 255)
                    : normal.Foreground;
                SetAttribute(new Terminal.Gui.Drawing.Attribute(foreground, normal.Background));
                Move(x, y);
                AddStr(cell.Char.ToString());
            }
        }

        return true;
    }
}
