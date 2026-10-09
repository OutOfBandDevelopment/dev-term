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

    [TestMethod]
    public void ApprovalLines_NoneRemembered_SaysSo()
    {
        Assert.AreSequenceEqual(["No plugin approvals are remembered."], PluginReport.ApprovalLines([]));
    }

    [TestMethod]
    public void ApprovalLines_ShowNameShortHashAndDate()
    {
        var when = new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);
        var lines = PluginReport.ApprovalLines([new PluginApproval("shout", "0123456789ABCDEF0123", when), new PluginApproval("tiny", "AB", when)]);

        Assert.StartsWith("shout  0123456789AB  approved 2026-10-", lines[0]);
        Assert.StartsWith("tiny  AB  approved 2026-10-", lines[1]);
    }
}
