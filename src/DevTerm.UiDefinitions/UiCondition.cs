using System.Collections;
using System.Globalization;

namespace DevTerm.UiDefinitions;

/// <summary>
/// A visibility condition on a <see cref="UiSection"/> or <see cref="UiControl"/>
/// (<c>VisibleWhen</c>): shown only while the value named by <see cref="Id"/> — another control's
/// value, or a property of the model a form is bound to — is one of <see cref="Values"/>
/// (case-insensitive). With no <see cref="Values"/>, the value must be true (a <c>bool</c>
/// <see langword="true"/>, or the text <c>"true"</c>/<c>"1"</c>). A collection value matches when any
/// of its items does (e.g. "the presenters list contains <c>scpi</c>"). See
/// docs/design/ui-definitions.md's "Forms from one definition" section.
/// </summary>
/// <remarks>
/// A plain class holding a <see cref="List{T}"/> (no dictionary, no non-empty default list) so it
/// round-trips through both <c>System.Text.Json</c> and <c>XmlSerializer</c> — <c>XmlSerializer</c>
/// appends deserialized items to a list that already has items, which is why "no values" rather
/// than a default <c>["true"]</c> means "is true".
/// </remarks>
public sealed class UiCondition
{
    public required string Id { get; set; }

    public List<string> Values { get; set; } = [];

    /// <summary>Whether <paramref name="value"/> (the current value of <see cref="Id"/>) satisfies this condition.</summary>
    public bool IsMetBy(object? value)
    {
        if (value is not string && value is IEnumerable items)
        {
            foreach (var item in items)
            {
                if (IsMetBy(item))
                {
                    return true;
                }
            }

            return false;
        }

        var text = value switch
        {
            null => null,
            bool b => b ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };

        if (Values.Count == 0)
        {
            return string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) || text == "1";
        }

        return text is not null && Values.Any(v => string.Equals(v, text, StringComparison.OrdinalIgnoreCase));
    }
}
