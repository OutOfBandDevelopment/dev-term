namespace DevTerm.UiDefinitions;

/// <summary>
/// A read-only display bound to live decoder output, not a control the user changes (e.g. a
/// digital input's current state, a pulse counter value, a connection status line).
/// </summary>
public sealed class IndicatorControl : UiControl
{
    public string? DefaultValue { get; set; }
}
