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

    /// <summary><see cref="ScpiParameterKind.Choice"/> only.</summary>
    public List<string> Options { get; set; } = [];

    public string? DefaultValue { get; set; }
}
