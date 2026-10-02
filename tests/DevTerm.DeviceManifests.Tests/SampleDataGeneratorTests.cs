using DevTerm.Test.Utilities;
using DevTerm.UiDefinitions;

namespace DevTerm.DeviceManifests.Tests;

[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class SampleDataGeneratorTests
{
    private static ValuePath Slider(string id, double min, double max) =>
        new(id, ValuePathType.Number, ValuePathSource.Control, id, Minimum: min, Maximum: max);

    [TestMethod]
    public void NumericValues_StayInsideTheDeclaredRange_AndVaryOverTime()
    {
        var path = Slider("vset", 0, 30);

        var series = Enumerable.Range(0, 60).Select(step => SampleDataGenerator.Value(path, 7, step)!.Value).ToList();

        Assert.IsTrue(series.All(v => v is >= 0 and <= 30));
        Assert.IsTrue(series.Max() - series.Min() > 10, "a walk, not a constant");
    }

    [TestMethod]
    public void SameSeedAndStep_GiveTheSameValues_DifferentSeedsDiffer()
    {
        var paths = new[] { Slider("a", 0, 10), Slider("b", -5, 5) };

        CollectionAssert.AreEqual(
            SampleDataGenerator.Values(paths, 3, 4).OrderBy(p => p.Key).ToList(),
            SampleDataGenerator.Values(paths, 3, 4).OrderBy(p => p.Key).ToList());
        Assert.AreNotEqual(SampleDataGenerator.Value(paths[0], 1, 5), SampleDataGenerator.Value(paths[0], 2, 5));
    }

    [TestMethod]
    public void TextPathsAreLeftOut_ChoicesCycleAndBooleansAlternate()
    {
        var paths = new[]
        {
            new ValuePath("label", ValuePathType.Text, ValuePathSource.ResponsePattern, "p"),
            new ValuePath("range", ValuePathType.Text, ValuePathSource.Control, "r", Choices: ["low", "high"]),
            new ValuePath("out", ValuePathType.Boolean, ValuePathSource.Control, "o"),
        };

        var step0 = SampleDataGenerator.Values(paths, 0, 0);
        var step1 = SampleDataGenerator.Values(paths, 0, 1);

        Assert.IsFalse(step0.ContainsKey("label"));
        Assert.AreEqual(0d, step0["range"]);
        Assert.AreEqual(1d, step1["range"]);
        Assert.AreNotEqual(step0["out"], step1["out"]);
        Assert.AreEqual("high", SampleDataGenerator.Text(paths[1], 1));
    }

    [TestMethod]
    public void ExpressionOverSampleData_EvaluatesToARealResult()
    {
        var manifest = new DeviceManifest
        {
            Name = "T",
            Ui = new UiDefinition { Name = "P", Sections = [new UiSection { Controls = [new SliderControl { Id = "volts", Label = "V", Minimum = 0, Maximum = 30 }] }] },
        };
        Assert.IsTrue(Expression.TryParse("{volts} * 1000", out var expression, out _));

        var values = SampleDataGenerator.Values(ValuePathCatalog.Enumerate(manifest), 7, 3);

        var result = expression!.Evaluate(values);
        Assert.IsTrue(result is >= 0 and <= 30000, result.ToString());
    }

    [TestMethod]
    public void DeclaredExample_StartsTheSeries_AndTextKeepsIt()
    {
        var pattern = new ResponsePattern { Name = "reading", Match = @"^MEAS (?<volts>-?[\d.]+) V (?<state>\w+)$", Example = "MEAS 12.5 V OK" };
        var paths = ValuePathCatalog.FromPattern(pattern);
        var volts = paths.Single(p => p.Path == "volts");
        var state = paths.Single(p => p.Path == "state");

        Assert.AreEqual("12.5", volts.Example);
        Assert.AreEqual(12.5, SampleDataGenerator.Value(volts, 3, 0));
        var later = Enumerable.Range(1, 40).Select(step => SampleDataGenerator.Value(volts, 3, step)!.Value).ToList();
        Assert.IsTrue(later.All(v => Math.Abs(v - 12.5) <= 0.7), "wanders only a few percent");
        Assert.IsTrue(later.Distinct().Count() > 1);
        Assert.AreEqual("OK", SampleDataGenerator.TextValues(paths.Where(p => p.Type == ValuePathType.Text), 3, 9)["state"]);
    }

    [TestMethod]
    public void ExampleThatDoesNotMatch_IsIgnored()
    {
        var pattern = new ResponsePattern { Name = "reading", Match = @"^MEAS (?<volts>[\d.]+)$", Example = "nope" };

        Assert.IsTrue(ValuePathCatalog.FromPattern(pattern).All(p => p.Example is null));
    }
}
