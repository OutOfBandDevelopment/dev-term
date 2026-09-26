namespace DevTerm.UiDefinitions;

/// <summary>
/// A read-only bar graph, one bar per <see cref="ChartChannel"/>, each filled to its latest value
/// within [<see cref="Minimum"/>, <see cref="Maximum"/>] (values outside are clamped to the ends).
/// Display-only like <see cref="IndicatorControl"/>: never sends, updated by live decoder values.
/// </summary>
public sealed class BarGraphControl : UiControl
{
    public List<ChartChannel> Channels { get; set; } = [];

    public double Minimum { get; set; }

    public double Maximum { get; set; } = 100;

    public string? Unit { get; set; }
}
