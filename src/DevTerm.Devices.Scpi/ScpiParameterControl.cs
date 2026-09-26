namespace DevTerm.Devices.Scpi;

/// <summary>
/// Which widget a <see cref="ScpiParameterDefinition"/> is collected with — chosen independently of
/// its <see cref="ScpiParameterDefinition.Kind"/> (the value's data type) via
/// <see cref="ScpiParameterDefinition.Control"/>. When unset, the widget follows the kind (see
/// <see cref="ScpiParameterKind"/>). A hint that doesn't fit the kind (a slider for a text value, a
/// choice with no options) falls back to the kind's own widget.
/// </summary>
public enum ScpiParameterControl
{
    /// <summary>A bounded number field (<c>NumericControl</c>); <see cref="ScpiParameterKind.Numeric"/> only.</summary>
    Numeric,

    /// <summary>A drag slider (<c>SliderControl</c>) over <c>[Minimum, Maximum]</c>; <see cref="ScpiParameterKind.Numeric"/> only.</summary>
    Slider,

    /// <summary>
    /// A plain text field (<c>TextFieldControl</c>). For a <see cref="ScpiParameterKind.Numeric"/>
    /// parameter it carries a number <c>ValueConstraint</c> (integer when <c>DecimalPlaces</c> is 0)
    /// with the parameter's bounds, so a typed value that isn't a number, or is out of range, is
    /// rejected rather than silently clamped.
    /// </summary>
    Text,

    /// <summary>One of <see cref="ScpiParameterDefinition.Options"/> (<c>ChoiceControl</c>); needs a non-empty option list.</summary>
    Choice,
}
