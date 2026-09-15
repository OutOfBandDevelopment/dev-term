namespace DevTerm.UiDefinitions;

/// <summary>Free text (e.g. an IP address, a hostname).</summary>
public sealed class TextFieldControl : UiControl
{
    public string? DefaultValue { get; set; }

    public int? MaxLength { get; set; }
}
