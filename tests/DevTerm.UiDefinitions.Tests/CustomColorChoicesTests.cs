using DevTerm.Test.Utilities;
using DevTerm.UiDefinitions;

namespace DevTerm.UiDefinitions.Tests;

[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class CustomColorChoicesTests
{
    private static UiDefinition Definition(string option) => new()
    {
        Name = "Light",
        Sections =
        [
            new UiSection
            {
                Label = "Color",
                Controls =
                [
                    new ChoiceControl { Id = "color", Label = "Color", Style = ChoiceStyle.RadioGroup, Options = ["Red", "Custom", "Off"] },
                    new ButtonControl { Id = "customColor", Label = "Custom...", ColorPickerTargetCommandId = "color", ColorPickerChoiceOption = option },
                ],
            },
        ],
    };

    [TestMethod]
    public void Find_LinksTheChoiceToItsColorButton()
    {
        var links = CustomColorChoices.Find(Definition("Custom"));

        Assert.AreEqual("customColor", links["color"].Button.Id);
        Assert.AreEqual("Custom", links["color"].Option);
    }

    [TestMethod]
    public void Find_IgnoresAnOptionTheChoiceDoesNotHave() =>
        Assert.IsEmpty(CustomColorChoices.Find(Definition("Magenta")));

    [TestMethod]
    public void ColorPickerChoiceOption_RoundTripsThroughJsonAndXml()
    {
        var definition = Definition("Custom");

        foreach (var copy in new[] { UiDefinitionSerializer.FromJson(UiDefinitionSerializer.ToJson(definition)), UiDefinitionSerializer.FromXml(UiDefinitionSerializer.ToXml(definition)) })
        {
            var button = copy.Sections[0].Controls.OfType<ButtonControl>().Single();
            Assert.AreEqual("Custom", button.ColorPickerChoiceOption);
        }
    }
}
