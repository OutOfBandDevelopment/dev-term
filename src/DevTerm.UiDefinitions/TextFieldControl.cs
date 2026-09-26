namespace DevTerm.UiDefinitions;

/// <summary>Free text (e.g. an IP address, a hostname) — or, with a <see cref="Constraint"/>, a typed number checked on commit.</summary>
public sealed class TextFieldControl : UiControl
{
    public string? DefaultValue { get; set; }

    public int? MaxLength { get; set; }

    /// <summary>
    /// Optional data type/bounds for the typed value, checked by <see cref="ValueValidator"/> when the
    /// field is committed (and when a <see cref="ButtonControl.ParameterFieldIds"/> button reads it):
    /// an invalid value is reported and not sent, a valid number is normalized. Null means free text.
    /// </summary>
    public ValueConstraint? Constraint { get; set; }
}
