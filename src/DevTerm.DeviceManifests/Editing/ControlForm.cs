using System.ComponentModel;
using System.Globalization;
using DevTerm.UiDefinitions;
using DevTerm.UiDefinitions.Forms;

namespace DevTerm.DeviceManifests.Editing;

/// <summary>
/// One panel control, of any kind: the common id/label/help, plus each kind's own fields — shown only
/// for the kinds that have them (a visibility condition on <see cref="Kind"/>, the same mechanism
/// that shows the Connection Editor's Serial fields only for the serial transport). Changing the kind
/// replaces the control with a new one of that kind, keeping its id, label and help.
/// </summary>
[FormSection("Control", Order = 0, Label = "")]
[FormSection("Behavior", Order = 1)]
[FormSection("Display", Order = 2)]
public sealed class ControlForm : EditorForm
{
    private const string _buttonKind = "button";
    private const string _toggleKind = "toggle";
    private const string _sliderKind = "slider";
    private const string _numericKind = "numeric";
    private const string _choiceKind = "choice";
    private const string _textFieldKind = "textField";
    private const string _indicatorKind = "indicator";
    private const string _barGraphKind = "barGraph";
    private const string _stripChartKind = "stripChart";
    private const string _vectorKind = "vector";

    private readonly UiSection _section;

    public ControlForm(UiSection section, UiControl control, Action edited)
        : base(edited)
    {
        _section = section;
        Control = control;
    }

    /// <summary>The JSON <c>kind</c> of each control type, in the order the kind picker lists them.</summary>
    public static IReadOnlyList<string> Kinds { get; } = [_buttonKind, _toggleKind, _sliderKind, _numericKind, _choiceKind, _textFieldKind, _indicatorKind, _barGraphKind, _stripChartKind, _vectorKind];

    public static IReadOnlyList<string> ChoiceStyles { get; } = Enum.GetNames<ChoiceStyle>();

    public static IReadOnlyList<string> ValueKinds { get; } = Enum.GetNames<ValueKind>();

    public static IReadOnlyList<string> CoordinateSystems { get; } = Enum.GetNames<CoordinateSystem>();

    public static IReadOnlyList<string> AngleUnits { get; } = Enum.GetNames<AngleUnit>();

    /// <summary>The control being edited — replaced by a new instance when <see cref="Kind"/> changes.</summary>
    public UiControl Control { get; private set; }

    public static string KindOf(UiControl control) => control switch
    {
        ButtonControl => _buttonKind,
        ToggleControl => _toggleKind,
        SliderControl => _sliderKind,
        NumericControl => _numericKind,
        ChoiceControl => _choiceKind,
        TextFieldControl => _textFieldKind,
        IndicatorControl => _indicatorKind,
        BarGraphControl => _barGraphKind,
        StripChartControl => _stripChartKind,
        VectorControl => _vectorKind,
        _ => _buttonKind,
    };

    /// <summary>A new control of <paramref name="kind"/> (a <see cref="Kinds"/> entry).</summary>
    public static UiControl Create(string kind, string id, string label) => kind switch
    {
        _toggleKind => new ToggleControl { Id = id, Label = label },
        _sliderKind => new SliderControl { Id = id, Label = label, Maximum = 100 },
        _numericKind => new NumericControl { Id = id, Label = label, Maximum = 100 },
        _choiceKind => new ChoiceControl { Id = id, Label = label },
        _textFieldKind => new TextFieldControl { Id = id, Label = label },
        _indicatorKind => new IndicatorControl { Id = id, Label = label },
        _barGraphKind => new BarGraphControl { Id = id, Label = label },
        _stripChartKind => new StripChartControl { Id = id, Label = label },
        _vectorKind => new VectorControl { Id = id, Label = label },
        _ => new ButtonControl { Id = id, Label = label },
    };

    [Category("Control")]
    [DisplayName("Kind")]
    [FormField(Order = 0, OptionsFrom = nameof(Kinds))]
    public string Kind
    {
        get => KindOf(Control);
        set
        {
            if (value == Kind || !Kinds.Contains(value))
            {
                return;
            }

            var replacement = Create(value, Control.Id, Control.Label);
            replacement.Description = Control.Description;
            replacement.VisibleWhen = Control.VisibleWhen;
            var index = _section.Controls.IndexOf(Control);
            if (index >= 0)
            {
                _section.Controls[index] = replacement;
            }

            Control = replacement;
            Changed(null);
        }
    }

    [Category("Control")]
    [DisplayName("Id")]
    [Description("The command it invokes (a manifest command's id), or the value id it shows.")]
    [FormField(Order = 1)]
    public string Id
    {
        get => Control.Id;
        set
        {
            Control.Id = value;
            Changed();
        }
    }

    [Category("Control")]
    [DisplayName("Label")]
    [FormField(Order = 2)]
    public string Label
    {
        get => Control.Label;
        set
        {
            Control.Label = value;
            Changed();
        }
    }

    [Category("Control")]
    [DisplayName("Help")]
    [Description("Tooltip text (WPF).")]
    [FormField(Order = 3)]
    public string? Help
    {
        get => Control.Description;
        set
        {
            Control.Description = NullIfBlank(value);
            Changed();
        }
    }

    [Category("Behavior")]
    [DisplayName("Command id")]
    [Description("The command to invoke when it isn't the button's own id.")]
    [FormField(Order = 0, VisibleWhen = nameof(Kind), VisibleWhenValues = [_buttonKind])]
    public string? CommandId
    {
        get => (Control as ButtonControl)?.CommandId;
        set => Set<ButtonControl>(c => c.CommandId = NullIfBlank(value));
    }

    [Category("Behavior")]
    [DisplayName("Parameter fields")]
    [Description("Comma-separated ids of the fields whose values it sends, comma-joined.")]
    [FormField(Order = 1, VisibleWhen = nameof(Kind), VisibleWhenValues = [_buttonKind])]
    public string ParameterFields
    {
        get => string.Join(", ", (Control as ButtonControl)?.ParameterFieldIds ?? []);
        set => Set<ButtonControl>(c => c.ParameterFieldIds = FormBinding.SplitList(value) is { Count: > 0 } ids ? [.. ids] : null);
    }

    [Category("Behavior")]
    [DisplayName("Color picker target")]
    [Description("Opens a color picker and sends r,g,b to this command instead.")]
    [FormField(Order = 2, VisibleWhen = nameof(Kind), VisibleWhenValues = [_buttonKind])]
    public string? ColorPickerTarget
    {
        get => (Control as ButtonControl)?.ColorPickerTargetCommandId;
        set => Set<ButtonControl>(c => c.ColorPickerTargetCommandId = NullIfBlank(value));
    }

    [Category("Behavior")]
    [DisplayName("Starts on")]
    [FormField(Order = 3, VisibleWhen = nameof(Kind), VisibleWhenValues = [_toggleKind])]
    public bool DefaultOn
    {
        get => (Control as ToggleControl)?.DefaultValue ?? false;
        set => Set<ToggleControl>(c => c.DefaultValue = value);
    }

    [Category("Behavior")]
    [DisplayName("Default value")]
    [FormField(Order = 4, VisibleWhen = nameof(Kind), VisibleWhenValues = [_sliderKind, _numericKind, _choiceKind, _textFieldKind, _indicatorKind])]
    public string DefaultValue
    {
        get => Control switch
        {
            SliderControl s => Format(s.DefaultValue),
            NumericControl n => Format(n.DefaultValue),
            ChoiceControl c => c.DefaultValue ?? string.Empty,
            TextFieldControl t => t.DefaultValue ?? string.Empty,
            IndicatorControl i => i.DefaultValue ?? string.Empty,
            _ => string.Empty,
        };
        set
        {
            switch (Control)
            {
                case SliderControl s:
                    s.DefaultValue = Parse(value) ?? 0;
                    break;
                case NumericControl n:
                    n.DefaultValue = Parse(value) ?? 0;
                    break;
                case ChoiceControl c:
                    c.DefaultValue = NullIfBlank(value);
                    break;
                case TextFieldControl t:
                    t.DefaultValue = NullIfBlank(value);
                    break;
                case IndicatorControl i:
                    i.DefaultValue = NullIfBlank(value);
                    break;
                default:
                    return;
            }

            Changed();
        }
    }

    [Category("Behavior")]
    [DisplayName("Options")]
    [Description("Comma-separated choices.")]
    [FormField(Order = 5, VisibleWhen = nameof(Kind), VisibleWhenValues = [_choiceKind])]
    public string Options
    {
        get => string.Join(", ", (Control as ChoiceControl)?.Options ?? []);
        set => Set<ChoiceControl>(c => c.Options = [.. FormBinding.SplitList(value)]);
    }

    [Category("Behavior")]
    [DisplayName("Style")]
    [FormField(Order = 6, OptionsFrom = nameof(ChoiceStyles), VisibleWhen = nameof(Kind), VisibleWhenValues = [_choiceKind])]
    public string Style
    {
        get => ((Control as ChoiceControl)?.Style ?? ChoiceStyle.Dropdown).ToString();
        set => Set<ChoiceControl>(c => c.Style = Enum.TryParse<ChoiceStyle>(value, true, out var style) ? style : c.Style);
    }

    [Category("Behavior")]
    [DisplayName("Value type")]
    [Description("Text accepts anything; Integer/Number are checked against the minimum/maximum before sending.")]
    [FormField(Order = 7, OptionsFrom = nameof(ValueKinds), VisibleWhen = nameof(Kind), VisibleWhenValues = [_textFieldKind])]
    public string ValueType
    {
        get => ((Control as TextFieldControl)?.Constraint?.Kind ?? ValueKind.Text).ToString();
        set => Set<TextFieldControl>(
            c =>
            {
                // Text is "no constraint" (free text, no range); a number kind keeps any range already set.
                var kind = Enum.TryParse<ValueKind>(value, true, out var parsed) ? parsed : ValueKind.Text;
                if (kind == ValueKind.Text)
                {
                    c.Constraint = null;
                }
                else
                {
                    (c.Constraint ??= new ValueConstraint()).Kind = kind;
                }
            },
            all: true);
    }

    [Category("Behavior")]
    [DisplayName("Max length")]
    [FormField(Order = 8, VisibleWhen = nameof(Kind), VisibleWhenValues = [_textFieldKind])]
    public int? MaxLength
    {
        get => (Control as TextFieldControl)?.MaxLength;
        set => Set<TextFieldControl>(c => c.MaxLength = value is > 0 ? value : null);
    }

    [Category("Behavior")]
    [DisplayName("Minimum")]
    [FormField(Order = 9, VisibleWhen = nameof(HasRange))]
    public double? Minimum
    {
        get => Control switch
        {
            SliderControl s => s.Minimum,
            NumericControl n => n.Minimum,
            BarGraphControl b => b.Minimum,
            StripChartControl c => c.Minimum,
            TextFieldControl t => t.Constraint?.Minimum,
            _ => null,
        };
        set => SetRange(value, isMinimum: true);
    }

    [Category("Behavior")]
    [DisplayName("Maximum")]
    [FormField(Order = 10, VisibleWhen = nameof(HasRange))]
    public double? Maximum
    {
        get => Control switch
        {
            SliderControl s => s.Maximum,
            NumericControl n => n.Maximum,
            BarGraphControl b => b.Maximum,
            StripChartControl c => c.Maximum,
            TextFieldControl t => t.Constraint?.Maximum,
            _ => null,
        };
        set => SetRange(value, isMinimum: false);
    }

    [Category("Behavior")]
    [DisplayName("Step")]
    [FormField(Order = 11, VisibleWhen = nameof(Kind), VisibleWhenValues = [_sliderKind])]
    public double Step
    {
        get => (Control as SliderControl)?.Step ?? 1;
        set => Set<SliderControl>(c => c.Step = value);
    }

    [Category("Behavior")]
    [DisplayName("Unit")]
    [FormField(Order = 12, VisibleWhen = nameof(Kind), VisibleWhenValues = [_sliderKind, _numericKind, _barGraphKind, _stripChartKind])]
    public string? Unit
    {
        get => Control switch
        {
            SliderControl s => s.Unit,
            NumericControl n => n.Unit,
            BarGraphControl b => b.Unit,
            StripChartControl c => c.Unit,
            _ => null,
        };
        set
        {
            var unit = NullIfBlank(value);
            switch (Control)
            {
                case SliderControl s:
                    s.Unit = unit;
                    break;
                case NumericControl n:
                    n.Unit = unit;
                    break;
                case BarGraphControl b:
                    b.Unit = unit;
                    break;
                case StripChartControl c:
                    c.Unit = unit;
                    break;
                default:
                    return;
            }

            Changed();
        }
    }

    [Category("Display")]
    [DisplayName("Channels")]
    [Description("Comma-separated value ids, each optionally id:label or id:label:#RRGGBB.")]
    [FormField(Order = 0, VisibleWhen = nameof(Kind), VisibleWhenValues = [_barGraphKind, _stripChartKind])]
    public string Channels
    {
        get => string.Join(", ", ChannelsOf(Control).Select(c => c.Id + (c.Label is null && c.Color is null ? string.Empty : ":" + c.Label) + (c.Color is null ? string.Empty : ":" + c.Color)));
        set
        {
            var channels = FormBinding.SplitList(value).Select(item =>
            {
                var bits = item.Split(':');
                return new ChartChannel
                {
                    Id = bits[0].Trim(),
                    Label = bits.Length > 1 ? NullIfBlank(bits[1].Trim()) : null,
                    Color = bits.Length > 2 ? NullIfBlank(bits[2].Trim()) : null,
                };
            }).ToList();
            switch (Control)
            {
                case BarGraphControl b:
                    b.Channels = channels;
                    break;
                case StripChartControl s:
                    s.Channels = channels;
                    break;
                default:
                    return;
            }

            Changed();
        }
    }

    [Category("Display")]
    [DisplayName("History length")]
    [FormField(Order = 1, VisibleWhen = nameof(Kind), VisibleWhenValues = [_stripChartKind], Minimum = 1)]
    public int HistoryLength
    {
        get => (Control as StripChartControl)?.HistoryLength ?? 60;
        set => Set<StripChartControl>(c => c.HistoryLength = value);
    }

    [Category("Display")]
    [DisplayName("Coordinates")]
    [FormField(Order = 2, OptionsFrom = nameof(CoordinateSystems), VisibleWhen = nameof(Kind), VisibleWhenValues = [_vectorKind])]
    public string Coordinates
    {
        get => ((Control as VectorControl)?.Coordinates ?? CoordinateSystem.XY).ToString();
        set => Set<VectorControl>(c => c.Coordinates = Enum.TryParse<CoordinateSystem>(value, true, out var system) ? system : c.Coordinates, all: true);
    }

    public bool IsCartesianVector => Control is VectorControl { Coordinates: not CoordinateSystem.Polar };

    public bool IsXyzVector => Control is VectorControl { Coordinates: CoordinateSystem.XYZ };

    public bool IsPolarVector => Control is VectorControl { Coordinates: CoordinateSystem.Polar };

    public bool HasRange => Control is SliderControl or NumericControl or BarGraphControl or StripChartControl
        || Control is TextFieldControl { Constraint.Kind: not ValueKind.Text };

    [Category("Display")]
    [DisplayName("X value id")]
    [FormField(Order = 3, VisibleWhen = nameof(IsCartesianVector))]
    public string? XId
    {
        get => (Control as VectorControl)?.XId;
        set => Set<VectorControl>(c => c.XId = NullIfBlank(value));
    }

    [Category("Display")]
    [DisplayName("Y value id")]
    [FormField(Order = 4, VisibleWhen = nameof(IsCartesianVector))]
    public string? YId
    {
        get => (Control as VectorControl)?.YId;
        set => Set<VectorControl>(c => c.YId = NullIfBlank(value));
    }

    [Category("Display")]
    [DisplayName("Z value id")]
    [FormField(Order = 5, VisibleWhen = nameof(IsXyzVector))]
    public string? ZId
    {
        get => (Control as VectorControl)?.ZId;
        set => Set<VectorControl>(c => c.ZId = NullIfBlank(value));
    }

    [Category("Display")]
    [DisplayName("Radius value id")]
    [FormField(Order = 6, VisibleWhen = nameof(IsPolarVector))]
    public string? RadiusId
    {
        get => (Control as VectorControl)?.RadiusId;
        set => Set<VectorControl>(c => c.RadiusId = NullIfBlank(value));
    }

    [Category("Display")]
    [DisplayName("Angle value id")]
    [FormField(Order = 7, VisibleWhen = nameof(IsPolarVector))]
    public string? AngleId
    {
        get => (Control as VectorControl)?.AngleId;
        set => Set<VectorControl>(c => c.AngleId = NullIfBlank(value));
    }

    [Category("Display")]
    [DisplayName("Angle unit")]
    [FormField(Order = 8, OptionsFrom = nameof(AngleUnits), VisibleWhen = nameof(IsPolarVector))]
    public string AngleUnitName
    {
        get => ((Control as VectorControl)?.AngleUnit ?? AngleUnit.Degrees).ToString();
        set => Set<VectorControl>(c => c.AngleUnit = Enum.TryParse<AngleUnit>(value, true, out var unit) ? unit : c.AngleUnit);
    }

    [Category("Display")]
    [DisplayName("Range")]
    [Description("Every axis spans ±range (a polar plot's outer ring).")]
    [FormField(Order = 9, VisibleWhen = nameof(Kind), VisibleWhenValues = [_vectorKind])]
    public double Range
    {
        get => (Control as VectorControl)?.Range ?? 1;
        set => Set<VectorControl>(c => c.Range = value);
    }

    [Category("Display")]
    [DisplayName("Trail length")]
    [FormField(Order = 10, VisibleWhen = nameof(Kind), VisibleWhenValues = [_vectorKind], Minimum = 0)]
    public int TrailLength
    {
        get => (Control as VectorControl)?.TrailLength ?? 20;
        set => Set<VectorControl>(c => c.TrailLength = value);
    }

    [Category("Display")]
    [DisplayName("Hue value id")]
    [Description("Optional live color: hue in degrees (saturation/brightness optional, 0-1 or 0-100).")]
    [FormField(Order = 11, VisibleWhen = nameof(Kind), VisibleWhenValues = [_vectorKind])]
    public string? HueId
    {
        get => (Control as VectorControl)?.HueId;
        set => Set<VectorControl>(c => c.HueId = NullIfBlank(value));
    }

    [Category("Display")]
    [DisplayName("Saturation value id")]
    [FormField(Order = 12, VisibleWhen = nameof(Kind), VisibleWhenValues = [_vectorKind])]
    public string? SaturationId
    {
        get => (Control as VectorControl)?.SaturationId;
        set => Set<VectorControl>(c => c.SaturationId = NullIfBlank(value));
    }

    [Category("Display")]
    [DisplayName("Brightness value id")]
    [FormField(Order = 13, VisibleWhen = nameof(Kind), VisibleWhenValues = [_vectorKind])]
    public string? BrightnessId
    {
        get => (Control as VectorControl)?.BrightnessId;
        set => Set<VectorControl>(c => c.BrightnessId = NullIfBlank(value));
    }

    private static IEnumerable<ChartChannel> ChannelsOf(UiControl control) => control switch
    {
        BarGraphControl b => b.Channels,
        StripChartControl s => s.Channels,
        _ => [],
    };

    private static string Format(double value) => value.ToString(CultureInfo.InvariantCulture);

    private static double? Parse(string? text) =>
        double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    private void Set<T>(Action<T> write, bool all = false, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        where T : UiControl
    {
        if (Control is T control)
        {
            write(control);
            Changed(all ? null : name);
        }
    }

    private void SetRange(double? value, bool isMinimum)
    {
        switch (Control)
        {
            case SliderControl s when value is { } v:
                if (isMinimum)
                {
                    s.Minimum = v;
                }
                else
                {
                    s.Maximum = v;
                }

                break;
            case NumericControl n when value is { } v:
                if (isMinimum)
                {
                    n.Minimum = v;
                }
                else
                {
                    n.Maximum = v;
                }

                break;
            case BarGraphControl b when value is { } v:
                if (isMinimum)
                {
                    b.Minimum = v;
                }
                else
                {
                    b.Maximum = v;
                }

                break;
            case StripChartControl c:
                if (isMinimum)
                {
                    c.Minimum = value;
                }
                else
                {
                    c.Maximum = value;
                }

                break;
            case TextFieldControl { Constraint: { } constraint }:
                if (isMinimum)
                {
                    constraint.Minimum = value;
                }
                else
                {
                    constraint.Maximum = value;
                }

                break;
            default:
                return;
        }

        Changed(isMinimum ? nameof(Minimum) : nameof(Maximum));
    }
}
