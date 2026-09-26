namespace DevTerm.Devices.Scpi;

/// <summary>
/// A <see cref="ScpiParameterDefinition"/>'s data type. Each kind has a default widget (below);
/// <see cref="ScpiParameterDefinition.Control"/> can pick a different one.
/// </summary>
public enum ScpiParameterKind
{
    /// <summary>A bounded number (renders as a <c>NumericControl</c>).</summary>
    Numeric,

    /// <summary>One of a fixed set of options (renders as a <c>ChoiceControl</c>).</summary>
    Choice,

    /// <summary>Free text (renders as a <c>TextFieldControl</c>).</summary>
    Text,
}
