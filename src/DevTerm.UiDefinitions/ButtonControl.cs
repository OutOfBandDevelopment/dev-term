namespace DevTerm.UiDefinitions;

/// <summary>Invokes a command; carries no value of its own (e.g. "Apply", "Reboot", "Reset Counter").</summary>
public sealed class ButtonControl : UiControl
{
    /// <summary>The command this button invokes, if different from <see cref="UiControl.Id"/>.</summary>
    public string? CommandId { get; set; }
}
