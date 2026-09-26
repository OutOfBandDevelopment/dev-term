namespace DevTerm.UiDefinitions;

/// <summary>
/// One live value a chart control plots (a bar in a <see cref="BarGraphControl"/>, a trace in a
/// <see cref="StripChartControl"/>). <see cref="Id"/> is the key the structured presenter publishes
/// the value under (<c>IStructuredPresenter.ValuesChanged</c>) — the same keying an
/// <see cref="IndicatorControl"/>'s own id uses, so one published value can drive an indicator and a
/// chart at once.
/// </summary>
public sealed class ChartChannel
{
    public required string Id { get; set; }

    /// <summary>What the legend/bar label shows; <see cref="Id"/> when unset.</summary>
    public string? Label { get; set; }

    /// <summary>An optional <c>#RRGGBB</c> color; unset channels take the next slot of <see cref="ChartPalette"/> in order.</summary>
    public string? Color { get; set; }
}
