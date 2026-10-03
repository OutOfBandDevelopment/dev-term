using DevTerm.Test.Utilities;
using DevTerm.UiDefinitions;

namespace DevTerm.Wpf.Tests;

[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class StripChartElementTests
{
    [TestMethod]
    public void Hover_ReadsTheSampleUnderThePointer_AndNothingOutsideThePlot()
    {
        StaTestRunner.Run(() =>
        {
            var state = new StripChartState(new StripChartControl
            {
                Id = "s",
                Label = "S",
                HistoryLength = 3,
                Unit = "V",
                Channels = [new ChartChannel { Id = "a", Label = "A" }],
            });
            state.Apply("a", "1");
            state.Apply("a", "2");
            state.Apply("a", "3");
            var element = new StripChartElement(state);

            // The plot spans x 44..364: the left edge is the oldest of 3 slots, the right edge the newest.
            StringAssert.StartsWith(element.ReadoutAt(new System.Windows.Point(45, 50)), "-2: A ");
            StringAssert.StartsWith(element.ReadoutAt(new System.Windows.Point(363, 50)), "now: A ");
            Assert.IsNull(element.ReadoutAt(new System.Windows.Point(10, 50)));
            Assert.AreEqual(3, element.ContextMenu.Items.Count);
            return Task.CompletedTask;
        });
    }
}
