using System.Text;

namespace DevTerm.Core.Control;

/// <summary>
/// Joins/splits the several values a <see cref="ButtonControl"/>'s <c>ParameterFieldIds</c> collects
/// into the single string <see cref="IControlSurface.InvokeAsync"/> carries, escaping a literal comma
/// or backslash in any one value (backslash-escaped, the same convention as most shell/CSV quoting)
/// so it survives the round trip intact instead of being mistaken for the separator between values —
/// see docs/bugs/resolved/024-comma-in-text-parameter.md. <see cref="Split"/> mirrors
/// <see cref="string.Split(char)"/>'s own behavior for an empty input (a single empty-string element,
/// never zero elements) so existing "no value at index i, use the parameter's default" logic keeps
/// working unchanged.
/// </summary>
public static class ParameterValueList
{
    /// <summary>Escapes and comma-joins <paramref name="values"/> — the exact string a matching <see cref="Split"/> reconstructs.</summary>
    public static string Join(IEnumerable<string> values) => string.Join(',', values.Select(Escape));

    /// <summary>Reverses <see cref="Join"/>: unescapes each comma-separated segment of <paramref name="joined"/> back into its original value.</summary>
    public static string[] Split(string? joined)
    {
        var text = joined ?? string.Empty;
        var results = new List<string>();
        var current = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\\' && i + 1 < text.Length)
            {
                current.Append(text[++i]);
            }
            else if (c == ',')
            {
                results.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        results.Add(current.ToString());
        return [.. results];
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace(",", "\\,", StringComparison.Ordinal);
}
