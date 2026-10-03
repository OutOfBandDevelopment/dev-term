using DevTerm.Test.Utilities;

namespace DevTerm.UiDefinitions.Tests;

[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class StripChartHistoryTests
{
    private static StripChartState Chart()
    {
        var state = new StripChartState(new StripChartControl
        {
            Id = "s",
            Label = "S",
            HistoryLength = 5,
            Unit = "V",
            Channels = [new ChartChannel { Id = "a", Label = "Volts, in" }, new ChartChannel { Id = "b" }],
        });
        state.ApplyAll([new("a", "1"), new("b", "10")]);
        state.ApplyAll([new("a", "2")]);
        state.ApplyAll([new("a", "3"), new("b", "30")]);
        return state;
    }

    [TestMethod]
    public void Rows_AreOldestFirst_WithAgeCountingBackFromTheNewest_AndBlankWhereAChannelHasNone()
    {
        var rows = StripChartHistory.Rows(Chart());

        Assert.AreEqual(3, rows.Count);
        Assert.AreSequenceEqual(["2", "1", ""], [.. rows[0]]);
        Assert.AreSequenceEqual(["1", "2", "10"], [.. rows[1]]);
        Assert.AreSequenceEqual(["0", "3", "30"], [.. rows[2]]);
    }

    [TestMethod]
    public void ToCsv_QuotesALabelThatNeedsIt_AndUsesCrLf()
    {
        var csv = StripChartHistory.ToCsv(Chart());

        Assert.IsTrue(csv.StartsWith("Age,\"Volts, in\",b\r\n", StringComparison.Ordinal));
        Assert.IsTrue(csv.EndsWith("0,3,30\r\n", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ReadoutAt_NamesTheAge_AndNullBeforeTheFirstSample()
    {
        var state = Chart();

        Assert.IsNull(StripChartHistory.ReadoutAt(state, 0));
        StringAssert.StartsWith(StripChartHistory.ReadoutAt(state, 4), "now: ");
        StringAssert.StartsWith(StripChartHistory.ReadoutAt(state, 3), "-1: ");
        Assert.IsNull(StripChartHistory.ReadoutAt(state, 5));
    }
}
