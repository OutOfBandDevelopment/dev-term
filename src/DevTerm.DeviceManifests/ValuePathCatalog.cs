using System.Text.RegularExpressions;
using DevTerm.UiDefinitions;

namespace DevTerm.DeviceManifests;

/// <summary>The kind of value a <see cref="ValuePath"/> holds, as far as the manifest can tell.</summary>
public enum ValuePathType
{
    Number,
    Text,
    Boolean,
}

/// <summary>Where a <see cref="ValuePath"/> comes from in the manifest.</summary>
public enum ValuePathSource
{
    /// <summary>A response pattern's name, or one of its named capture groups.</summary>
    ResponsePattern,

    /// <summary>The id a query command's reply is published under.</summary>
    QueryReply,

    /// <summary>A panel control's id (an indicator is a published value; every control's current value is readable by a button's parameter expressions).</summary>
    Control,
}

/// <summary>
/// One value an expression may legally read, with whatever the manifest says about it.
/// <see cref="Type"/> for a regex capture is inferred from the capture's pattern, so it is a hint rather than a guarantee.
/// </summary>
public sealed record ValuePath(
    string Path,
    ValuePathType Type,
    ValuePathSource Source,
    string Origin,
    string? Unit = null,
    double? Minimum = null,
    double? Maximum = null,
    IReadOnlyList<string>? Choices = null);

/// <summary>
/// Enumerates every path an <see cref="Expression"/> in a <see cref="DeviceManifest"/> can reference, from the manifest alone
/// (no connected device). It is the single source for the expression picker, sample-data generation and the validator's
/// unknown-reference check. See docs/design/proposals/expression-picker-paths-and-cel.md.
/// </summary>
public static partial class ValuePathCatalog
{
    /// <summary>Every readable path, de-duplicated by <see cref="ValuePath.Path"/> (first source wins), in a stable order: patterns, then query replies, then controls.</summary>
    public static IReadOnlyList<ValuePath> Enumerate(DeviceManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var paths = new List<ValuePath>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(ValuePath path)
        {
            if (!string.IsNullOrWhiteSpace(path.Path) && seen.Add(path.Path))
            {
                paths.Add(path);
            }
        }

        foreach (var pattern in manifest.Inbound?.Patterns ?? [])
        {
            foreach (var path in FromPattern(pattern))
            {
                Add(path);
            }
        }

        foreach (var command in manifest.OutboundCommands)
        {
            if (command.EffectiveReplyId is { Length: > 0 } replyId)
            {
                Add(new ValuePath(replyId, ValuePathType.Text, ValuePathSource.QueryReply, command.EffectiveId));
            }
        }

        foreach (var control in manifest.Ui?.Sections.SelectMany(s => s.Controls) ?? [])
        {
            Add(FromControl(control));
        }

        return paths;
    }

    /// <summary>The paths one response pattern publishes: its name (first capture group, or the whole match) and every named group.</summary>
    public static IReadOnlyList<ValuePath> FromPattern(ResponsePattern pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        var groups = ScanGroups(pattern.Match ?? string.Empty);
        var results = new List<ValuePath>();

        var primary = groups.FirstOrDefault(g => g.Name is null);
        var wholeMatchBody = primary?.Body ?? pattern.Match ?? string.Empty;
        results.Add(new ValuePath(pattern.Name, Classify(wholeMatchBody), ValuePathSource.ResponsePattern, pattern.Name));

        foreach (var group in groups.Where(g => g.Name is not null))
        {
            results.Add(new ValuePath(group.Name!, Classify(group.Body), ValuePathSource.ResponsePattern, pattern.Name));
        }

        return results;
    }

    private static ValuePath FromControl(UiControl control) => control switch
    {
        SliderControl s => new ValuePath(s.Id, ValuePathType.Number, ValuePathSource.Control, s.Label, s.Unit, s.Minimum, s.Maximum),
        NumericControl n => new ValuePath(n.Id, ValuePathType.Number, ValuePathSource.Control, n.Label, n.Unit, n.Minimum, n.Maximum),
        ToggleControl t => new ValuePath(t.Id, ValuePathType.Boolean, ValuePathSource.Control, t.Label),
        ChoiceControl c => new ValuePath(c.Id, ValuePathType.Text, ValuePathSource.Control, c.Label, Choices: c.Options),
        _ => new ValuePath(control.Id, ValuePathType.Text, ValuePathSource.Control, control.Label),
    };

    private sealed record CaptureGroup(int Start, string? Name, string Body);

    // Finds capturing groups with their body text. Non-capturing/lookaround groups ("(?:", "(?=", ...) are skipped; a
    // named group is "(?<name>" or "(?'name'" (not "(?<=" / "(?<!").
    private static List<CaptureGroup> ScanGroups(string pattern)
    {
        var groups = new List<CaptureGroup>();
        var stack = new Stack<(int Open, int BodyStart, string? Name, bool Captures)>();
        var inClass = false;

        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '\\')
            {
                i++;
                continue;
            }

            if (inClass)
            {
                inClass = c != ']';
                continue;
            }

            if (c == '[')
            {
                inClass = true;
            }
            else if (c == '(')
            {
                if (i + 1 < pattern.Length && pattern[i + 1] == '?')
                {
                    var named = NamedGroupStart().Match(pattern, i);
                    stack.Push(named.Success
                        ? (i, i + named.Length, named.Groups["n"].Value, true)
                        : (i, i + 1, null, false));
                }
                else
                {
                    stack.Push((i, i + 1, null, true));
                }
            }
            else if (c == ')' && stack.Count > 0)
            {
                var (open, start, name, captures) = stack.Pop();
                if (captures)
                {
                    groups.Add(new CaptureGroup(open, name, pattern[start..i]));
                }
            }
        }

        return groups.OrderBy(g => g.Start).ToList();
    }

    private static ValuePathType Classify(string body) =>
        NumericBody().IsMatch(body) && (body.Contains(@"\d", StringComparison.Ordinal) || body.Contains("0-9", StringComparison.Ordinal))
            ? ValuePathType.Number
            : ValuePathType.Text;

    [GeneratedRegex(@"\G\(\?(?:<(?<n>[A-Za-z_]\w*)>|'(?<n>[A-Za-z_]\w*)')")]
    private static partial Regex NamedGroupStart();

    [GeneratedRegex(@"^[\\d\[\]0-9.+\-eE*?{},|()\s]*$")]
    private static partial Regex NumericBody();
}
