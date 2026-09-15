namespace DevTerm.UiDefinitions;

/// <summary>A bounded numeric value entered as a number rather than dragged (e.g. an on/off blink duration in ms).</summary>
public sealed class NumericControl : UiControl
{
    public double Minimum { get; set; }

    public double Maximum { get; set; }

    public double DefaultValue { get; set; }

    public string? Unit { get; set; }
}
