using DevTerm.Test.Utilities;

namespace DevTerm.UiDefinitions.Tests;

/// <summary>
/// Verifies <see cref="ValueConstraint"/> round-trips through both serializers (and that
/// definitions written before it existed still load), and that <see cref="ValueValidator"/> — the
/// one validator both control-panel renderers share — rejects and normalizes the way they rely on.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class ValueConstraintTests
{
    private static UiDefinition BuildDefinition() => new()
    {
        Name = "Constraint Device",
        Sections =
        [
            new UiSection
            {
                Label = "Values",
                Controls =
                [
                    new TextFieldControl
                    {
                        Id = "channel",
                        Label = "Channel",
                        DefaultValue = "1",
                        Constraint = new ValueConstraint { Kind = ValueKind.Integer, Minimum = 1, Maximum = 4 },
                    },
                    new TextFieldControl
                    {
                        Id = "offset",
                        Label = "Offset",
                        Constraint = new ValueConstraint { Kind = ValueKind.Number, Minimum = -2.5, ClampToRange = true },
                    },
                    new TextFieldControl { Id = "name", Label = "Name", MaxLength = 8 },
                    new SliderControl { Id = "level", Label = "Level", Minimum = 0, Maximum = 255, Step = 1, DefaultValue = 10 },
                ],
            },
        ],
    };

    private static void AssertRoundTripped(UiDefinition roundTripped)
    {
        var controls = roundTripped.Sections[0].Controls;

        var channel = ((TextFieldControl)controls[0]).Constraint;
        Assert.IsNotNull(channel);
        Assert.AreEqual(ValueKind.Integer, channel.Kind);
        Assert.AreEqual(1d, channel.Minimum);
        Assert.AreEqual(4d, channel.Maximum);
        Assert.IsFalse(channel.ClampToRange);

        var offset = ((TextFieldControl)controls[1]).Constraint;
        Assert.IsNotNull(offset);
        Assert.AreEqual(ValueKind.Number, offset.Kind);
        Assert.AreEqual(-2.5, offset.Minimum);
        Assert.IsNull(offset.Maximum);
        Assert.IsTrue(offset.ClampToRange);

        var name = (TextFieldControl)controls[2];
        Assert.IsNull(name.Constraint);
        Assert.AreEqual(8, name.MaxLength);

        var level = (SliderControl)controls[3];
        Assert.AreEqual(0d, level.Minimum);
        Assert.AreEqual(255d, level.Maximum);
    }

    [TestMethod]
    public void Json_RoundTripsTheConstraint()
    {
        var json = UiDefinitionSerializer.ToJson(BuildDefinition());

        AssertRoundTripped(UiDefinitionSerializer.FromJson(json));
        Assert.Contains("\"Kind\": \"Integer\"", json, "The value kind is written by name, not as a number.");
    }

    [TestMethod]
    public void Xml_RoundTripsTheConstraint()
    {
        var xml = UiDefinitionSerializer.ToXml(BuildDefinition());

        AssertRoundTripped(UiDefinitionSerializer.FromXml(xml));
        Assert.Contains("<Constraint>", xml);
    }

    [TestMethod]
    public void Json_WrittenBeforeConstraintsExisted_StillLoads()
    {
        const string json = """
            {
              "Name": "Old Device",
              "Sections": [
                {
                  "Label": "Old",
                  "Controls": [
                    { "kind": "textField", "Id": "host", "Label": "Host", "MaxLength": 20 },
                    { "kind": "numeric", "Id": "port", "Label": "Port", "Minimum": 1, "Maximum": 65535, "DefaultValue": 80 }
                  ]
                }
              ]
            }
            """;

        var definition = UiDefinitionSerializer.FromJson(json);

        Assert.IsNull(((TextFieldControl)definition.Sections[0].Controls[0]).Constraint);
        var port = (NumericControl)definition.Sections[0].Controls[1];
        Assert.AreEqual(1d, port.Minimum);
        Assert.AreEqual(65535d, port.Maximum);
    }

    [TestMethod]
    public void Validate_NoConstraintOrText_AcceptsAnythingUnchanged()
    {
        Assert.AreEqual(" any thing ", ValueValidator.Validate(null, " any thing ").Value);
        Assert.IsTrue(ValueValidator.Validate(new ValueConstraint { Kind = ValueKind.Text, Maximum = 1 }, "longer than one").IsValid);
    }

    [TestMethod]
    public void Validate_Number_NormalizesToInvariantText()
    {
        var constraint = new ValueConstraint { Kind = ValueKind.Number };

        Assert.AreEqual("1000", ValueValidator.Validate(constraint, " 1e3 ").Value);
        Assert.AreEqual("0.25", ValueValidator.Validate(constraint, ".25").Value);
        Assert.AreEqual("5", ValueValidator.Validate(constraint, "05").Value);
    }

    [TestMethod]
    public void Validate_NotANumber_IsRejectedWithAMessage()
    {
        var result = ValueValidator.Validate(new ValueConstraint { Kind = ValueKind.Number }, "abc");

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("'abc' is not a number.", result.Error);
        Assert.AreEqual("abc", result.Value);
        Assert.IsFalse(ValueValidator.Validate(new ValueConstraint { Kind = ValueKind.Number }, string.Empty).IsValid);
    }

    [TestMethod]
    public void Validate_Integer_RejectsFractionsAndNormalizesWholeNumbers()
    {
        var constraint = new ValueConstraint { Kind = ValueKind.Integer };

        Assert.AreEqual("'2.5' is not a whole number.", ValueValidator.Validate(constraint, "2.5").Error);
        Assert.AreEqual("3", ValueValidator.Validate(constraint, "3.0").Value);
        Assert.AreEqual("-7", ValueValidator.Validate(constraint, "-7").Value);
    }

    [TestMethod]
    public void Validate_OutOfRange_IsRejectedUnlessClamping()
    {
        var rejecting = new ValueConstraint { Kind = ValueKind.Integer, Minimum = 1, Maximum = 4 };
        var clamping = new ValueConstraint { Kind = ValueKind.Integer, Minimum = 1, Maximum = 4, ClampToRange = true };

        var rejected = ValueValidator.Validate(rejecting, "9");
        Assert.IsFalse(rejected.IsValid);
        Assert.AreEqual("9 is out of range (1 to 4).", rejected.Error);
        Assert.AreEqual("4", ValueValidator.Validate(clamping, "9").Value);
        Assert.AreEqual("1", ValueValidator.Validate(clamping, "-3").Value);
        Assert.AreEqual("at least -2.5", new ValueConstraint { Minimum = -2.5 }.DescribeRange());
    }

    [TestMethod]
    public void ConstraintFor_SliderAndNumeric_ClampToTheirRange()
    {
        var numeric = ValueValidator.ConstraintFor(new NumericControl { Id = "n", Label = "N", Minimum = 0, Maximum = 100 });
        var slider = ValueValidator.ConstraintFor(new SliderControl { Id = "s", Label = "S", Minimum = 0, Maximum = 255 });

        Assert.AreEqual("100", ValueValidator.Validate(numeric, "999").Value);
        Assert.AreEqual("255", ValueValidator.Validate(slider, "999").Value);
        Assert.IsFalse(ValueValidator.Validate(numeric, "lots").IsValid, "Unparsable input is rejected, not silently replaced by the default.");
        Assert.IsNull(ValueValidator.ConstraintFor(new ToggleControl { Id = "t", Label = "T" }));
    }
}
