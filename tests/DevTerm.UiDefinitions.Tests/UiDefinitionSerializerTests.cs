using DevTerm.UiDefinitions;

namespace DevTerm.UiDefinitions.Tests;

/// <summary>
/// Round-trips a real control panel — the Kuando Busylight color/blink/sound panel from
/// docs/design/features/kuando-busylight-protocol.md's @startsalt mockup, in this model's shape —
/// through both serializers, rather than a synthetic minimal example.
/// </summary>
[TestCategory("UNIT")]
[TestClass]
public sealed class UiDefinitionSerializerTests
{
    private static UiDefinition BuildBusylightPanel() => new()
    {
        Name = "Kuando Busylight",
        Description = "Color / blink / sound control panel",
        Sections =
        [
            new UiSection
            {
                Label = "Color",
                Controls =
                [
                    new ChoiceControl
                    {
                        Id = "color",
                        Label = "Color",
                        Style = ChoiceStyle.RadioGroup,
                        Options = ["Red", "Green", "Blue", "Yellow", "Off"],
                        DefaultValue = "Off",
                    },
                    new ButtonControl { Id = "customColor", Label = "Custom..." },
                ],
            },
            new UiSection
            {
                Label = "Blink",
                Controls =
                [
                    new ChoiceControl
                    {
                        Id = "blinkMode",
                        Label = "Blink",
                        Style = ChoiceStyle.RadioGroup,
                        Options = ["Solid", "Slow", "Fast"],
                        DefaultValue = "Solid",
                    },
                    new NumericControl { Id = "onMs", Label = "On", Unit = "ms", Minimum = 0, Maximum = 65535, DefaultValue = 1000 },
                    new NumericControl { Id = "offMs", Label = "Off", Unit = "ms", Minimum = 0, Maximum = 65535, DefaultValue = 0 },
                ],
            },
            new UiSection
            {
                Label = "Sound",
                Controls =
                [
                    new ToggleControl { Id = "mute", Label = "Mute", DefaultValue = false },
                    new ChoiceControl
                    {
                        Id = "track",
                        Label = "Track",
                        Style = ChoiceStyle.Dropdown,
                        Options = ["Funky", "Nordic", "Quiet", "Open Office", "Kuando"],
                    },
                    new SliderControl { Id = "volume", Label = "Volume", Minimum = 0, Maximum = 7, Step = 1, DefaultValue = 0 },
                ],
            },
            new UiSection
            {
                Controls =
                [
                    new ButtonControl { Id = "apply", Label = "Apply" },
                    new ButtonControl { Id = "programSequence", Label = "Program Sequence..." },
                ],
            },
        ],
    };

    [TestMethod]
    public void ToJson_ThenFromJson_PreservesEveryControlKindAndField()
    {
        var original = BuildBusylightPanel();

        var json = UiDefinitionSerializer.ToJson(original);
        var roundTripped = UiDefinitionSerializer.FromJson(json);

        AssertEquivalent(original, roundTripped);
    }

    [TestMethod]
    public void ToXml_ThenFromXml_PreservesEveryControlKindAndField()
    {
        var original = BuildBusylightPanel();

        var xml = UiDefinitionSerializer.ToXml(original);
        var roundTripped = UiDefinitionSerializer.FromXml(xml);

        AssertEquivalent(original, roundTripped);
    }

    [TestMethod]
    public void ToJson_UsesADistinctKindDiscriminatorPerControlType()
    {
        var json = UiDefinitionSerializer.ToJson(BuildBusylightPanel());

        StringAssert.Contains(json, "\"kind\": \"choice\"");
        StringAssert.Contains(json, "\"kind\": \"button\"");
        StringAssert.Contains(json, "\"kind\": \"numeric\"");
        StringAssert.Contains(json, "\"kind\": \"toggle\"");
        StringAssert.Contains(json, "\"kind\": \"slider\"");
    }

    [TestMethod]
    public void ToXml_UsesADistinctElementNamePerControlType()
    {
        var xml = UiDefinitionSerializer.ToXml(BuildBusylightPanel());

        StringAssert.Contains(xml, "<Choice>");
        StringAssert.Contains(xml, "<Button>");
        StringAssert.Contains(xml, "<Numeric>");
        StringAssert.Contains(xml, "<Toggle>");
        StringAssert.Contains(xml, "<Slider>");
    }

    private static void AssertEquivalent(UiDefinition expected, UiDefinition actual)
    {
        Assert.AreEqual(expected.Name, actual.Name);
        Assert.AreEqual(expected.Description, actual.Description);
        Assert.HasCount(expected.Sections.Count, actual.Sections);

        for (var i = 0; i < expected.Sections.Count; i++)
        {
            var expectedSection = expected.Sections[i];
            var actualSection = actual.Sections[i];
            Assert.AreEqual(expectedSection.Label, actualSection.Label, $"Section {i} label");
            Assert.HasCount(expectedSection.Controls.Count, actualSection.Controls, $"Section {i} control count");

            for (var j = 0; j < expectedSection.Controls.Count; j++)
            {
                AssertControlEquivalent(expectedSection.Controls[j], actualSection.Controls[j], $"Section {i} control {j}");
            }
        }
    }

    private static void AssertControlEquivalent(UiControl expected, UiControl actual, string context)
    {
        Assert.AreEqual(expected.GetType(), actual.GetType(), $"{context} type");
        Assert.AreEqual(expected.Id, actual.Id, $"{context} Id");
        Assert.AreEqual(expected.Label, actual.Label, $"{context} Label");

        switch (expected)
        {
            case ButtonControl e:
                Assert.AreEqual(e.CommandId, ((ButtonControl)actual).CommandId, $"{context} CommandId");
                break;
            case ToggleControl e:
                Assert.AreEqual(e.DefaultValue, ((ToggleControl)actual).DefaultValue, $"{context} DefaultValue");
                break;
            case SliderControl e:
                var slider = (SliderControl)actual;
                Assert.AreEqual(e.Minimum, slider.Minimum, $"{context} Minimum");
                Assert.AreEqual(e.Maximum, slider.Maximum, $"{context} Maximum");
                Assert.AreEqual(e.Step, slider.Step, $"{context} Step");
                Assert.AreEqual(e.DefaultValue, slider.DefaultValue, $"{context} DefaultValue");
                Assert.AreEqual(e.Unit, slider.Unit, $"{context} Unit");
                break;
            case NumericControl e:
                var numeric = (NumericControl)actual;
                Assert.AreEqual(e.Minimum, numeric.Minimum, $"{context} Minimum");
                Assert.AreEqual(e.Maximum, numeric.Maximum, $"{context} Maximum");
                Assert.AreEqual(e.DefaultValue, numeric.DefaultValue, $"{context} DefaultValue");
                Assert.AreEqual(e.Unit, numeric.Unit, $"{context} Unit");
                break;
            case ChoiceControl e:
                var choice = (ChoiceControl)actual;
                CollectionAssert.AreEqual(e.Options, choice.Options, $"{context} Options");
                Assert.AreEqual(e.DefaultValue, choice.DefaultValue, $"{context} DefaultValue");
                Assert.AreEqual(e.Style, choice.Style, $"{context} Style");
                break;
            case TextFieldControl e:
                var textField = (TextFieldControl)actual;
                Assert.AreEqual(e.DefaultValue, textField.DefaultValue, $"{context} DefaultValue");
                Assert.AreEqual(e.MaxLength, textField.MaxLength, $"{context} MaxLength");
                break;
            case IndicatorControl e:
                Assert.AreEqual(e.DefaultValue, ((IndicatorControl)actual).DefaultValue, $"{context} DefaultValue");
                break;
            default:
                Assert.Fail($"Unhandled control type {expected.GetType()}");
                break;
        }
    }
}
