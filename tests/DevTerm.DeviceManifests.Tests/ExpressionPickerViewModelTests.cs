using DevTerm.DeviceManifests.Editing;
using DevTerm.Test.Utilities;

namespace DevTerm.DeviceManifests.Tests;

[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class ExpressionPickerViewModelTests
{
    private static readonly ValuePath _voltage = new("volts", ValuePathType.Number, ValuePathSource.Control, "Voltage", "V", 0, 30);
    private static readonly ValuePath _current = new("amps", ValuePathType.Number, ValuePathSource.Control, "Current", "A", 0, 5);
    private static readonly ValuePath _mode = new("mode", ValuePathType.Text, ValuePathSource.Control, "Mode", Choices: ["CC", "CV"]);
    private static readonly ValuePath _banner = new("banner", ValuePathType.Text, ValuePathSource.ResponsePattern, "id");

    private static ExpressionPickerViewModel Create(string? text = null) =>
        new([_voltage, _current, _mode, _banner], text, seed: 7);

    [TestMethod]
    public void Empty_IsNeitherValidNorAnError()
    {
        var picker = Create();

        Assert.IsTrue(picker.IsEmpty);
        Assert.IsFalse(picker.IsValid);
        Assert.IsNull(picker.Error);
        Assert.IsNull(picker.Result);
        Assert.AreEqual("Empty", picker.Diagnostics);
        Assert.AreEqual("-", picker.ResultText);
    }

    [TestMethod]
    public void ValidExpression_EvaluatesAgainstTheSampleData()
    {
        var picker = Create("{volts} * {amps}");

        Assert.IsTrue(picker.IsValid);
        Assert.AreEqual("OK", picker.Diagnostics);
        var values = picker.SampleValues;
        Assert.AreEqual(values["volts"] * values["amps"], picker.Result!.Value, 1e-9);
    }

    [TestMethod]
    public void SyntaxError_ReportsTheParserMessage_AndNoResult()
    {
        var picker = Create("{volts} *");

        Assert.IsFalse(picker.IsValid);
        Assert.IsFalse(string.IsNullOrEmpty(picker.Error));
        Assert.IsNull(picker.Result);
        StringAssert.StartsWith(picker.Diagnostics, "Error: ");
    }

    [TestMethod]
    public void UnknownId_AndTextPath_WarnWithoutBlocking()
    {
        var picker = Create("{volts} + {nothing} + {banner} + {mode}");

        Assert.IsTrue(picker.IsValid);
        Assert.HasCount(2, picker.Warnings, "an unknown id and a free-text path, but not a choice");
        Assert.IsTrue(picker.Warnings.Any(w => w.Contains("'nothing'", StringComparison.Ordinal)));
        Assert.IsTrue(picker.Warnings.Any(w => w.Contains("'banner'", StringComparison.Ordinal)));
        StringAssert.StartsWith(picker.Diagnostics, "OK, with warnings");
    }

    [TestMethod]
    public void InsertPath_AtCaret_PutsTheReferenceThereAndMovesPastIt()
    {
        var picker = Create("1 + 2");
        picker.CaretIndex = 4;
        var amps = picker.AllPaths.Single(p => p.Path.Path == "amps");

        picker.InsertPath(amps);

        Assert.AreEqual("1 + {amps}2", picker.Text);
        Assert.AreEqual(4 + "{amps}".Length, picker.CaretIndex);
    }

    [TestMethod]
    public void InsertPath_ReplacesTheSelection()
    {
        var picker = Create("{volts} + XX");
        picker.CaretIndex = 10;
        picker.SelectionLength = 2;

        picker.InsertPath(picker.AllPaths.Single(p => p.Path.Path == "amps"));

        Assert.AreEqual("{volts} + {amps}", picker.Text);
        Assert.IsTrue(picker.IsValid);
    }

    [TestMethod]
    public void InsertFunction_LeavesTheCaretBetweenTheParentheses()
    {
        var picker = Create();

        picker.InsertFunction(ExpressionPickerViewModel.Functions.Single(f => f.Name == "round"));

        Assert.AreEqual("round()", picker.Text);
        Assert.AreEqual(6, picker.CaretIndex);

        picker.InsertPath(picker.AllPaths.Single(p => p.Path.Path == "volts"));
        Assert.AreEqual("round({volts})", picker.Text);
        Assert.IsTrue(picker.IsValid);
    }

    [TestMethod]
    public void InsertOperator_SpacesBinaryOperators_ButNotParentheses()
    {
        var picker = Create("{volts}");

        picker.InsertOperator("*");
        picker.InsertOperator("(");

        Assert.AreEqual("{volts} * (", picker.Text);
    }

    [TestMethod]
    public void EveryOfferedFunction_ParsesOnceItsArgumentsAreFilledIn()
    {
        var snippets = new Dictionary<string, string>
        {
            ["round"] = "round(1.26, 1)",
            ["min"] = "min(1, 2)",
            ["max"] = "max(1, 2)",
            ["abs"] = "abs(-1)",
            ["if"] = "if(1, 2, 3)",
        };

        foreach (var function in ExpressionPickerViewModel.Functions)
        {
            Assert.IsTrue(Create(snippets[function.Name]).IsValid, function.Name);
        }

        Assert.AreEqual(snippets.Count, ExpressionPickerViewModel.Functions.Count);
    }

    [TestMethod]
    public void Filter_MatchesIdOriginAndUnit_CaseInsensitively()
    {
        var picker = Create();

        picker.Filter = "VOLT";
        CollectionAssert.AreEqual(new[] { "volts" }, picker.Paths.Select(p => p.Path.Path).ToArray());

        picker.Filter = "current";
        CollectionAssert.AreEqual(new[] { "amps" }, picker.Paths.Select(p => p.Path.Path).ToArray());

        picker.Filter = "A";
        Assert.IsTrue(picker.Paths.Any(p => p.Path.Path == "amps"));

        picker.Filter = string.Empty;
        Assert.HasCount(4, picker.Paths);
    }

    [TestMethod]
    public void PathRows_DescribeTypeUnitRangeAndChoices()
    {
        var picker = Create();

        Assert.AreEqual("number, V, 0 to 30", picker.AllPaths.Single(p => p.Path.Path == "volts").Detail);
        Assert.AreEqual("text, CC/CV", picker.AllPaths.Single(p => p.Path.Path == "mode").Detail);
        Assert.IsNotNull(picker.AllPaths.Single(p => p.Path.Path == "volts").Example);
        Assert.AreEqual("volts  (number, V, 0 to 30)", picker.AllPaths.Single(p => p.Path.Path == "volts").ToString());
    }

    [TestMethod]
    public void NextSample_MovesTheResult_AndRaisesChanged()
    {
        var picker = Create("{volts}");
        var before = picker.Result;
        var raised = 0;
        picker.Changed += (_, _) => raised++;

        picker.NextSample();

        Assert.AreEqual(1, raised);
        Assert.AreNotEqual(before, picker.Result);
    }

    [TestMethod]
    public void SameSeed_GivesTheSameResult()
    {
        Assert.AreEqual(Create("{volts} + {amps}").Result, Create("{volts} + {amps}").Result);
    }

    [TestMethod]
    public void Text_Setter_Reparses_AndRaisesChanged_OnlyWhenItChanges()
    {
        var picker = Create("1");
        var raised = 0;
        picker.Changed += (_, _) => raised++;

        picker.Text = "1";
        picker.Text = "2 +";

        Assert.AreEqual(1, raised);
        Assert.IsFalse(picker.IsValid);
    }

    [TestMethod]
    public void DuplicatePaths_AreListedOnce_FirstWins()
    {
        var picker = new ExpressionPickerViewModel([_voltage, _voltage with { Origin = "again" }]);

        Assert.HasCount(1, picker.AllPaths);
        Assert.AreEqual("Voltage", picker.AllPaths[0].Path.Origin);
    }

    [TestMethod]
    public void BundledManifest_PathsAreAllUsableInAnExpression()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "manifests");
        Assert.IsTrue(Directory.Exists(root), "the bundled manifests folder should be copied next to the tests");

        foreach (var manifest in Directory.EnumerateDirectories(root).Select(DeviceManifestLoader.Load))
        {
            var paths = ValuePathCatalog.Enumerate(manifest);
            var picker = new ExpressionPickerViewModel(paths);
            foreach (var row in picker.AllPaths.Where(p => p.Path.Type != ValuePathType.Text))
            {
                picker.Text = string.Empty;
                picker.InsertPath(row);
                Assert.IsTrue(picker.IsValid, $"{manifest.Name}: {row.Reference}");
                Assert.AreEqual(0, picker.Warnings.Count, $"{manifest.Name}: {row.Reference}");
            }
        }
    }

    [TestMethod]
    public void ChannelList_AppendsBareIdsWithSeparators_AndValidatesEachChannel()
    {
        var picker = new ExpressionPickerViewModel([_voltage, _current], null, 7, channelList: true);

        picker.InsertPath(picker.AllPaths.Single(p => p.Path.Path == "volts"));
        picker.InsertPath(picker.AllPaths.Single(p => p.Path.Path == "amps"));

        Assert.AreEqual("volts; amps", picker.Text);
        Assert.IsTrue(picker.IsValid);
        Assert.AreEqual(0, picker.Warnings.Count);

        picker.Text = "volts:V:#FF0000:{amps} *; nothing";
        Assert.IsFalse(picker.IsValid);
        StringAssert.Contains(picker.Error, "volts");

        picker.Text = "volts:V:#FF0000:{amps} * 2; nothing";
        Assert.IsTrue(picker.IsValid);
        Assert.HasCount(1, picker.Warnings);
    }
}
