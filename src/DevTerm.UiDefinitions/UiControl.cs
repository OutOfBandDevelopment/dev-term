using System.Text.Json.Serialization;

namespace DevTerm.UiDefinitions;

/// <summary>
/// One control in a <see cref="UiSection"/>. <see cref="Id"/> is what a future
/// <c>IControlSurface</c> command/parameter this control reads or writes would be named — this
/// type is deliberately independent of that contract for now (see docs/design/ui-definitions.md).
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ButtonControl), "button")]
[JsonDerivedType(typeof(ToggleControl), "toggle")]
[JsonDerivedType(typeof(SliderControl), "slider")]
[JsonDerivedType(typeof(NumericControl), "numeric")]
[JsonDerivedType(typeof(ChoiceControl), "choice")]
[JsonDerivedType(typeof(TextFieldControl), "textField")]
[JsonDerivedType(typeof(IndicatorControl), "indicator")]
[JsonDerivedType(typeof(BarGraphControl), "barGraph")]
[JsonDerivedType(typeof(StripChartControl), "stripChart")]
[JsonDerivedType(typeof(VectorControl), "vector")]
public abstract class UiControl
{
    public required string Id { get; set; }

    public required string Label { get; set; }

    /// <summary>Optional help text (a tooltip in WPF; the TUI has no hover to show it on). A generated form fills it from a property's <c>[Description]</c>.</summary>
    public string? Description { get; set; }

    /// <summary>When set, the control is shown only while this condition holds — honored by the form renderers (see docs/design/ui-definitions.md's "Forms from one definition").</summary>
    public UiCondition? VisibleWhen { get; set; }
}
