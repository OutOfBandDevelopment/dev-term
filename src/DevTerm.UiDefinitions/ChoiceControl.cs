namespace DevTerm.UiDefinitions;

/// <summary>How a <see cref="ChoiceControl"/> should be rendered.</summary>
public enum ChoiceStyle
{
    /// <summary>A dropdown/combo box — reads better for a longer option list (e.g. a sound track name).</summary>
    Dropdown,

    /// <summary>A radio button group — reads better for a small option count (e.g. a color preset).</summary>
    RadioGroup,
}

/// <summary>One of a fixed set of named options (e.g. a color preset, a connection mode).</summary>
public sealed class ChoiceControl : UiControl
{
    public List<string> Options { get; set; } = [];

    public string? DefaultValue { get; set; }

    public ChoiceStyle Style { get; set; } = ChoiceStyle.Dropdown;
}
