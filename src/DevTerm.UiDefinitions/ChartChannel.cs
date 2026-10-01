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

    /// <summary>
    /// When set, an <see cref="Expression"/> deriving this channel's plotted number from the live
    /// published values (e.g. <c>{raw_mv} / 1000</c>) instead of plotting <see cref="Id"/>'s own
    /// published value directly. Unset (the default) keeps today's direct behavior. See
    /// docs/design/proposals/manifest-editor-expression-builder.md.
    /// </summary>
    public string? Expression { get; set; }
}
