namespace DevTerm.UiDefinitions;

/// <summary>
/// A read-only strip/roll chart recorder: each <see cref="ChartChannel"/> is a trace of its last
/// <see cref="HistoryLength"/> values, scrolling left as new values arrive. The value axis is
/// [<see cref="Minimum"/>, <see cref="Maximum"/>] when both are set, otherwise it auto-scales to the
/// visible history. Display-only like <see cref="IndicatorControl"/>.
/// </summary>
public sealed class StripChartControl : UiControl
{
    public List<ChartChannel> Channels { get; set; } = [];

    public double? Minimum { get; set; }

    public double? Maximum { get; set; }

    /// <summary>How many samples per channel the chart keeps (and spans horizontally).</summary>
    public int HistoryLength { get; set; } = 60;

    public string? Unit { get; set; }
}
