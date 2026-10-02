using System.Text;
using DevTerm.Core.StreamContent;
using DevTerm.Test.Utilities;

namespace DevTerm.Core.Tests.StreamContent;

[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class HpglToSvgConverterTests
{
    private static byte[] Hpgl(string instructions) => Encoding.ASCII.GetBytes(instructions);

    [TestMethod]
    public void ConvertToSvg_EmptyInput_ProducesAnEmptyButValidSvg()
    {
        var svg = HpglToSvgConverter.ConvertToSvg([]);

        Assert.IsTrue(svg.Contains("<svg", StringComparison.Ordinal));
        Assert.IsFalse(svg.Contains("<path", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ConvertToSvg_SinglePenDownRun_ProducesOnePathWithEverySegment()
    {
        var svg = HpglToSvgConverter.ConvertToSvg(Hpgl("IN;PU0,0;PD100,0,100,100,0,100;PU;"));

        var pathCount = svg.Split("<path").Length - 1;
        Assert.AreEqual(1, pathCount);

        // Starts at the PU position, then draws through every PD coordinate pair - 4 points, so 3 line segments (one M, three L).
        Assert.AreEqual(1, svg.Split("d=\"M ").Length - 1);
        Assert.AreEqual(3, svg.Split(" L ").Length - 1);
    }

    [TestMethod]
    public void ConvertToSvg_ConsecutivePenDownCommandsWithNoInterveningPenUp_ChainIntoOneContinuousPath()
    {
        // Two separate PD commands, no PU between them - must still be one connected polyline, not two 1-segment paths.
        var svg = HpglToSvgConverter.ConvertToSvg(Hpgl("IN;PU0,0;PD50,50;PD100,0;PU;"));

        var pathCount = svg.Split("<path").Length - 1;
        Assert.AreEqual(1, pathCount);
        Assert.AreEqual(2, svg.Split(" L ").Length - 1);
    }

    [TestMethod]
    public void ConvertToSvg_PenUpWithNoFollowingPenDown_DrawsNothing()
    {
        var svg = HpglToSvgConverter.ConvertToSvg(Hpgl("IN;PU0,0;PU100,100;PU200,200;"));

        Assert.IsFalse(svg.Contains("<path", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ConvertToSvg_SelectPenMidPath_ColorsThePathByThePenAtStart_NotAtFlush()
    {
        // SP2 takes effect, a path starts under pen 2, then SP3 changes the pen before the path ends -
        // the whole path must still use pen 2's color (captured at start), not pen 3's.
        var withMidPathPenChange = HpglToSvgConverter.ConvertToSvg(Hpgl("IN;SP2;PU0,0;PD50,50;SP3;PD100,0;PU;"));
        var pen2Only = HpglToSvgConverter.ConvertToSvg(Hpgl("IN;SP2;PU0,0;PD50,50;PD100,0;PU;"));

        var colorStart = withMidPathPenChange.IndexOf("stroke=\"", StringComparison.Ordinal);
        var colorEnd = withMidPathPenChange.IndexOf('"', colorStart + 8);
        var actualColor = withMidPathPenChange[(colorStart + 8)..colorEnd];

        var expectedStart = pen2Only.IndexOf("stroke=\"", StringComparison.Ordinal);
        var expectedEnd = pen2Only.IndexOf('"', expectedStart + 8);
        var expectedColor = pen2Only[(expectedStart + 8)..expectedEnd];

        Assert.AreEqual(expectedColor, actualColor);
    }

    [TestMethod]
    public void ConvertToSvg_RelativeMode_AccumulatesFromTheCurrentPosition()
    {
        var absolute = HpglToSvgConverter.ConvertToSvg(Hpgl("IN;PU0,0;PD50,0,50,50;PU;"));
        var relative = HpglToSvgConverter.ConvertToSvg(Hpgl("IN;PU0,0;PR;PD50,0,0,50;PU;"));

        Assert.AreEqual(absolute, relative);
    }

    [TestMethod]
    public void ConvertToSvg_FlipsTheYAxis()
    {
        // HP-GL Y increases upward; SVG Y increases downward, so a point with a larger HP-GL Y must
        // render with a *smaller* SVG y than a point with a smaller HP-GL Y.
        var svg = HpglToSvgConverter.ConvertToSvg(Hpgl("IN;PU0,0;PD0,100;PU;"));

        var dStart = svg.IndexOf("d=\"", StringComparison.Ordinal) + 3;
        var dEnd = svg.IndexOf('"', dStart);
        var d = svg[dStart..dEnd];
        var tokens = d.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // tokens: ["M", "x1,y1", "L", "x2,y2"]
        var startY = double.Parse(tokens[1].Split(',')[1], System.Globalization.CultureInfo.InvariantCulture);
        var endY = double.Parse(tokens[3].Split(',')[1], System.Globalization.CultureInfo.InvariantCulture);

        Assert.IsTrue(endY < startY);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("garbage not hpgl at all")]
    [DataRow("IN;PU;PD;SP;PA;PR;")]
    [DataRow("PD1,2,3")]
    public void ConvertToSvg_MalformedOrIncompleteInput_NeverThrows(string instructions) =>
        HpglToSvgConverter.ConvertToSvg(Hpgl(instructions));
}
