using DevTerm.DeviceManifests;
using DevTerm.Test.Utilities;

namespace DevTerm.Devices.Scpi.Tests;

[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class ScpiValuePathTests
{
    [TestMethod]
    public void QueryCommands_PublishTheirReplyId_AndParametersTheirFieldId()
    {
        var profile = new ScpiInstrumentProfile
        {
            Name = "Test",
            Commands =
            [
                new ScpiCommandDefinition { Id = "idn", Label = "Identify", Template = "*IDN?", IsQuery = true },
                new ScpiCommandDefinition
                {
                    Id = "freq",
                    Label = "Frequency",
                    Template = "FREQ {Hz}",
                    Parameters = [new ScpiParameterDefinition { Name = "Hz", Kind = ScpiParameterKind.Numeric, Minimum = 1, Maximum = 1000, Unit = "Hz" }],
                },
            ],
        };

        var paths = ValuePathCatalog.Enumerate(ScpiUiDefinitionBuilder.Build(profile));

        Assert.IsTrue(paths.Any(p => p.Path == "idn.reply"));
        var hz = paths.Single(p => p.Path == "freq.Hz");
        Assert.AreEqual(ValuePathType.Number, hz.Type);
        Assert.AreEqual(1000d, hz.Maximum);
        Assert.AreEqual("Hz", hz.Unit);
    }

    [TestMethod]
    public void EveryBundledProfile_YieldsItsQueryReplies()
    {
        foreach (var profile in ScpiProfileCatalog.All)
        {
            var paths = ValuePathCatalog.Enumerate(ScpiUiDefinitionBuilder.Build(profile)).Select(p => p.Path).ToHashSet();
            foreach (var query in profile.Commands.Where(c => c.IsQuery))
            {
                Assert.IsTrue(paths.Contains($"{query.Id}.reply"), $"{profile.Name}: {query.Id}");
            }
        }
    }
}
