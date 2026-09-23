namespace DevTerm.Devices.Scpi;

/// <summary>How a <see cref="ScpiParameterDefinition"/>'s value should be collected and rendered.</summary>
public enum ScpiParameterKind
{
    /// <summary>A bounded number (renders as a <c>NumericControl</c>).</summary>
    Numeric,

    /// <summary>One of a fixed set of options (renders as a <c>ChoiceControl</c>).</summary>
    Choice,

    /// <summary>Free text (renders as a <c>TextFieldControl</c>).</summary>
    Text,
}
