using System.Globalization;

namespace DevTerm.Core.Presenters;

/// <summary>One named value of a <see cref="StructuredMessage"/>, with the unit it is in when the decoder knows it.</summary>
public sealed record StructuredField(string Name, string Value, string? Unit = null)
{
    /// <summary><c>temp=21.5 C</c>, or <c>state=OK</c> without a unit.</summary>
    public override string ToString() => Unit is { Length: > 0 } ? $"{Name}={Value} {Unit}" : $"{Name}={Value}";
}

/// <summary>
/// The small, optional structured form of one decoded message (docs/design/presenters.md): named fields with units,
/// alongside the text a presenter renders. A decoder may emit it; none must.
/// </summary>
public sealed record StructuredMessage(string Source, IReadOnlyList<StructuredField> Fields)
{
    /// <summary>The first field called <paramref name="name"/> (case-insensitive), or null.</summary>
    public StructuredField? Find(string name) =>
        Fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The field's value as a number (invariant culture), or null when absent or not numeric.</summary>
    public double? Number(string name) =>
        Find(name) is { } field && double.TryParse(field.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    public override string ToString() => string.Join(' ', Fields);
}

/// <summary>Optional companion to <see cref="IPresenter"/> for a decoder that also emits a <see cref="StructuredMessage"/> per decoded message.</summary>
public interface IStructuredMessageSource
{
    event EventHandler<StructuredMessage>? MessageDecoded;
}
