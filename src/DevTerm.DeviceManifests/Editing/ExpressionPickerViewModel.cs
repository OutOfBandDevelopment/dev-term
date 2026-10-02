using System.Globalization;
using DevTerm.UiDefinitions;

namespace DevTerm.DeviceManifests.Editing;

/// <summary>One function the expression language offers, with the snippet the picker inserts for it.</summary>
public sealed record ExpressionFunction(string Name, string Signature, string Description, string Snippet);

/// <summary>One row of the picker's path list: a <see cref="ValuePath"/> plus how to show it.</summary>
public sealed record PickerPath(ValuePath Path, string Detail, string? Example)
{
    /// <summary>The text inserted into an expression for this path (<c>{id}</c>).</summary>
    public string Reference => "{" + Path.Path + "}";

    public override string ToString() => Detail.Length == 0 ? Path.Path : $"{Path.Path}  ({Detail})";
}

/// <summary>
/// The expression picker's state, with no UI framework in it: a searchable list of the paths an expression may read
/// (from <see cref="ValuePathCatalog"/>), the functions the language offers, insert-at-caret editing, live
/// parse/reference diagnostics, and a result evaluated against <see cref="SampleDataGenerator"/> values. The Terminal.Gui and
/// WPF forms render this the way they render the other editor forms. See
/// docs/design/proposals/expression-picker-paths-and-cel.md.
/// </summary>
public sealed class ExpressionPickerViewModel
{
    private static readonly IReadOnlyList<ExpressionFunction> _functions =
    [
        new("round", "round(x) / round(x, n)", "Round to n decimal places (0 when omitted).", "round()"),
        new("min", "min(a, b, ...)", "The smallest argument.", "min()"),
        new("max", "max(a, b, ...)", "The largest argument.", "max()"),
        new("abs", "abs(x)", "The absolute value.", "abs()"),
        new("if", "if(cond, a, b)", "a when cond is non-zero, otherwise b.", "if()"),
    ];

    private readonly IReadOnlyList<PickerPath> _allPaths;
    private readonly Dictionary<string, ValuePath> _byId;
    private string _text;
    private string _filter = string.Empty;
    private int _caret;
    private int _selectionLength;
    private int _step;

    public ExpressionPickerViewModel(IEnumerable<ValuePath> paths, string? text = null, int seed = 0)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var list = paths.ToList();
        _byId = list.GroupBy(p => p.Path, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        Seed = seed;
        _allPaths = [.. _byId.Values.Select(p => new PickerPath(p, Describe(p), Example(p, seed)))];
        _text = text ?? string.Empty;
        _caret = _text.Length;
        Recompute();
    }

    /// <summary>Raised after any change to the text, the filter, the sample step or the caret-visible state.</summary>
    public event EventHandler? Changed;

    /// <summary>The functions the picker offers, in display order.</summary>
    public static IReadOnlyList<ExpressionFunction> Functions => _functions;

    /// <summary>The seed the sample data is generated from, so a screenshot or test shows the same values every time.</summary>
    public int Seed { get; }

    /// <summary>The expression text being edited.</summary>
    public string Text
    {
        get => _text;
        set
        {
            value ??= string.Empty;
            if (value == _text)
            {
                return;
            }

            _text = value;
            _caret = Math.Min(_caret, _text.Length);
            _selectionLength = Math.Min(_selectionLength, _text.Length - _caret);
            Recompute();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The caret position (0..Text.Length). Out-of-range values are clamped.</summary>
    public int CaretIndex
    {
        get => _caret;
        set
        {
            _caret = Math.Clamp(value, 0, _text.Length);
            _selectionLength = Math.Min(_selectionLength, _text.Length - _caret);
        }
    }

    /// <summary>How many characters starting at <see cref="CaretIndex"/> an insert replaces (0 for a plain caret).</summary>
    public int SelectionLength
    {
        get => _selectionLength;
        set => _selectionLength = Math.Clamp(value, 0, _text.Length - _caret);
    }

    /// <summary>Narrows <see cref="Paths"/> to those whose id, label or unit contains this text (case-insensitive).</summary>
    public string Filter
    {
        get => _filter;
        set
        {
            value ??= string.Empty;
            if (value == _filter)
            {
                return;
            }

            _filter = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Every path the expression may read, unfiltered.</summary>
    public IReadOnlyList<PickerPath> AllPaths => _allPaths;

    /// <summary>The paths matching <see cref="Filter"/>, in catalog order.</summary>
    public IReadOnlyList<PickerPath> Paths => string.IsNullOrWhiteSpace(_filter)
        ? _allPaths
        : [.. _allPaths.Where(p => Matches(p.Path, _filter.Trim()))];

    /// <summary>True when the text parses.</summary>
    public bool IsValid { get; private set; }

    /// <summary>The parse error, or null when the text parses (or is empty: see <see cref="IsEmpty"/>).</summary>
    public string? Error { get; private set; }

    /// <summary>Non-blocking problems with a parseable expression: ids nothing publishes, text-valued paths read as numbers.</summary>
    public IReadOnlyList<string> Warnings { get; private set; } = [];

    /// <summary>True when there is nothing to parse yet.</summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(_text);

    /// <summary>The expression's value against the current sample data, or null when it does not parse.</summary>
    public double? Result { get; private set; }

    /// <summary>The result as shown: invariant culture, up to 6 significant decimals, or a dash when there is none.</summary>
    public string ResultText => Result is { } r
        ? double.IsFinite(r) ? r.ToString("0.######", CultureInfo.InvariantCulture) : r.ToString(CultureInfo.InvariantCulture)
        : "-";

    /// <summary>A one-line status for the diagnostics area.</summary>
    public string Diagnostics =>
        IsEmpty ? "Empty" :
        !IsValid ? $"Error: {Error}" :
        Warnings.Count > 0 ? "OK, with warnings: " + string.Join(" ", Warnings) :
        "OK";

    /// <summary>The sample values the result is evaluated against, so a form can show them next to the paths.</summary>
    public IReadOnlyDictionary<string, double> SampleValues => SampleDataGenerator.Values(_byId.Values, Seed, _step);

    /// <summary>Moves the sample data to the next point in its smooth walk and re-evaluates.</summary>
    public void NextSample()
    {
        _step++;
        Recompute();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Inserts <c>{id}</c> for <paramref name="path"/> at the caret, replacing any selection.</summary>
    public void InsertPath(PickerPath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        InsertAtCaret(path.Reference, path.Reference.Length);
    }

    /// <summary>Inserts a function's snippet at the caret and leaves the caret between its parentheses.</summary>
    public void InsertFunction(ExpressionFunction function)
    {
        ArgumentNullException.ThrowIfNull(function);
        InsertAtCaret(function.Snippet, function.Snippet.Length - 1);
    }

    /// <summary>Inserts an operator or literal at the caret (spaced when it is a binary operator).</summary>
    public void InsertOperator(string op)
    {
        ArgumentException.ThrowIfNullOrEmpty(op);
        var text = op is "(" or ")" ? op : $" {op} ";
        InsertAtCaret(text, text.Length);
    }

    private void InsertAtCaret(string insert, int caretAfter)
    {
        var start = _caret;
        var length = Math.Min(_selectionLength, _text.Length - start);
        _text = string.Concat(_text.AsSpan(0, start), insert, _text.AsSpan(start + length));
        _caret = start + caretAfter;
        _selectionLength = 0;
        Recompute();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Recompute()
    {
        Warnings = [];
        Result = null;

        if (IsEmpty)
        {
            IsValid = false;
            Error = null;
            return;
        }

        if (!Expression.TryParse(_text, out var expression, out var error))
        {
            IsValid = false;
            Error = error;
            return;
        }

        IsValid = true;
        Error = null;

        var warnings = new List<string>();
        foreach (var id in expression!.ReferencedIds)
        {
            if (!_byId.TryGetValue(id, out var path))
            {
                warnings.Add($"'{id}' is not published by anything in this manifest.");
            }
            else if (path.Type == ValuePathType.Text && path.Choices is null)
            {
                warnings.Add($"'{id}' is text, which an expression reads as 0.");
            }
        }

        Warnings = warnings;
        Result = expression.Evaluate(SampleDataGenerator.Values(_byId.Values, Seed, _step));
    }

    private static bool Matches(ValuePath path, string filter) =>
        path.Path.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || path.Origin.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || (path.Unit?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);

    private static string Describe(ValuePath path)
    {
        var parts = new List<string> { path.Type.ToString().ToLowerInvariant() };
        if (path.Unit is { Length: > 0 })
        {
            parts.Add(path.Unit);
        }

        if (path.Minimum is { } min && path.Maximum is { } max)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{min:0.###} to {max:0.###}"));
        }

        if (path.Choices is { Count: > 0 } choices)
        {
            parts.Add(string.Join("/", choices));
        }

        return string.Join(", ", parts);
    }

    private static string? Example(ValuePath path, int seed) =>
        SampleDataGenerator.Value(path, seed, 0) is { } value
            ? value.ToString("0.###", CultureInfo.InvariantCulture)
            : SampleDataGenerator.Text(path, 0);
}
