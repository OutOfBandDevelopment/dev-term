using DevTerm.Test.Utilities;

namespace DevTerm.UiDefinitions.Tests;

/// <summary>
/// The display-only chart controls — <see cref="BarGraphControl"/>, <see cref="StripChartControl"/>,
/// <see cref="VectorControl"/> — round-trip through both serializers, and their shared live state
/// (<see cref="LiveDisplayState"/>, what both renderers draw from) clamps, scrolls, scales and
/// converts coordinates as documented in docs/design/ui-definitions.md.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class ChartControlsTests
{
    private static UiDefinition BuildChartsPanel() => new()
    {
        Name = "Charts",
        Sections =
        [
            new UiSection
            {
                Label = "Displays",
                Controls =
                [
                    new BarGraphControl
                    {
                        Id = "levels",
                        Label = "Levels",
                        Minimum = 0,
                        Maximum = 50,
                        Unit = "V",
                        Channels = [new ChartChannel { Id = "a", Label = "A", Color = "#112233" }, new ChartChannel { Id = "b" }],
                    },
                    new StripChartControl
                    {
                        Id = "history",
                        Label = "History",
                        HistoryLength = 5,
                        Minimum = -1,
                        Channels = [new ChartChannel { Id = "a" }],
                    },
                    new VectorControl
                    {
                        Id = "polar",
                        Label = "Polar",
                        Coordinates = CoordinateSystem.Polar,
                        RadiusId = "r",
                        AngleId = "t",
                        AngleUnit = AngleUnit.Radians,
                        Range = 2,
                        TrailLength = 3,
                        HueId = "h",
                    },
                ],
            },
        ],
    };

    private static void AssertChartsPanel(UiDefinition roundTripped)
    {
        var controls = roundTripped.Sections[0].Controls;
        var bar = (BarGraphControl)controls[0];
        Assert.AreEqual(50, bar.Maximum);
        Assert.AreEqual("V", bar.Unit);
        Assert.HasCount(2, bar.Channels);
        Assert.AreEqual("#112233", bar.Channels[0].Color);
        Assert.IsNull(bar.Channels[1].Label);

        var strip = (StripChartControl)controls[1];
        Assert.AreEqual(5, strip.HistoryLength);
        Assert.AreEqual(-1, strip.Minimum);
        Assert.IsNull(strip.Maximum);

        var vector = (VectorControl)controls[2];
        Assert.AreEqual(CoordinateSystem.Polar, vector.Coordinates);
        Assert.AreEqual("r", vector.RadiusId);
        Assert.AreEqual(AngleUnit.Radians, vector.AngleUnit);
        Assert.AreEqual(2, vector.Range);
        Assert.AreEqual("h", vector.HueId);
    }

    [TestMethod]
    public void ChartControls_RoundTripThroughJson()
    {
        var json = UiDefinitionSerializer.ToJson(BuildChartsPanel());

        Assert.Contains("\"kind\": \"barGraph\"", json);
        Assert.Contains("\"kind\": \"stripChart\"", json);
        Assert.Contains("\"kind\": \"vector\"", json);
        Assert.Contains("\"Polar\"", json, "Enums are written by name.");
        AssertChartsPanel(UiDefinitionSerializer.FromJson(json));
    }

    [TestMethod]
    public void ChartControls_RoundTripThroughXml()
    {
        var xml = UiDefinitionSerializer.ToXml(BuildChartsPanel());

        Assert.Contains("<BarGraph>", xml);
        Assert.Contains("<StripChart>", xml);
        Assert.Contains("<Vector>", xml);
        AssertChartsPanel(UiDefinitionSerializer.FromXml(xml));
    }

    [TestMethod]
    public void FromJson_AcceptsCamelCaseHandWrittenJson()
    {
        const string json = """
            {
              "name": "Hand written",
              "sections": [ { "label": "S", "controls": [
                { "kind": "vector", "id": "v", "label": "V", "coordinates": "XYZ", "xId": "x", "yId": "y", "zId": "z" }
              ] } ]
            }
            """;

        var vector = (VectorControl)UiDefinitionSerializer.FromJson(json).Sections[0].Controls[0];

        Assert.AreEqual(CoordinateSystem.XYZ, vector.Coordinates);
        Assert.AreSequenceEqual(new[] { "x", "y", "z" }, vector.ValueIds().ToArray());
    }

    [TestMethod]
    [DataRow("42", 42)]
    [DataRow(" +1.25E+01 ", 12.5)]
    [DataRow("DC VOLT 1.25 V", 1.25)]
    [DataRow("-0.5mA", -0.5)]
    public void ChartValue_ReadsTheNumberOutOfAReply(string text, double expected)
    {
        Assert.IsTrue(ChartValue.TryParse(text, out var value));
        Assert.AreEqual(expected, value, 1e-9);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("no number here")]
    [DataRow("NaN")]
    public void ChartValue_RejectsTextWithNoFiniteNumber(string text) =>
        Assert.IsFalse(ChartValue.TryParse(text, out _));

    [TestMethod]
    public void BarGraph_ClampsItsFraction_AndIgnoresOtherIds()
    {
        var state = new BarGraphState((BarGraphControl)BuildChartsPanel().Sections[0].Controls[0]);

        Assert.AreEqual(0, state.FractionOf("a"));
        Assert.IsNull(state.ValueOf("a"));

        Assert.IsTrue(state.ApplyAll(new Dictionary<string, string> { ["a"] = "25", ["b"] = "80", ["other"] = "1" }));
        Assert.AreEqual(0.5, state.FractionOf("a"), 1e-9);
        Assert.AreEqual(1, state.FractionOf("b"), "Above the maximum clamps to a full bar.");
        Assert.AreEqual(80, state.ValueOf("b"), "The value itself is kept unclamped for the readout.");

        Assert.IsFalse(state.Apply("other", "5"));
        Assert.IsFalse(state.Apply("a", "garbage"), "Unparsable text changes nothing.");
        Assert.AreEqual(25, state.ValueOf("a"));
    }

    [TestMethod]
    public void StripChart_KeepsOnlyItsHistoryLength_OldestFirst()
    {
        var state = new StripChartState((StripChartControl)BuildChartsPanel().Sections[0].Controls[1]);

        foreach (var value in new[] { 1, 2, 3, 4, 5, 6, 7 })
        {
            state.Apply("a", value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        Assert.AreSequenceEqual(new[] { 3.0, 4, 5, 6, 7 }, state.SamplesOf("a").ToArray());
    }

    [TestMethod]
    public void StripChart_Scale_UsesDeclaredBounds_AndAutoScalesTheRest()
    {
        var state = new StripChartState((StripChartControl)BuildChartsPanel().Sections[0].Controls[1]);
        Assert.AreEqual((-1.0, 1.0), state.Scale(), "No data: the declared minimum, and 1 for the missing maximum.");

        state.Apply("a", "3");
        state.Apply("a", "9");

        Assert.AreEqual((-1.0, 9.0), state.Scale(), "The declared minimum wins; the maximum follows the data.");

        var both = new StripChartState(new StripChartControl { Id = "s", Label = "S", Minimum = 0, Maximum = 10, Channels = [new ChartChannel { Id = "a" }] });
        both.Apply("a", "50");
        Assert.AreEqual((0.0, 10.0), both.Scale(), "Both bounds declared: fixed, whatever the data.");

        var flat = new StripChartState(new StripChartControl { Id = "s", Label = "S", Channels = [new ChartChannel { Id = "a" }] });
        flat.Apply("a", "4");
        Assert.AreEqual((3.5, 4.5), flat.Scale(), "A flat line gets a visible span around it.");
    }

    [TestMethod]
    public void Vector_Polar_ConvertsToCartesian_AndColorsFromHue()
    {
        var state = new VectorState((VectorControl)BuildChartsPanel().Sections[0].Controls[2]);
        Assert.IsFalse(state.HasPoint);
        Assert.AreEqual("r=— θ=— rad", state.Readout());

        state.ApplyAll(new Dictionary<string, string> { ["r"] = "2", ["t"] = (Math.PI / 2).ToString(System.Globalization.CultureInfo.InvariantCulture), ["h"] = "120" });

        Assert.IsTrue(state.HasPoint);
        Assert.AreEqual(0, state.Point.X, 1e-9);
        Assert.AreEqual(2, state.Point.Y, 1e-9);
        Assert.AreEqual(((byte)0, (byte)255, (byte)0), state.Color, "Hue 120° at full saturation/value is pure green.");
    }

    [TestMethod]
    public void Vector_Trail_GetsOnePointPerBatch_BoundedByTrailLength()
    {
        var state = new VectorState(new VectorControl { Id = "v", Label = "V", XId = "x", YId = "y", TrailLength = 2 });

        state.ApplyAll(new Dictionary<string, string> { ["x"] = "1", ["y"] = "1" });
        Assert.IsEmpty(state.Trail, "The first point has nothing behind it.");

        state.ApplyAll(new Dictionary<string, string> { ["x"] = "2", ["y"] = "2" });
        state.ApplyAll(new Dictionary<string, string> { ["x"] = "3", ["y"] = "3" });
        state.ApplyAll(new Dictionary<string, string> { ["x"] = "4", ["y"] = "4" });

        Assert.HasCount(2, state.Trail, "An x+y pair published together moves the point once, and the trail keeps TrailLength points.");
        Assert.AreEqual((2.0, 2.0, 0.0), state.Trail[0]);
        Assert.AreEqual((3.0, 3.0, 0.0), state.Trail[1]);
        Assert.AreEqual("x=4 y=4", state.Readout());
    }

    [TestMethod]
    public void ChartPalette_AssignsSlotsInOrder_ThenANeutralOverflow()
    {
        Assert.AreEqual("#2A78D6", ChartPalette.ColorFor(new ChartChannel { Id = "a" }, 0));
        Assert.AreEqual("#EB6834", ChartPalette.ColorFor(new ChartChannel { Id = "b" }, 1));
        Assert.AreEqual("#ABCDEF", ChartPalette.ColorFor(new ChartChannel { Id = "c", Color = "#ABCDEF" }, 2), "A declared color wins.");
        Assert.AreEqual(ChartPalette.Overflow, ChartPalette.ColorFor(new ChartChannel { Id = "i" }, 8), "A ninth channel is never a recycled hue.");
        Assert.AreEqual(ChartPalette.Slots[3], ChartPalette.ColorFor(new ChartChannel { Id = "d", Color = "not a color" }, 3));
    }
}
