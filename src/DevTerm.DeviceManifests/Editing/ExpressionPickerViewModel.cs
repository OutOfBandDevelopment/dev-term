using System.Globalization;
using DevTerm.UiDefinitions;

namespace DevTerm.DeviceManifests.Editing;

/// <summary>One function the expression language offers, with the snippet the picker inserts for it.</summary>
public sealed record ExpressionFunction(string Name, string Signature, string Description, string Snippet, int CaretFromEnd = 1);

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
/// docs/design/features/expression-picker-paths-and-cel.md.
/// </summary>
/// <summary>What the text being picked is: one expression, a chart's channel list, a semicolon-separated list of expressions, or one value id.</summary>
public enum PickerMode
{
    Expression,
    Channels,
    ExpressionList,
    ValueId,
}

public sealed class ExpressionPickerViewModel
{
    private const int _buttonCount = 5;

    private static readonly IReadOnlyList<ExpressionFunction> _functions =
    [
        new("round", "round(x) / round(x, n)", "Round to n decimal places (0 when omitted).", "round()"),
        new("min", "min(a, b, ...)", "The smallest argument.", "min()"),
        new("max", "max(a, b, ...)", "The largest argument.", "max()"),
        new("abs", "abs(x)", "The absolute value.", "abs()"),
        new("if", "if(cond, a, b)", "a when cond is non-zero, otherwise b.", "if()"),
        new("matches", "matches(text, regex)", "1 when the text matches the regular expression (a literal regex is checked as you type).", "matches(, '')", CaretFromEnd: 5),
        new("contains", "contains(text, part)", "1 when the text contains the part.", "contains(, '')", CaretFromEnd: 5),
        new("startsWith", "startsWith(text, prefix)", "1 when the text starts with the prefix.", "startsWith(, '')", CaretFromEnd: 5),
        new("endsWith", "endsWith(text, suffix)", "1 when the text ends with the suffix.", "endsWith(, '')", CaretFromEnd: 5),
        new("size", "size(x)", "The length of a text or a list.", "size()"),
        new("number", "number(text)", "The number at the start of the text.", "number()"),
        new("string", "string(x)", "The value as text.", "string()"),
        new("has", "has({id})", "1 when the value has arrived, 0 while it is absent.", "has()"),
        new("split", "split(text, separator)", "The text cut into a list at each separator.", "split(, ',')", CaretFromEnd: 6),
        new("join", "join(list, separator)", "The list's items joined into one text.", "join(, ',')", CaretFromEnd: 6),
    ];

    private readonly IReadOnlyList<PickerPath> _allPaths;
    private readonly Dictionary<string, ValuePath> _byId;
    private RecordedSamples? _recorded;
    private string? _textResult;
    private string _text;
    private string _filter = string.Empty;
    private int _caret;
    private int _selectionLength;
    private int _step;

    public ExpressionPickerViewModel(IEnumerable<ValuePath> paths, string? text = null, int seed = 0, PickerMode mode = PickerMode.Expression)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var list = paths.ToList();
        _byId = list.GroupBy(p => p.Path, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        Seed = seed;
        Mode = mode;
        _allPaths = [.. _byId.Values.Select(p => new PickerPath(p, Describe(p), Example(p, seed)))];
        _text = text ?? string.Empty;
        _caret = _text.Length;
        Recompute();
    }

    /// <summary>What the text is; decides how a chosen path is inserted and how the text is checked.</summary>
    public PickerMode Mode { get; }

    /// <summary>True when the text is a chart's channel list (see <see cref="PickerMode.Channels"/>).</summary>
    public bool IsChannelList => Mode == PickerMode.Channels;

    /// <summary>True when the function buttons apply (a single expression or a list of them).</summary>
    public bool ShowsFunctions => Mode is PickerMode.Expression or PickerMode.ExpressionList;

    /// <summary>True when there is one expression whose result against sample data can be shown.</summary>
    public bool ShowsResult => Mode == PickerMode.Expression;

    /// <summary>The dialog title.</summary>
    public string Title => Mode switch
    {
        PickerMode.Channels => "Channels",
        PickerMode.ExpressionList => "Expressions",
        PickerMode.ValueId => "Value",
        _ => "Expression",
    };

    /// <summary>The label over the text box.</summary>
    public string Prompt => Mode switch
    {
        PickerMode.Channels => "Channels (id[:label[:#RRGGBB[:expression]]], separated by ;):",
        PickerMode.ExpressionList => "Expressions (separated by ;, blank keeps a field's own value):",
        PickerMode.ValueId => "Value id:",
        _ => "Expression:",
    };

    /// <summary>The hint on the path list for how to choose.</summary>
    public string ChooseHint => Mode switch
    {
        PickerMode.Channels => "Double-click a value to add it as a channel",
        PickerMode.ValueId => "Double-click a value to use it",
        _ => "Double-click a value to insert it at the caret",
    };

    /// <summary>Raised after any change to the text, the filter, the sample step or the caret-visible state.</summary>
    public event EventHandler? Changed;

    /// <summary>The functions the picker offers, in display order.</summary>
    public static IReadOnlyList<ExpressionFunction> Functions => _functions;

    /// <summary>The numeric functions every picker shows as a button of their own.</summary>
    public static IReadOnlyList<ExpressionFunction> ButtonFunctions { get; } = [.. _functions.Take(_buttonCount)];

    /// <summary>The text and list functions, offered from one "more" control to keep the function row short.</summary>
    public static IReadOnlyList<ExpressionFunction> MoreFunctions { get; } = [.. _functions.Skip(_buttonCount)];

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
    public string ResultText => _textResult is not null ? $"\"{_textResult}\"" : Result is { } r
        ? double.IsFinite(r) ? r.ToString("0.######", CultureInfo.InvariantCulture) : r.ToString(CultureInfo.InvariantCulture)
        : "-";

    /// <summary>A one-line status for the diagnostics area.</summary>
    public string Diagnostics =>
        IsEmpty ? "Empty" :
        !IsValid ? $"Error: {Error}" :
        Warnings.Count > 0 ? "OK, with warnings: " + string.Join(" ", Warnings) :
        "OK";

    /// <summary>The sample values the result is evaluated against, so a form can show them next to the paths.</summary>
    public IReadOnlyDictionary<string, double> SampleValues => SampleDataGenerator.Values(_byId.Values, Seed, _step, _recorded);

    /// <summary>
    /// A recording to draw sample values from instead of generated ones (paths it has no values for still generate). Null uses
    /// generated values only; setting it restarts the walk and re-evaluates.
    /// </summary>
    public RecordedSamples? Recording
    {
        get => _recorded;
        set
        {
            _recorded = value;
            _step = 0;
            Recompute();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

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
        if (Mode == PickerMode.ValueId)
        {
            _text = path.Path.Path;
            _caret = _text.Length;
            _selectionLength = 0;
            Recompute();
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (IsChannelList)
        {
            var before = _text.AsSpan(0, Math.Min(_caret, _text.Length)).TrimEnd();
            var channel = before.Length > 0 && before[^1] != ';' ? "; " + path.Path.Path : path.Path.Path;
            InsertAtCaret(channel, channel.Length);
            return;
        }

        InsertAtCaret(path.Reference, path.Reference.Length);
    }

    /// <summary>Inserts a function's snippet at the caret and leaves the caret where the function's first argument goes.</summary>
    public void InsertFunction(ExpressionFunction function)
    {
        ArgumentNullException.ThrowIfNull(function);
        InsertAtCaret(function.Snippet, function.Snippet.Length - function.CaretFromEnd);
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
        _textResult = null;

        if (IsEmpty)
        {
            IsValid = false;
            Error = null;
            return;
        }

        if (Mode == PickerMode.ValueId)
        {
            IsValid = true;
            Error = null;
            var id = _text.Trim();
            Warnings = _byId.ContainsKey(id) ? [] : [$"'{id}' is not published by anything in this manifest."];
            return;
        }

        if (IsChannelList)
        {
            RecomputeChannels();
            return;
        }

        if (Mode == PickerMode.ExpressionList)
        {
            RecomputeExpressionList();
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
            if (!_byId.ContainsKey(id))
            {
                warnings.Add($"'{id}' is not published by anything in this manifest.");
            }
        }

        Warnings = warnings;
        var numbers = SampleDataGenerator.Values(_byId.Values, Seed, _step, _recorded);
        var texts = SampleDataGenerator.TextValues(_byId.Values, Seed, _step, _recorded);
        Result = expression.Evaluate(numbers, texts);
        var shown = double.IsNaN(Result.Value) ? expression.EvaluateToText(numbers, texts) : null;
        _textResult = shown == "NaN" ? null : shown;
    }

    private void RecomputeExpressionList()
    {
        var warnings = new List<string>();
        var index = 0;
        foreach (var item in _text.Split(';').Select(i => i.Trim()))
        {
            index++;
            if (item.Length == 0)
            {
                continue;
            }

            if (!Expression.TryParse(item, out var expression, out var error))
            {
                IsValid = false;
                Error = $"Item {index}: {error}";
                return;
            }

            warnings.AddRange(expression!.ReferencedIds.Where(r => !_byId.ContainsKey(r)).Select(r => $"'{r}' is not published by anything in this manifest."));
        }

        IsValid = true;
        Error = null;
        Warnings = warnings;
    }

    private void RecomputeChannels()
    {
        var warnings = new List<string>();
        var count = 0;
        foreach (var item in _text.Split(';').Select(i => i.Trim()).Where(i => i.Length > 0))
        {
            count++;
            var bits = item.Split(':');
            var id = bits[0].Trim();
            if (id.Length == 0)
            {
                IsValid = false;
                Error = $"Channel {count} has no id.";
                return;
            }

            if (bits.Length > 3 && bits[3].Trim() is { Length: > 0 } expressionText)
            {
                if (!Expression.TryParse(expressionText, out var expression, out var error))
                {
                    IsValid = false;
                    Error = $"Channel '{id}': {error}";
                    return;
                }

                warnings.AddRange(expression!.ReferencedIds.Where(r => !_byId.ContainsKey(r)).Select(r => $"'{r}' is not published by anything in this manifest."));
            }
            else if (!_byId.ContainsKey(id))
            {
                warnings.Add($"'{id}' is not published by anything in this manifest.");
            }
        }

        IsValid = count > 0;
        Error = null;
        Warnings = warnings;
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
