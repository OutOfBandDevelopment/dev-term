namespace DevTerm.UiDefinitions;

/// <summary>A bounded numeric range dragged to a value (e.g. an analog output 0-255, a volume level).</summary>
public sealed class SliderControl : UiControl
{
    public double Minimum { get; set; }

    public double Maximum { get; set; }

    public double Step { get; set; } = 1;

    public double DefaultValue { get; set; }

    public string? Unit { get; set; }
}
