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
        var hint = ManifestPanelHint.For(new CliOptions { ManifestName = "loopback-sensor-demo" }, TempStore());

        Assert.IsNotNull(hint);
        Assert.Contains("loopback-sensor-demo", hint);
        Assert.Contains("Device Manifest", hint);
    }

    [TestMethod]
    public void MarkUsed_StopsTheHint_AndSurvivesAReload_ForThatManifestOnly()
    {
        var store = TempStore();
        var options = new CliOptions { ManifestName = "loopback-sensor-demo" };
        Assert.IsNotNull(ManifestPanelHint.For(options, store));

        ManifestPanelHint.MarkUsed("Loopback-Sensor-Demo", store);
        ManifestPanelHint.MarkUsed("loopback-sensor-demo", store);

        Assert.IsNull(ManifestPanelHint.For(options, new AppPreferencesStore(store.Path)));
        Assert.HasCount(1, store.Load().UsedPanelHints);
    }

    private static AppPreferencesStore TempStore() =>
        new(Path.Combine(Path.GetTempPath(), "devterm-hint-" + Guid.NewGuid().ToString("N"), "preferences.json"));
}
