using System.Xml.Serialization;

namespace DevTerm.UiDefinitions;

/// <summary>
/// A named, declarative description of a control panel — what controls exist and how they're
/// grouped, independent of any UI framework. See docs/design/ui-definitions.md.
/// </summary>
public sealed class UiDefinition
{
    public required string Name { get; set; }

    public string? Description { get; set; }

    public List<UiSection> Sections { get; set; } = [];
}

/// <summary>One labeled group of controls. See docs/design/ui-definitions.md.</summary>
public sealed class UiSection
{
    public string? Label { get; set; }

    [XmlElement("Button", typeof(ButtonControl))]
    [XmlElement("Toggle", typeof(ToggleControl))]
    [XmlElement("Slider", typeof(SliderControl))]
    [XmlElement("Numeric", typeof(NumericControl))]
    [XmlElement("Choice", typeof(ChoiceControl))]
    [XmlElement("TextField", typeof(TextFieldControl))]
    [XmlElement("Indicator", typeof(IndicatorControl))]
    public List<UiControl> Controls { get; set; } = [];
}
