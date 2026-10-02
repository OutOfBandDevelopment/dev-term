using System.Text.Json.Serialization;

namespace DevTerm.UiDefinitions;

/// <summary>How an <see cref="IndicatorControl"/> reads: plain, or as a warning (e.g. a "device not found" hint).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<IndicatorStyle>))]
public enum IndicatorStyle
{
    Plain,

    /// <summary>Drawn as a warning where the front end can (WPF: the window's warning text color; the TUI has no per-label color and shows it plain).</summary>
    Warning,
}

/// <summary>
/// A read-only display bound to live decoder output, not a control the user changes (e.g. a
/// digital input's current state, a pulse counter value, a connection status line). In a generated
/// form, a read-only property of the model the form is bound to.
/// </summary>
public sealed class IndicatorControl : UiControl
{
    public string? DefaultValue { get; set; }

    public IndicatorStyle Style { get; set; } = IndicatorStyle.Plain;

    /// <summary>
    /// When set, an <see cref="Expression"/> deriving this indicator's displayed number from the
    /// live published values (e.g. <c>{raw_mv} / 1000</c>) instead of showing <see cref="UiControl.Id"/>'s
    /// own published text verbatim. Unset (the default) keeps today's direct behavior. See
    /// docs/design/proposals/manifest-editor-expression-builder.md.
    /// </summary>
    public string? Expression { get; set; }
}
