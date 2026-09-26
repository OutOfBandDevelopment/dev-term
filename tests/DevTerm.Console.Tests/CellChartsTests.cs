using DevTerm.Test.Utilities;
using DevTerm.UiDefinitions;

namespace DevTerm.Console.Tests;

/// <summary>
/// The TUI's character-cell chart renderings (<see cref="CellCharts"/>) — block-character bars, a
/// braille strip chart, braille vector plots — checked as plain text, without a terminal.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class CellChartsTests
{
    private static bool IsBraille(char c) => c is >= '⠁' and <= '⣿';

    [TestMethod]
    public void BarGraph_FillsEachBarInProportion_AndShowsTheValue()
    {
        var state = new BarGraphState(new BarGraphControl
        {
            Id = "levels",
            Label = "Levels",
            Maximum = 100,
            Unit = "%",
            Channels = [new ChartChannel { Id = "a", Label = "A" }, new ChartChannel { Id = "bb", Label = "BB" }, new ChartChannel { Id = "c", Label = "C" }],
        });
        state.ApplyAll(new Dictionary<string, string> { ["a"] = "50", ["bb"] = "100" });

        var grid = CellCharts.RenderBarGraph(state);
        var lines = grid.Lines();

        Assert.HasCount(3, lines);
        Assert.StartsWith("A  ▕", lines[0], "Labels are padded to the longest.");
        Assert.AreEqual(CellCharts.BarWidth / 2, lines[0].Count(c => c == '█'), "Half full.");
        Assert.Contains("50 %", lines[0]);
        Assert.AreEqual(CellCharts.BarWidth, lines[1].Count(c => c == '█'));
        Assert.AreEqual(0, lines[2].Count(c => c == '█'));
        Assert.Contains("—", lines[2], "No value yet.");

        var barCell = grid[4, 0];
        Assert.AreEqual(ChartPalette.Slots[0], barCell.Color, "Each bar is drawn in its channel's palette color.");
        Assert.AreEqual(ChartPalette.Slots[1], grid[4, 1].Color);
    }

    [TestMethod]
    public void BarGraph_UsesEighthBlocksForAPartialCell()
    {
        var state = new BarGraphState(new BarGraphControl { Id = "b", Label = "B", Maximum = CellCharts.BarWidth * 8, Channels = [new ChartChannel { Id = "a" }] });
        state.Apply("a", "4");

        Assert.Contains("▌", CellCharts.RenderBarGraph(state).Lines()[0], "4/8 of one cell is a half block.");
    }

    [TestMethod]
    public void StripChart_PlotsBrailleTraces_WithAxisLabelsAndALegend()
    {
        var state = new StripChartState(new StripChartControl
        {
            Id = "s",
            Label = "S",
            Minimum = 0,
            Maximum = 100,
            HistoryLength = 10,
            Channels = [new ChartChannel { Id = "a", Label = "A" }, new ChartChannel { Id = "b", Label = "B" }],
        });
        for (var i = 0; i < 10; i++)
        {
            state.ApplyAll(new Dictionary<string, string> { ["a"] = (i * 10).ToString(System.Globalization.CultureInfo.InvariantCulture), ["b"] = "50" });
        }

        var grid = CellCharts.RenderStripChart(state);
        var lines = grid.Lines();

        Assert.HasCount(CellCharts.StripPlotHeight + 1, lines);
        Assert.StartsWith("100 ┤", lines[0]);
        Assert.StartsWith("  0 ┤", lines[CellCharts.StripPlotHeight - 1]);
        Assert.IsTrue(lines.Take(CellCharts.StripPlotHeight).Any(l => l.Any(IsBraille)), "The traces are braille dots.");
        Assert.Contains("A 90", lines[^1], "The legend shows each channel's latest value.");
        Assert.Contains("B 50", lines[^1]);

        // The rising trace A ends at the top right; the flat trace B sits on the middle rows.
        var topRight = lines[0][^1];
        Assert.IsTrue(IsBraille(topRight), $"Expected the newest (highest) sample at the top-right, got '{topRight}'.");
    }

    [TestMethod]
    public void Vector_XY_PlotsThePointInItsQuadrant_WithAReadout()
    {
        var state = new VectorState(new VectorControl { Id = "v", Label = "V", XId = "x", YId = "y", Range = 1 });
        state.ApplyAll(new Dictionary<string, string> { ["x"] = "0.8", ["y"] = "0.8" });

        var lines = CellCharts.RenderVector(state).Lines();
        var midColumn = CellCharts.VectorWidth / 2;

        Assert.StartsWith("x=0.8 y=0.8", lines[^1]);
        Assert.Contains('┼', lines[CellCharts.VectorHeight / 2]);
        var dotRows = Enumerable.Range(0, CellCharts.VectorHeight).Where(r => lines[r].Skip(midColumn + 1).Take(CellCharts.VectorWidth - midColumn - 1).Any(IsBraille)).ToList();
        Assert.IsNotEmpty(dotRows, "The point is right of the y axis.");
        Assert.IsTrue(dotRows.All(r => r < CellCharts.VectorHeight / 2), "...and above the x axis.");
    }

    [TestMethod]
    public void Vector_Polar_DrawsARing_AndTheReadoutInDegrees()
    {
        var state = new VectorState(new VectorControl { Id = "p", Label = "P", Coordinates = CoordinateSystem.Polar, RadiusId = "r", AngleId = "t" });
        state.ApplyAll(new Dictionary<string, string> { ["r"] = "0.5", ["t"] = "90" });

        var grid = CellCharts.RenderVector(state);

        Assert.StartsWith("r=0.5 θ=90°", grid.Lines()[^1]);
        Assert.IsTrue(Enumerable.Range(0, 4).Any(x => IsBraille(grid[x, CellCharts.VectorHeight / 2].Char)), "The outer ring crosses the x axis near the left edge.");
    }
}
