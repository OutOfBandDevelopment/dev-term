namespace DevTerm.Devices.Scpi;

/// <summary>
/// One parameter a <see cref="ScpiCommandDefinition"/>'s <see cref="ScpiCommandDefinition.Template"/>
/// substitutes by name (a <c>{Name}</c> token) — see docs/design/proposals/scpi-instrument-control.md.
/// </summary>
public sealed class ScpiParameterDefinition
{
    public required string Name { get; set; }

    public ScpiParameterKind Kind { get; set; } = ScpiParameterKind.Text;

    /// <summary><see cref="ScpiParameterKind.Numeric"/> only.</summary>
    public double Minimum { get; set; }

    /// <summary><see cref="ScpiParameterKind.Numeric"/> only.</summary>
    public double Maximum { get; set; }

    /// <summary><see cref="ScpiParameterKind.Numeric"/> only.</summary>
    public string? Unit { get; set; }

    /// <summary>
    /// <see cref="ScpiParameterKind.Numeric"/> only. When set, the value is formatted with exactly
    /// this many decimal places (e.g. <c>2</c> → <c>"12.00"</c>) instead of a free-form
    /// <see cref="double.ToString()"/>. Needed for instruments whose command parser expects a
    /// fixed-width numeric field rather than a variable-length SCPI number — confirmed against a
    /// real Korad KA3005P/KA6003P, whose <c>VSET</c>/<c>ISET</c> commands have no terminator at all
    /// and parse a fixed number of characters after the colon; sending a whole number like
    /// <c>"12"</c> instead of <c>"12.00"</c> desyncs the parser and the set (and any command after
    /// it) silently fails.
    /// </summary>
    public int? DecimalPlaces { get; set; }

    /// <summary>
    /// <see cref="ScpiParameterKind.Numeric"/> only, paired with <see cref="DecimalPlaces"/>. When
    /// set, the integer part is zero-padded to this many digits (e.g. <c>2</c> → <c>"05.00"</c>
    /// rather than <c>"5.00"</c>). Real-hardware-confirmed necessary for the Korad KA3005P/KA6003P:
    /// sending <c>VSET1:5.00</c> (missing the leading zero) is one character short of the fixed-width
    /// field the device parses, and a follow-up <c>VSET1?</c> read back the unchanged previous value
    /// — the set silently failed even with <see cref="DecimalPlaces"/> alone applied.
    /// </summary>
    public int? IntegerDigits { get; set; }

    /// <summary><see cref="ScpiParameterKind.Choice"/> only.</summary>
    public List<string> Options { get; set; } = [];

    public string? DefaultValue { get; set; }
}
