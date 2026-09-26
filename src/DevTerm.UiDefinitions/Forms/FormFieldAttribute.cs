namespace DevTerm.UiDefinitions.Forms;

/// <summary>Which <see cref="UiControl"/> a generated form field becomes — the same vocabulary a device panel uses.</summary>
public enum FormFieldKind
{
    /// <summary>Inferred from the property: bool → toggle, enum or <see cref="FormFieldAttribute.OptionsFrom"/> → choice, read-only → indicator, <c>ICommand</c> → button, anything else → text field (with an integer/number constraint for a numeric type).</summary>
    Auto,
    TextField,
    Toggle,
    Choice,
    Numeric,
    Slider,
    Indicator,
    Button,
}

/// <summary>
/// Declares a model property as a field of the form <see cref="FormDefinitionGenerator"/> builds
/// from the model. Everything a field needs besides this comes from the standard
/// <c>System.ComponentModel</c> attributes the model may already carry: <c>[Category]</c> is the
/// section, <c>[DisplayName]</c> the label, <c>[Description]</c> the help text, and
/// <c>[Browsable(false)]</c> leaves a property out. Once any property of a type carries this
/// attribute, only the properties that do become fields (opt-in); a type with none (e.g.
/// <c>CliOptions</c>) gets a field for every browsable property of a supported type. See
/// docs/design/ui-definitions.md's "Forms from one definition".
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class FormFieldAttribute : Attribute
{
    public FormFieldKind Kind { get; set; } = FormFieldKind.Auto;

    /// <summary>Position within its section (lower first); ties keep declaration order.</summary>
    public int Order { get; set; }

    /// <summary>
    /// A property (instance or static) on the same model listing the choices — any sequence; each
    /// item's text is an option. Makes the field a <see cref="ChoiceControl"/>.
    /// </summary>
    public string? OptionsFrom { get; set; }

    public ChoiceStyle ChoiceStyle { get; set; } = ChoiceStyle.Dropdown;

    /// <summary>A property (or control id) whose value decides whether this field is shown — see <see cref="UiCondition"/>.</summary>
    public string? VisibleWhen { get; set; }

    /// <summary>The values of <see cref="VisibleWhen"/> that show the field; none means "while it's true".</summary>
    public string[]? VisibleWhenValues { get; set; }

    /// <summary>The value's data type for a text field; <see cref="UiDefinitions.ValueKind.Text"/> (the default) infers it from the property type.</summary>
    public ValueKind ValueKind { get; set; } = ValueKind.Text;

    /// <summary>Inclusive lower bound (unset: <see cref="double.NaN"/>).</summary>
    public double Minimum { get; set; } = double.NaN;

    /// <summary>Inclusive upper bound (unset: <see cref="double.NaN"/>).</summary>
    public double Maximum { get; set; } = double.NaN;

    public double Step { get; set; } = 1;

    public string? Unit { get; set; }

    /// <summary>A text field's maximum length (0: none).</summary>
    public int MaxLength { get; set; }

    /// <summary>For an indicator: shown as a warning (see <see cref="IndicatorStyle.Warning"/>).</summary>
    public bool Warning { get; set; }
}

/// <summary>
/// Declares how one <c>[Category]</c> of a model's fields renders as a form section: its order, a
/// label different from the category name (an empty label draws no header), and a visibility
/// condition for the whole section (e.g. the Serial fields only while the transport is serial).
/// A category with no attribute gets a section labeled with the category name, in order of first
/// appearance, always shown.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class FormSectionAttribute : Attribute
{
    public FormSectionAttribute(string category)
    {
        Category = category;
    }

    public string Category { get; }

    /// <summary>The header text; the category name when null, no header when empty.</summary>
    public string? Label { get; set; }

    public int Order { get; set; }

    public string? VisibleWhen { get; set; }

    public string[]? VisibleWhenValues { get; set; }
}
