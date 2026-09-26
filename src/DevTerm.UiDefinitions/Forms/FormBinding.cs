using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows.Input;

namespace DevTerm.UiDefinitions.Forms;

/// <summary>
/// Binds a generated form's controls to the model it was generated from — by <see cref="UiControl.Id"/>
/// = property name — for both front ends' form renderers, which have no shared data-binding system
/// (Terminal.Gui has none at all): reads a property as text/bool/selection, writes one back
/// (validated against the control's <see cref="ValueConstraint"/>, converted to the property's type),
/// evaluates <see cref="UiCondition"/>s, runs a command property, and raises <see cref="Changed"/>
/// whenever a bound value may have changed — relaying the model's own
/// <see cref="INotifyPropertyChanged.PropertyChanged"/>, or, for a plain model with none, after each
/// write made through this binding.
/// </summary>
/// <remarks>
/// A string-typed property always receives what was typed, even when it fails its constraint (the
/// result still reports the error): a view model like <c>ConnectionEditorViewModel</c> keeps
/// unparsed text as-is and validates it later, and rejecting a keystroke would fight the user
/// mid-edit. A property of any other type is only written once the text converts.
/// </remarks>
public sealed class FormBinding : IDisposable
{
    private readonly Dictionary<string, PropertyInfo?> _properties = new(StringComparer.Ordinal);
    private readonly INotifyPropertyChanged? _notifying;

    public FormBinding(object source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Source = source;
        _notifying = source as INotifyPropertyChanged;
        if (_notifying is not null)
        {
            _notifying.PropertyChanged += OnSourcePropertyChanged;
        }
    }

    /// <summary>The bound model.</summary>
    public object Source { get; }

    /// <summary>A bound value may have changed: the property name, or null for "anything may have".</summary>
    public event EventHandler<string?>? Changed;

    public void Dispose()
    {
        if (_notifying is not null)
        {
            _notifying.PropertyChanged -= OnSourcePropertyChanged;
        }
    }

    /// <summary>Whether the model has a readable property named <paramref name="id"/>.</summary>
    public bool Has(string id) => Property(id) is { CanRead: true };

    /// <summary>Whether <paramref name="id"/> can't be written (no public setter, or no such property).</summary>
    public bool IsReadOnly(string id) => Property(id)?.SetMethod is not { IsPublic: true };

    /// <summary>The property's current value, or null when there's no such property.</summary>
    public object? GetValue(string id) => Property(id) is { CanRead: true } property ? property.GetValue(Source) : null;

    /// <summary>The property's current value as text: invariant culture, a collection comma-joined, null as empty.</summary>
    public string GetText(string id) => GetValue(id) is { } value ? Format(value) : string.Empty;

    public bool GetBool(string id) => GetValue(id) switch
    {
        bool b => b,
        string s => string.Equals(s, "true", StringComparison.OrdinalIgnoreCase) || s == "1",
        _ => false,
    };

    /// <summary>A check list's checked options: a collection property's items, or a comma-separated text property split.</summary>
    public IReadOnlyList<string> GetSelection(string id) => GetValue(id) switch
    {
        null => [],
        string text => SplitList(text),
        IEnumerable items => [.. items.Cast<object?>().Select(i => i is null ? string.Empty : Format(i)).Where(s => s.Length > 0)],
        var other => [Format(other)],
    };

    /// <summary>
    /// Writes <paramref name="text"/> to <paramref name="control"/>'s property, validated against its
    /// constraint (<see cref="ValueValidator.ConstraintFor"/>). Returns the validation result; see the
    /// remarks for when an invalid value is still written.
    /// </summary>
    public ValueValidationResult SetText(UiControl control, string text)
    {
        ArgumentNullException.ThrowIfNull(control);
        var result = ValueValidator.Validate(ValueValidator.ConstraintFor(control), text);
        if (Property(control.Id) is not { SetMethod.IsPublic: true } property)
        {
            return result;
        }

        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (type == typeof(string))
        {
            Write(property, text);
            return result;
        }

        if (!result.IsValid)
        {
            return result;
        }

        if (!TryConvert(result.Value, property.PropertyType, out var converted))
        {
            return ValueValidationResult.Invalid(text, $"'{text.Trim()}' isn't a valid {Humanized(type)}.");
        }

        Write(property, converted);
        return result;
    }

    public void SetBool(string id, bool value)
    {
        if (Property(id) is { SetMethod.IsPublic: true } property)
        {
            Write(property, property.PropertyType == typeof(string) ? (value ? "true" : "false") : value);
        }
    }

    /// <summary>Writes a check list's checked options: joined with commas for a text property, as a list/array otherwise.</summary>
    public void SetSelection(string id, IEnumerable<string> selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        if (Property(id) is not { SetMethod.IsPublic: true } property)
        {
            return;
        }

        var items = selected.ToList();
        var type = property.PropertyType;
        object value = type == typeof(string) ? string.Join(',', items)
            : type.IsArray ? items.ToArray()
            : items;
        Write(property, value);
    }

    /// <summary>Whether <paramref name="condition"/> holds for the model's current values (no condition: always).</summary>
    public bool IsVisible(UiCondition? condition) => condition is null || condition.IsMetBy(GetValue(condition.Id));

    /// <summary>Runs the <see cref="ICommand"/> property named <paramref name="id"/>; false when there's none, or it can't execute.</summary>
    public bool Invoke(string id)
    {
        if (GetValue(id) is ICommand command && command.CanExecute(null))
        {
            command.Execute(null);
            return true;
        }

        return false;
    }

    /// <summary>A value as form text: invariant culture, a collection comma-joined.</summary>
    public static string Format(object value) => value switch
    {
        string s => s,
        bool b => b ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        IEnumerable items => string.Join(',', items.Cast<object?>().Select(i => i is null ? string.Empty : Format(i))),
        _ => value.ToString() ?? string.Empty,
    };

    /// <summary>A comma-separated list, items trimmed, blanks dropped.</summary>
    public static IReadOnlyList<string> SplitList(string? text) =>
        string.IsNullOrWhiteSpace(text) ? [] : [.. text.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0)];

    internal static bool IsInteger(Type type) =>
        type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte)
        || type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) || type == typeof(sbyte);

    internal static bool IsFloatingPoint(Type type) => type == typeof(double) || type == typeof(float) || type == typeof(decimal);

    internal static bool IsScalar(Type type) => type == typeof(string) || type.IsEnum || IsInteger(type) || IsFloatingPoint(type);

    internal static bool IsStringCollection(Type type) =>
        type == typeof(string[]) || (type.IsGenericType && typeof(IEnumerable<string>).IsAssignableFrom(type) && type.IsAssignableFrom(typeof(List<string>)));

    private PropertyInfo? Property(string id)
    {
        if (!_properties.TryGetValue(id, out var property))
        {
            property = Source.GetType().GetProperty(id, BindingFlags.Public | BindingFlags.Instance);
            _properties[id] = property;
        }

        return property;
    }

    private void Write(PropertyInfo property, object? value)
    {
        if (Equals(property.GetValue(Source), value))
        {
            return;
        }

        property.SetValue(Source, value);

        // A model that notifies has already said so itself (see OnSourcePropertyChanged).
        if (_notifying is null)
        {
            Changed?.Invoke(this, property.Name);
        }
    }

    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e) => Changed?.Invoke(this, string.IsNullOrEmpty(e.PropertyName) ? null : e.PropertyName);

    private static bool TryConvert(string text, Type targetType, out object? value)
    {
        var type = Nullable.GetUnderlyingType(targetType) ?? targetType;
        value = null;
        var trimmed = text.Trim();
        if (trimmed.Length == 0 && (Nullable.GetUnderlyingType(targetType) is not null || !targetType.IsValueType))
        {
            return true;
        }

        if (type.IsEnum)
        {
            if (Enum.TryParse(type, trimmed, ignoreCase: true, out var parsed))
            {
                value = parsed;
                return true;
            }

            return false;
        }

        if (IsStringCollection(type))
        {
            var items = SplitList(text);
            value = type.IsArray ? items.ToArray() : items.ToList();
            return true;
        }

        try
        {
            value = Convert.ChangeType(trimmed, type, CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or InvalidCastException)
        {
            return false;
        }
    }

    private static string Humanized(Type type) =>
        IsInteger(type) ? "whole number" : IsFloatingPoint(type) ? "number" : type.IsEnum ? "choice" : "value";
}
