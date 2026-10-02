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

    /// <summary>
    /// With <see cref="ColorPickerTargetCommandId"/> naming a <see cref="ChoiceControl"/> (e.g. the
    /// Busylight's Red/Green/Blue/... radio group), the option in that choice that stands for "this
    /// picked color" (e.g. <c>"Custom"</c>). Both renderers then keep the two in step: picking a color
    /// selects that option, and selecting that option re-applies the last picked color (opening the
    /// picker if none has been picked yet) instead of sending the option's own text. Without it, a
    /// preset radio overwrote the custom color with no way back to it short of reopening the picker.
    /// </summary>
    public string? ColorPickerChoiceOption { get; set; }

    /// <summary>
    /// When set, clicking this button reads each named sibling control's current value (by
    /// <see cref="UiControl.Id"/>, looked up in the same section) instead of invoking with no
    /// value, joins them with <c>,</c> — the same convention <see cref="ColorPickerTargetCommandId"/>
    /// already uses to pack multiple values through <c>IControlSurface</c>'s single string
    /// parameter — and invokes <see cref="CommandId"/>/<see cref="UiControl.Id"/> with the joined
    /// string. Lets a generic renderer offer "pick a command, fill in its parameters, invoke it"
    /// (e.g. a SCPI command with parameters) without a bespoke dynamic form.
    /// </summary>
    public List<string>? ParameterFieldIds { get; set; }

    /// <summary>
    /// Positionally parallel to <see cref="ParameterFieldIds"/>: an <see cref="Expression"/>
    /// transforming that index's sibling field value before it's joined into the sent string (e.g.
    /// sending a field entered in Celsius as Fahrenheit via <c>{field_id} * 9 / 5 + 32</c>), evaluated
    /// against every sibling control's current value in the same section. A null or blank entry at a
    /// given index — including when this list itself is unset — keeps that index's existing bare
    /// lookup unchanged. See docs/design/features/manifest-editor-expression-builder.md.
    /// </summary>
    public List<string?>? ParameterExpressions { get; set; }
}
