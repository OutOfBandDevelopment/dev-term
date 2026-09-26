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
}
