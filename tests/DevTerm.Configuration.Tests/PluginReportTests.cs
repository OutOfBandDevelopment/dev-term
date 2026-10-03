using DevTerm.Core.Plugins;
using DevTerm.Test.Utilities;

namespace DevTerm.Configuration.Tests;

[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class PluginReportTests
{
    [TestMethod]
    public void Lines_NoResults_SaysNoPluginsFound()
    {
        Assert.AreSequenceEqual(["No plugins found."], PluginReport.Lines([]));
        Assert.AreSequenceEqual(["No plugins found."], PluginReport.Lines(null));
    }

    [TestMethod]
    public void Lines_LoadedAndSkipped_DescribesEachWithItsFolder()
    {
        var lines = PluginReport.Lines(
        [
            new PluginLoadResult(@"C:\p\a", "alpha", true, string.Empty),
            new PluginLoadResult(@"C:\p\b", "beta", false, "no plugin.json"),
        ]);

        Assert.AreSequenceEqual([@"alpha  loaded  (C:\p\a)", @"beta  skipped: no plugin.json  (C:\p\b)"], lines);
    }
}
