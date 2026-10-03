using DevTerm.Test.Utilities;
using DevTerm.UiDefinitions;

namespace DevTerm.Console.Tests;

[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class StripChartExportTests
{
    [TestMethod]
    public void SaveHistoryCsv_WritesTheHistoryIntoTheExportFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "devterm-export-" + Guid.NewGuid().ToString("N"));
        var original = ControlPanelMode.ExportFolder;
        ControlPanelMode.ExportFolder = () => folder;
        try
        {
            var state = new StripChartState(new StripChartControl { Id = "chart", Label = "Chart", Channels = [new ChartChannel { Id = "a" }] });
            state.Apply("a", "1.5");

            var path = ControlPanelMode.SaveHistoryCsv(state);

            Assert.AreEqual(folder, Path.GetDirectoryName(path));
            Assert.AreEqual("Age,a\r\n0,1.5\r\n", File.ReadAllText(path));
        }
        finally
        {
            ControlPanelMode.ExportFolder = original;
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}
