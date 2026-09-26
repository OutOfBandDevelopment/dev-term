using System.Globalization;
using System.Text.Json.Serialization;

namespace DevTerm.UiDefinitions;

/// <summary>The data type a <see cref="ValueConstraint"/> checks a value against — independent of the widget that collects it.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ValueKind>))]
public enum ValueKind
{
    /// <summary>Any text; <see cref="ValueConstraint.Minimum"/>/<see cref="ValueConstraint.Maximum"/> are ignored.</summary>
    Text,

    /// <summary>A whole number (e.g. <c>"5"</c>, <c>"-3"</c>, <c>"1e3"</c>) — normalized to plain digits.</summary>
    Integer,

    /// <summary>Any real number (e.g. <c>"5"</c>, <c>"0.25"</c>, <c>"1e-3"</c>) — normalized to an invariant-culture number.</summary>
    Number,
}

/// <summary>
/// A value's data type and bounds, declared separately from the widget that collects it — so a
/// number can be typed into a plain <see cref="TextFieldControl"/> and still be rejected when it
/// isn't a number or is out of range. Checked by <see cref="ValueValidator"/>, the one validator
/// both control-panel renderers share. See docs/design/ui-definitions.md's "Values and widgets"
/// section.
/// </summary>
/// <remarks>
/// A plain class of nullable scalars (no dictionary) so it round-trips through both
/// <c>System.Text.Json</c> and <c>XmlSerializer</c> — see CLAUDE.md.
/// </remarks>
public sealed class ValueConstraint
{
    public ValueKind Kind { get; set; } = ValueKind.Text;

    /// <summary>Inclusive lower bound, <see cref="ValueKind.Integer"/>/<see cref="ValueKind.Number"/> only.</summary>
    public double? Minimum { get; set; }

    /// <summary>Inclusive upper bound, <see cref="ValueKind.Integer"/>/<see cref="ValueKind.Number"/> only.</summary>
    public double? Maximum { get; set; }

    /// <summary>
    /// When true, an out-of-range number is clamped to <see cref="Minimum"/>/<see cref="Maximum"/>
    /// instead of rejected. False by default: a typed value the device can't take is reported rather
    /// than silently changed. <see cref="SliderControl"/>/<see cref="NumericControl"/> use it (see
    /// <see cref="ForRange"/>) to keep the clamping they have always done.
    /// </summary>
    public bool ClampToRange { get; set; }

    /// <summary>
    /// The constraint a bounded <see cref="SliderControl"/>/<see cref="NumericControl"/> implies: any
    /// number, clamped into <c>[minimum, maximum]</c>.
    /// </summary>
    public static ValueConstraint ForRange(double minimum, double maximum) => new()
    {
        Kind = ValueKind.Number,
        Minimum = minimum,
        Maximum = maximum,
        ClampToRange = true,
    };

    /// <summary>A short human description of the range (e.g. <c>"0 to 255"</c>, <c>"at least 1"</c>), or null when unbounded.</summary>
    public string? DescribeRange() => (Minimum, Maximum) switch
    {
        ({ } min, { } max) => $"{Format(min)} to {Format(max)}",
        ({ } min, null) => $"at least {Format(min)}",
        (null, { } max) => $"at most {Format(max)}",
        _ => null,
    };

    internal static string Format(double value) => value.ToString(CultureInfo.InvariantCulture);
}
