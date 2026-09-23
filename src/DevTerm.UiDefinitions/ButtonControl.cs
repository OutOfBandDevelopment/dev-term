namespace DevTerm.UiDefinitions;

/// <summary>Invokes a command; carries no value of its own (e.g. "Apply", "Reboot", "Reset Counter").</summary>
public sealed class ButtonControl : UiControl
{
    /// <summary>The command this button invokes, if different from <see cref="UiControl.Id"/>.</summary>
    public string? CommandId { get; set; }

    /// <summary>
    /// When set, clicking this button opens a modal RGB/HSV color picker (both front-end renderers
    /// support this generically — not tied to any one device) instead of invoking <see cref="Id"/>/
    /// <see cref="CommandId"/> directly; on confirm, the picked color is sent as an
    /// <c>"r,g,b"</c>-formatted value (each 0-255, invariant culture) to the command id named here.
    /// </summary>
    public string? ColorPickerTargetCommandId { get; set; }
}
