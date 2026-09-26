using System.Text.Json.Serialization;

namespace DevTerm.UiDefinitions;

/// <summary>How a <see cref="ChoiceControl"/> should be rendered (written by name in JSON; a number still reads).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ChoiceStyle>))]
public enum ChoiceStyle
{
    /// <summary>A dropdown/combo box — reads better for a longer option list (e.g. a sound track name).</summary>
    Dropdown,

    /// <summary>A radio button group — reads better for a small option count (e.g. a color preset).</summary>
    RadioGroup,

    /// <summary>
    /// A row of check boxes, any number of them checked — a multi-select. The value is the checked
    /// options, comma-joined in <see cref="ChoiceControl.Options"/> order (e.g. the connection
    /// editor's presenter list). Rendered by the form renderers; a control panel, which sends one
    /// value per change, treats it as a <see cref="Dropdown"/>.
    /// </summary>
    CheckList,
}

/// <summary>One of a fixed set of named options (e.g. a color preset, a connection mode).</summary>
public sealed class ChoiceControl : UiControl
{
    public List<string> Options { get; set; } = [];

    public string? DefaultValue { get; set; }

    public ChoiceStyle Style { get; set; } = ChoiceStyle.Dropdown;
}
