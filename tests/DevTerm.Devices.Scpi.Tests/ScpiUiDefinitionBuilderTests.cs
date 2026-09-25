using DevTerm.Test.Utilities;
using DevTerm.UiDefinitions;

namespace DevTerm.Devices.Scpi.Tests;

/// <summary>
/// Verifies <see cref="ScpiUiDefinitionBuilder"/>'s mapping from a data-declared
/// <see cref="ScpiInstrumentProfile"/> onto the generic <see cref="UiDefinition"/> model: one
/// section per category, the right control shape per command (button-only, button+indicator for a
/// query, parameter fields + a <see cref="ButtonControl.ParameterFieldIds"/> button for a
/// parameterized command), and the always-present Custom Command section. No real instrument
/// involved, so UNIT.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Scpi)]
[TestClass]
public sealed class ScpiUiDefinitionBuilderTests
{
    private static ScpiInstrumentProfile BuildProfile() => new()
    {
        Name = "Test Instrument",
        Commands =
        [
            new ScpiCommandDefinition { Id = "rst", Label = "Reset", Category = "Common", Template = "*RST" },
            new ScpiCommandDefinition { Id = "idn", Label = "Identify", Category = "Common", Template = "*IDN?", IsQuery = true },
            new ScpiCommandDefinition
            {
                Id = "freq",
                Label = "Set Frequency",
                Category = "Source",
                Template = "SOUR1:FREQ {Frequency}",
                Parameters = [new ScpiParameterDefinition { Name = "Frequency", Kind = ScpiParameterKind.Numeric, Minimum = 0, Maximum = 100, Unit = "Hz" }],
            },
        ],
    };

    private static UiSection FindSection(UiDefinition definition, string label) =>
        definition.Sections.Single(s => s.Label == label);

    [TestMethod]
    public void Build_GroupsCommandsByCategoryPlusCustomCommandSection()
    {
        var definition = ScpiUiDefinitionBuilder.Build(BuildProfile());

        Assert.AreSequenceEqual(["Common", "Source", "Custom Command"], definition.Sections.Select(s => s.Label).ToArray(), Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public void Build_UsesProfileNameAsDefinitionName()
    {
        var definition = ScpiUiDefinitionBuilder.Build(BuildProfile());

        Assert.AreEqual("Test Instrument", definition.Name);
    }

    [TestMethod]
    public void Build_ZeroParameterCommand_IsAPlainButtonWithNoCommandIdOrParameterFieldIds()
    {
        var definition = ScpiUiDefinitionBuilder.Build(BuildProfile());
        var section = FindSection(definition, "Common");

        var button = (ButtonControl)section.Controls.Single(c => c.Id == "rst");

        Assert.IsNull(button.CommandId);
        Assert.IsNull(button.ParameterFieldIds);
    }

    [TestMethod]
    public void Build_QueryCommand_AddsMatchingReplyIndicator()
    {
        var definition = ScpiUiDefinitionBuilder.Build(BuildProfile());
        var section = FindSection(definition, "Common");

        var indicator = section.Controls.OfType<IndicatorControl>().Single(c => c.Id == "idn.reply");

        Assert.AreEqual("Identify Reply", indicator.Label);
    }

    [TestMethod]
    public void Build_NonQueryCommand_HasNoReplyIndicator()
    {
        var definition = ScpiUiDefinitionBuilder.Build(BuildProfile());
        var section = FindSection(definition, "Common");

        Assert.DoesNotContain(c => c.Id == "rst.reply", section.Controls.OfType<IndicatorControl>());
    }

    [TestMethod]
    public void Build_ParameterizedCommand_EmitsOneFieldPerParameter()
    {
        var definition = ScpiUiDefinitionBuilder.Build(BuildProfile());
        var section = FindSection(definition, "Source");

        var field = (NumericControl)section.Controls.Single(c => c.Id == "freq.Frequency");

        Assert.AreEqual(0, field.Minimum);
        Assert.AreEqual(100, field.Maximum);
        Assert.AreEqual("Hz", field.Unit);
    }

    [TestMethod]
    public void Build_ParameterizedCommand_ButtonInvokesCommandIdWithParameterFieldIds()
    {
        var definition = ScpiUiDefinitionBuilder.Build(BuildProfile());
        var section = FindSection(definition, "Source");

        var button = (ButtonControl)section.Controls.Single(c => c.Id == "freq.send");

        Assert.AreEqual("freq", button.CommandId);
        Assert.AreSequenceEqual(["freq.Frequency"], button.ParameterFieldIds);
    }

    [TestMethod]
    public void Build_CustomCommandSection_HasTextFieldButtonAndIndicator()
    {
        var definition = ScpiUiDefinitionBuilder.Build(BuildProfile());
        var section = FindSection(definition, "Custom Command");

        var textField = section.Controls.OfType<TextFieldControl>().Single();
        var button = section.Controls.OfType<ButtonControl>().Single();
        var indicator = section.Controls.OfType<IndicatorControl>().Single();

        Assert.AreEqual(ScpiUiDefinitionBuilder.CustomCommandFieldId, textField.Id);
        Assert.AreEqual(ScpiControlSurface.SendCustomCommandId, button.Id);
        Assert.AreSequenceEqual([ScpiUiDefinitionBuilder.CustomCommandFieldId], button.ParameterFieldIds);
        Assert.AreEqual($"{ScpiControlSurface.SendCustomCommandId}.reply", indicator.Id);
    }

    [TestMethod]
    public void Build_ChoiceParameter_CarriesOptionsAndDefault()
    {
        var profile = new ScpiInstrumentProfile
        {
            Name = "Choice Test",
            Commands =
            [
                new ScpiCommandDefinition
                {
                    Id = "conf",
                    Label = "Configure",
                    Template = "CONF {Range}",
                    Parameters = [new ScpiParameterDefinition { Name = "Range", Kind = ScpiParameterKind.Choice, Options = ["AUTO", "10"], DefaultValue = "AUTO" }],
                },
            ],
        };

        var definition = ScpiUiDefinitionBuilder.Build(profile);
        var field = (ChoiceControl)FindSection(definition, "Commands").Controls.Single(c => c.Id == "conf.Range");

        Assert.AreSequenceEqual(["AUTO", "10"], field.Options);
        Assert.AreEqual("AUTO", field.DefaultValue);
    }
}
