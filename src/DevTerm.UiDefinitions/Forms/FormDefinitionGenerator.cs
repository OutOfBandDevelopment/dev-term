using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Windows.Input;

namespace DevTerm.UiDefinitions.Forms;

/// <summary>
/// Turns an annotated model into the same <see cref="UiDefinition"/> a device panel is made of — one
/// section per <c>[Category]</c>, one control per property — so a settings form is declared once, on
/// the model, and rendered by each front end's generic form renderer instead of being hand-built
/// per front end. Reads <c>[Category]</c>/<c>[DisplayName]</c>/<c>[Description]</c>/<c>[Browsable]</c>
/// plus <see cref="FormFieldAttribute"/>/<see cref="FormSectionAttribute"/>; a control's
/// <see cref="UiControl.Id"/> is the property name, which is what <see cref="FormBinding"/> binds it
/// to. See docs/design/ui-definitions.md's "Forms from one definition".
/// </summary>
public static class FormDefinitionGenerator
{
    /// <summary>Generates the form for <typeparamref name="T"/>, defaults from a new instance; see <see cref="Generate(Type, object?, string?)"/>.</summary>
    public static UiDefinition Generate<T>()
        where T : class => Generate(typeof(T));

    /// <summary>Generates the form for <paramref name="instance"/>'s type, its defaults and choice lists read from <paramref name="instance"/>; see <see cref="Generate(Type, object?, string?)"/>.</summary>
    public static UiDefinition Generate(object instance, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return Generate(instance.GetType(), instance, name);
    }

    /// <summary>
    /// Generates the form for <paramref name="modelType"/>. <paramref name="instance"/>, when given,
    /// supplies each field's default value and the options named by
    /// <see cref="FormFieldAttribute.OptionsFrom"/> (an instance property can only be read from an
    /// instance); without one, defaults come from a new instance when the type has a public
    /// parameterless constructor.
    /// </summary>
    public static UiDefinition Generate(Type modelType, object? instance = null, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(modelType);

        var defaults = instance ?? TryCreate(modelType);
        var properties = modelType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0)
            .OrderBy(p => p.MetadataToken)
            .ToList();
        var optIn = properties.Any(p => p.GetCustomAttribute<FormFieldAttribute>() is not null);
        var sectionAttributes = modelType.GetCustomAttributes<FormSectionAttribute>()
            .GroupBy(a => a.Category, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var fields = new List<(string Category, int Order, int Index, UiControl Control)>();
        for (var index = 0; index < properties.Count; index++)
        {
            var property = properties[index];
            var field = property.GetCustomAttribute<FormFieldAttribute>();
            if ((optIn && field is null) || property.GetCustomAttribute<BrowsableAttribute>() is { Browsable: false })
            {
                continue;
            }

            if (BuildControl(property, field ?? new FormFieldAttribute(), defaults, explicitlyDeclared: field is not null) is not { } control)
            {
                continue;
            }

            var category = property.GetCustomAttribute<CategoryAttribute>()?.Category ?? string.Empty;
            fields.Add((category, field?.Order ?? 0, index, control));
        }

        var sections = fields
            .GroupBy(f => f.Category, StringComparer.Ordinal)
            .Select((group, appearance) => (Group: group, Appearance: appearance))
            .OrderBy(s => sectionAttributes.TryGetValue(s.Group.Key, out var a) ? a.Order : 1000 + s.Appearance)
            .ThenBy(s => s.Appearance)
            .Select(s =>
            {
                sectionAttributes.TryGetValue(s.Group.Key, out var attribute);
                return new UiSection
                {
                    Label = attribute?.Label ?? (s.Group.Key.Length > 0 ? s.Group.Key : null),
                    VisibleWhen = Condition(attribute?.VisibleWhen, attribute?.VisibleWhenValues),
                    Controls = [.. s.Group.OrderBy(f => f.Order).ThenBy(f => f.Index).Select(f => f.Control)],
                };
            })
            .ToList();

        return new UiDefinition
        {
            Name = name ?? modelType.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? modelType.Name,
            Description = modelType.GetCustomAttribute<DescriptionAttribute>()?.Description,
            Sections = sections,
        };
    }

    /// <summary><c>"DataBits"</c> → <c>"Data bits"</c>: a property name as a label when it has no <c>[DisplayName]</c>.</summary>
    public static string Humanize(string propertyName)
    {
        ArgumentNullException.ThrowIfNull(propertyName);

        // Split before an upper-case letter that follows a lower-case one ("Data|Bits") or that
        // starts a word after an acronym ("USB|Device" in "USBDevice").
        var words = new List<string>();
        var word = new StringBuilder();
        for (var i = 0; i < propertyName.Length; i++)
        {
            var c = propertyName[i];
            var startsWord = i > 0 && char.IsUpper(c)
                && (char.IsLower(propertyName[i - 1]) || (i + 1 < propertyName.Length && char.IsLower(propertyName[i + 1]) && char.IsUpper(propertyName[i - 1])));
            if (startsWord && word.Length > 0)
            {
                words.Add(word.ToString());
                word.Clear();
            }

            word.Append(c);
        }

        if (word.Length > 0)
        {
            words.Add(word.ToString());
        }

        // Later words lower-cased ("Data bits"), except an acronym ("Read timeout (ms)" needs a
        // [DisplayName]; "USB" stays "USB").
        return string.Join(' ', words.Select((w, i) => i == 0 || (w.Length > 1 && w.All(char.IsUpper)) ? w : char.ToLowerInvariant(w[0]) + w[1..]));
    }

    private static object? TryCreate(Type type)
    {
        if (type.IsAbstract || type.GetConstructor(Type.EmptyTypes) is null)
        {
            return null;
        }

        try
        {
            return Activator.CreateInstance(type);
        }
        catch (TargetInvocationException)
        {
            return null;
        }
    }

    private static UiCondition? Condition(string? id, string[]? values) =>
        string.IsNullOrWhiteSpace(id) ? null : new UiCondition { Id = id, Values = [.. values ?? []] };

    private static UiControl? BuildControl(PropertyInfo property, FormFieldAttribute field, object? defaults, bool explicitlyDeclared)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        var readOnly = property.SetMethod is not { IsPublic: true };
        var kind = field.Kind == FormFieldKind.Auto ? InferKind(type, readOnly, field) : field.Kind;
        if (kind is null)
        {
            return null;
        }

        // An unannotated model (every browsable property is a field) only gets fields for the types
        // a form can actually edit; a declared field of another type is the host's to render.
        if (!explicitlyDeclared && readOnly)
        {
            return null;
        }

        var id = property.Name;
        var label = property.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? Humanize(property.Name);
        var description = property.GetCustomAttribute<DescriptionAttribute>()?.Description;
        var current = defaults is null ? null : SafeGet(property, defaults);
        var visibleWhen = Condition(field.VisibleWhen, field.VisibleWhenValues);
        double? minimum = double.IsNaN(field.Minimum) ? null : field.Minimum;
        double? maximum = double.IsNaN(field.Maximum) ? null : field.Maximum;

        UiControl control = kind switch
        {
            FormFieldKind.Toggle => new ToggleControl { Id = id, Label = label, DefaultValue = current is true },
            FormFieldKind.Choice => new ChoiceControl
            {
                Id = id,
                Label = label,
                Options = [.. Options(property.DeclaringType!, type, field.OptionsFrom, defaults)],
                Style = field.ChoiceStyle,
                DefaultValue = current is null ? null : FormBinding.Format(current),
            },
            FormFieldKind.Numeric => new NumericControl
            {
                Id = id,
                Label = label,
                Minimum = minimum ?? double.MinValue,
                Maximum = maximum ?? double.MaxValue,
                DefaultValue = ToDouble(current),
                Unit = field.Unit,
            },
            FormFieldKind.Slider => new SliderControl
            {
                Id = id,
                Label = label,
                Minimum = minimum ?? 0,
                Maximum = maximum ?? 100,
                Step = field.Step,
                DefaultValue = ToDouble(current),
                Unit = field.Unit,
            },
            FormFieldKind.Indicator => new IndicatorControl
            {
                Id = id,
                Label = label,
                DefaultValue = current is null ? null : FormBinding.Format(current),
                Style = field.Warning ? IndicatorStyle.Warning : IndicatorStyle.Plain,
            },
            FormFieldKind.Button => new ButtonControl { Id = id, Label = label },
            _ => new TextFieldControl
            {
                Id = id,
                Label = label,
                DefaultValue = current is null ? null : FormBinding.Format(current),
                MaxLength = field.MaxLength > 0 ? field.MaxLength : null,
                Constraint = Constraint(type, field, minimum, maximum),
            },
        };

        control.Description = description;
        control.VisibleWhen = visibleWhen;
        return control;
    }

    private static FormFieldKind? InferKind(Type type, bool readOnly, FormFieldAttribute field)
    {
        if (typeof(ICommand).IsAssignableFrom(type))
        {
            return FormFieldKind.Button;
        }

        if (readOnly)
        {
            return FormFieldKind.Indicator;
        }

        if (field.OptionsFrom is not null || type.IsEnum)
        {
            return FormFieldKind.Choice;
        }

        if (type == typeof(bool))
        {
            return FormFieldKind.Toggle;
        }

        return FormBinding.IsScalar(type) || FormBinding.IsStringCollection(type) ? FormFieldKind.TextField : null;
    }

    private static ValueConstraint? Constraint(Type type, FormFieldAttribute field, double? minimum, double? maximum)
    {
        var kind = field.ValueKind != ValueKind.Text ? field.ValueKind
            : FormBinding.IsInteger(type) ? ValueKind.Integer
            : FormBinding.IsFloatingPoint(type) ? ValueKind.Number
            : ValueKind.Text;

        return kind == ValueKind.Text && minimum is null && maximum is null
            ? null
            : new ValueConstraint { Kind = kind, Minimum = minimum, Maximum = maximum };
    }

    private static IEnumerable<string> Options(Type declaringType, Type propertyType, string? optionsFrom, object? instance)
    {
        if (optionsFrom is not null)
        {
            var source = declaringType.GetProperty(optionsFrom, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                ?? throw new InvalidOperationException($"'{declaringType.Name}.{optionsFrom}' (named by [FormField(OptionsFrom)]) doesn't exist.");
            var target = source.GetMethod?.IsStatic == true ? null : instance;
            if (source.GetMethod?.IsStatic != true && target is null)
            {
                return [];
            }

            return source.GetValue(target) is IEnumerable items ? items.Cast<object?>().Select(i => i is null ? string.Empty : FormBinding.Format(i)).ToList() : [];
        }

        return propertyType.IsEnum ? Enum.GetNames(propertyType) : [];
    }

    private static object? SafeGet(PropertyInfo property, object instance)
    {
        try
        {
            return property.GetValue(instance);
        }
        catch (TargetInvocationException)
        {
            return null;
        }
    }

    private static double ToDouble(object? value) =>
        value is IConvertible convertible && value is not string && value is not bool
            ? convertible.ToDouble(CultureInfo.InvariantCulture)
            : double.TryParse(value as string, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
}
