using DevTerm.Test.Utilities;

namespace DevTerm.Configuration.Tests;

[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class ManifestPanelHintTests
{
    [TestMethod]
    public void For_NoManifestName_ReturnsNull() => Assert.IsNull(ManifestPanelHint.For(new CliOptions()));

    [TestMethod]
    public void For_ManifestThatDoesNotResolve_ReturnsNull_TheWarningsJob() =>
        Assert.IsNull(ManifestPanelHint.For(new CliOptions { ManifestName = "panel-hint-test-" + Guid.NewGuid().ToString("N") }));

    [TestMethod]
    public void For_TheBundledDemoManifest_OffersItsPanel()
    {
        var hint = ManifestPanelHint.For(new CliOptions { ManifestName = "loopback-sensor-demo" });

        Assert.IsNotNull(hint);
        Assert.Contains("loopback-sensor-demo", hint);
        Assert.Contains("Device Manifest", hint);
    }
}
