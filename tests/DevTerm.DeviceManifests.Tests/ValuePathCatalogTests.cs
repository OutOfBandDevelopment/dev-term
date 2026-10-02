using DevTerm.Test.Utilities;
using DevTerm.UiDefinitions;

namespace DevTerm.DeviceManifests.Tests;

[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class ValuePathCatalogTests
{
    private static DeviceManifest Manifest(
        List<ResponsePattern>? patterns = null,
        List<OutboundCommand>? commands = null,
        List<UiControl>? controls = null) => new()
    {
        Name = "Test",
        Inbound = new InboundProtocol { Patterns = patterns ?? [] },
        OutboundCommands = commands ?? [],
        Ui = new UiDefinition { Name = "Panel", Sections = [new UiSection { Controls = controls ?? [] }] },
    };

    [TestMethod]
    public void NamedGroups_ArePublishedAlongsideThePatternName()
    {
        var manifest = Manifest(patterns: [new ResponsePattern { Name = "reading", Match = @"^(?<volts>-?\d+\.\d+)V,(?<mode>[A-Z]+)$" }]);

        var paths = ValuePathCatalog.Enumerate(manifest);

        CollectionAssert.AreEqual(new[] { "reading", "volts", "mode" }, paths.Select(p => p.Path).ToArray());
        Assert.AreEqual(ValuePathType.Number, paths.Single(p => p.Path == "volts").Type);
        Assert.AreEqual(ValuePathType.Text, paths.Single(p => p.Path == "mode").Type);
    }

    [TestMethod]
    public void PatternWithAnUnnamedGroup_TakesItsTypeFromThatGroup()
    {
        var paths = ValuePathCatalog.FromPattern(new ResponsePattern { Name = "temp", Match = @"^T=(\d+)C$" });

        Assert.AreEqual(1, paths.Count);
        Assert.AreEqual(ValuePathType.Number, paths[0].Type);
    }

    [TestMethod]
    public void NonCapturingAndLookaroundGroups_AreNotPaths()
    {
        var paths = ValuePathCatalog.FromPattern(new ResponsePattern { Name = "x", Match = @"^(?:A|B)(?=\d)(?<n>\d+)$" });

        CollectionAssert.AreEqual(new[] { "x", "n" }, paths.Select(p => p.Path).ToArray());
    }

    [TestMethod]
    public void Query_PublishesItsReplyId_AndAnExplicitReplyIdWins()
    {
        var manifest = Manifest(commands:
        [
            new OutboundCommand { Name = "Get Voltage", Template = "VOUT1?", IsQuery = true },
            new OutboundCommand { Name = "Get Current", Template = "IOUT1?", IsQuery = true, ReplyId = "amps" },
            new OutboundCommand { Name = "Set Voltage", Template = "VSET1:{value}" },
        ]);

        CollectionAssert.AreEqual(new[] { "Get Voltage.reply", "amps" }, ValuePathCatalog.Enumerate(manifest).Select(p => p.Path).ToArray());
    }

    [TestMethod]
    public void Controls_CarryTheirRangeUnitAndChoices()
    {
        var manifest = Manifest(controls:
        [
            new SliderControl { Id = "vset", Label = "Voltage", Minimum = 0, Maximum = 30, Unit = "V" },
            new ToggleControl { Id = "out", Label = "Output" },
            new ChoiceControl { Id = "range", Label = "Range", Options = ["low", "high"] },
        ]);

        var paths = ValuePathCatalog.Enumerate(manifest);

        var slider = paths.Single(p => p.Path == "vset");
        Assert.AreEqual(ValuePathType.Number, slider.Type);
        Assert.AreEqual(30d, slider.Maximum);
        Assert.AreEqual("V", slider.Unit);
        Assert.AreEqual(ValuePathType.Boolean, paths.Single(p => p.Path == "out").Type);
        CollectionAssert.AreEqual(new[] { "low", "high" }, paths.Single(p => p.Path == "range").Choices!.ToArray());
    }

    [TestMethod]
    public void DuplicatePaths_KeepTheFirstSource()
    {
        var manifest = Manifest(
            patterns: [new ResponsePattern { Name = "vset", Match = @"^(\d+)$" }],
            controls: [new SliderControl { Id = "vset", Label = "Voltage", Maximum = 30 }]);

        var paths = ValuePathCatalog.Enumerate(manifest);

        Assert.AreEqual(1, paths.Count);
        Assert.AreEqual(ValuePathSource.ResponsePattern, paths[0].Source);
    }

    [TestMethod]
    public void Validator_WarnsOnAnExpressionReadingAnIdNothingPublishes()
    {
        var manifest = Manifest(
            patterns: [new ResponsePattern { Name = "reading", Match = @"^(\d+)$" }],
            controls: [new IndicatorControl { Id = "scaled", Label = "Scaled", Expression = "{reading} * {raed}" }]);

        var result = DeviceManifestValidator.Validate(manifest);

        Assert.IsTrue(result.IsValid);
        Assert.AreEqual(1, result.Warnings.Count(w => w.Contains("{raed}", StringComparison.Ordinal)));
        Assert.IsFalse(result.Warnings.Any(w => w.Contains("{reading}", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void BundledManifests_ReadOnlyIdsTheyPublish()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "manifests");
        Assert.IsTrue(Directory.Exists(root), "the bundled manifests folder should be copied next to the tests");

        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var manifest = DeviceManifestLoader.Load(directory);
            var unknown = DeviceManifestValidator.Validate(manifest).Warnings.Where(w => w.Contains("nothing in the manifest publishes", StringComparison.Ordinal));
            Assert.AreEqual(0, unknown.Count(), $"{manifest.Name}: {string.Join(" ", unknown)}");
        }
    }
}
