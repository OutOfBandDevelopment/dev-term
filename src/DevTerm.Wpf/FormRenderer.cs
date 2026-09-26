using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DevTerm.UiDefinitions;
using DevTerm.UiDefinitions.Forms;

namespace DevTerm.Wpf;

/// <summary>
/// The WPF half of the generic <em>form</em> renderer (see <c>DevTerm.Console.FormRenderer</c> for
/// the TUI's): walks a <see cref="UiDefinition"/> — typically one <see cref="FormDefinitionGenerator"/>
/// made from an annotated model — and builds real WPF controls bound two-way to that model through a
/// <see cref="FormBinding"/>. The settings-form sibling of <see cref="ControlPanelWindow"/>, which
/// renders the same control vocabulary as a command panel. Returns an embeddable element rather than
/// a window, so a host (the Connection Editor, the manifest editor) places it among its own parts.
/// See docs/design/ui-definitions.md's "Forms from one definition".
/// </summary>
/// <remarks>
/// <para>
/// Binding goes through <see cref="FormBinding"/> rather than WPF <c>{Binding}</c>s so both front
/// ends share one set of conversion/visibility/validation rules and a plain (non-notifying) model —
/// the manifest editor's — still updates every dependent row after an edit.
/// </para>
/// <para>
/// Layout matches the hand-built editor it replaced: each labeled section is a bold header over rows
/// of a fixed-width label column (<see cref="WpfFormOptions.LabelColumnWidth"/>) and a stretching
/// widget column; a toggle's check box and an unlabeled row sit in the widget column. A hidden
/// section or row is collapsed, so nothing leaves a gap.
/// </para>
/// </remarks>
internal static class FormRenderer
{
    /// <summary>The warning text color (an <see cref="IndicatorStyle.Warning"/> indicator, a field's validation message) — the dark red the editor always used; one place to re-point it.</summary>
    internal static Brush WarningBrush { get; set; } = Brushes.DarkRed;

    public static WpfFormParts Build(UiDefinition definition, FormBinding binding, WpfFormOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(binding);
        options ??= new WpfFormOptions();

        var parts = new WpfFormParts(binding) { Root = new StackPanel() };
        var first = true;
        foreach (var section in definition.Sections)
        {
            var panel = new StackPanel();
            if (!string.IsNullOrWhiteSpace(section.Label))
            {
                panel.Children.Add(new TextBlock { Text = section.Label, FontWeight = FontWeights.Bold, Margin = new Thickness(0, first ? 0 : 8, 0, 2) });
                parts.SectionPanels[section.Label] = panel;
            }

            first = false;
            foreach (var control in section.Controls)
            {
                var row = BuildRow(parts, control, options);
                panel.Children.Add(row.Element);
                parts.Rows[control.Id] = row.Element;
                parts.RowList.Add(row);
            }

            parts.Root.Children.Add(panel);
            parts.SectionList.Add((section, panel));
        }

        parts.RefreshAll();
        binding.Changed += (_, name) => parts.OnModelChanged(name);
        return parts;
    }

    private static WpfFormRow BuildRow(WpfFormParts parts, UiControl control, WpfFormOptions options)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = options.LabelColumnWidth is { } width ? new GridLength(width) : GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var row = new WpfFormRow(control, grid);

        if (control is not ToggleControl and not ButtonControl && !string.IsNullOrEmpty(control.Label))
        {
            var label = new TextBlock
            {
                Text = control.Label + ":",
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.NoWrap,
                Margin = new Thickness(0, 0, 8, 0),
            };
            grid.Children.Add(label);
            parts.RowLabels[control.Id] = label;
        }

        var widget = options.CustomWidgets.TryGetValue(control.Id, out var custom)
            ? custom(control)
            : BuildWidget(parts, row, control, options);
        if (control.Description is { Length: > 0 } help)
        {
            widget.ToolTip = help;
        }

        Grid.SetColumn(widget, 1);
        grid.Children.Add(widget);
        parts.ControlViews[control.Id] = widget;
        return row;
    }

    private static FrameworkElement BuildWidget(WpfFormParts parts, WpfFormRow row, UiControl control, WpfFormOptions options)
    {
        var binding = parts.Binding;
        switch (control)
        {
            case ToggleControl:
                {
                    var checkBox = new CheckBox { Content = control.Label, VerticalAlignment = VerticalAlignment.Center };
                    checkBox.Checked += (_, _) => parts.Push(() => binding.SetBool(control.Id, true));
                    checkBox.Unchecked += (_, _) => parts.Push(() => binding.SetBool(control.Id, false));
                    row.Refresh = () => checkBox.IsChecked = binding.GetBool(control.Id);
                    return checkBox;
                }

            case ChoiceControl { Style: ChoiceStyle.CheckList } choice:
                {
                    var panel = new WrapPanel();
                    var boxes = choice.Options.Select(option => new CheckBox { Content = option, Margin = new Thickness(0, 2, 12, 2) }).ToList();
                    void Push() => parts.Push(() => binding.SetSelection(control.Id, choice.Options.Where((_, i) => boxes[i].IsChecked == true)));
                    foreach (var box in boxes)
                    {
                        box.Checked += (_, _) => Push();
                        box.Unchecked += (_, _) => Push();
                        panel.Children.Add(box);
                    }

                    row.Refresh = () =>
                    {
                        var selected = binding.GetSelection(control.Id);
                        for (var i = 0; i < boxes.Count; i++)
                        {
                            boxes[i].IsChecked = selected.Contains(choice.Options[i], StringComparer.OrdinalIgnoreCase);
                        }
                    };
                    parts.CheckLists[control.Id] = boxes;
                    return panel;
                }

            case ChoiceControl { Style: ChoiceStyle.RadioGroup } choice:
                {
                    var panel = new WrapPanel();
                    var radios = choice.Options.Select(option => new RadioButton { Content = option, GroupName = $"form_{control.Id}_{Guid.NewGuid():N}", Margin = new Thickness(0, 2, 12, 2) }).ToList();
                    for (var i = 0; i < radios.Count; i++)
                    {
                        var option = choice.Options[i];
                        radios[i].Checked += (_, _) => parts.Push(() => binding.SetText(choice, option));
                        panel.Children.Add(radios[i]);
                    }

                    row.Refresh = () =>
                    {
                        var current = binding.GetText(control.Id);
                        foreach (var radio in radios)
                        {
                            radio.IsChecked = string.Equals((string)radio.Content, current, StringComparison.OrdinalIgnoreCase);
                        }
                    };
                    parts.RadioGroups[control.Id] = radios;
                    return panel;
                }

            case ChoiceControl choice:
                {
                    var box = new ComboBox { ItemsSource = choice.Options, VerticalAlignment = VerticalAlignment.Center };
                    box.SelectionChanged += (_, _) =>
                    {
                        if (box.SelectedItem is string selected)
                        {
                            parts.Push(() => binding.SetText(choice, selected));
                        }
                    };
                    row.Refresh = () =>
                    {
                        var current = binding.GetText(control.Id);
                        var match = choice.Options.FirstOrDefault(o => string.Equals(o, current, StringComparison.OrdinalIgnoreCase));
                        if (!Equals(box.SelectedItem, match))
                        {
                            box.SelectedItem = match;
                        }
                    };
                    return box;
                }

            case IndicatorControl indicator:
                {
                    var text = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
                    if (indicator.Style == IndicatorStyle.Warning)
                    {
                        text.Foreground = WarningBrush;
                    }

                    row.Refresh = () => text.Text = binding.Has(control.Id) ? binding.GetText(control.Id) : indicator.DefaultValue ?? string.Empty;
                    return text;
                }

            case ButtonControl button:
                {
                    var view = new Button { Content = control.Label, Padding = new Thickness(8, 2, 8, 2), HorizontalAlignment = HorizontalAlignment.Left };
                    view.Click += (_, _) =>
                    {
                        if (options.Actions.TryGetValue(control.Id, out var action))
                        {
                            action();
                        }
                        else
                        {
                            binding.Invoke(button.CommandId ?? button.Id);
                        }
                    };
                    return view;
                }

            case SliderControl slider:
                {
                    var view = new Slider { Minimum = slider.Minimum, Maximum = slider.Maximum, TickFrequency = slider.Step, IsSnapToTickEnabled = slider.Step > 0, Width = 160, VerticalAlignment = VerticalAlignment.Center };
                    var value = new TextBlock { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                    view.ValueChanged += (_, e) =>
                    {
                        value.Text = string.Create(CultureInfo.InvariantCulture, $"{e.NewValue:0.#}{slider.Unit}");
                        parts.Push(() => binding.SetText(slider, e.NewValue.ToString(CultureInfo.InvariantCulture)));
                    };
                    row.Refresh = () => view.Value = double.TryParse(binding.GetText(control.Id), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : slider.DefaultValue;
                    var panel = new StackPanel { Orientation = Orientation.Horizontal };
                    panel.Children.Add(view);
                    panel.Children.Add(value);
                    return panel;
                }

            default:
                return BuildTextBox(parts, row, control);
        }
    }

    private static FrameworkElement BuildTextBox(WpfFormParts parts, WpfFormRow row, UiControl control)
    {
        var binding = parts.Binding;
        var constraint = ValueValidator.ConstraintFor(control);
        var box = new TextBox { VerticalContentAlignment = VerticalAlignment.Center, IsReadOnly = binding.IsReadOnly(control.Id) };
        if (control is TextFieldControl { MaxLength: { } max })
        {
            box.MaxLength = max;
        }

        var error = new TextBlock { Foreground = WarningBrush, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 2, 0, 0) };
        parts.ErrorTexts[control.Id] = error;

        void Show(ValueValidationResult result)
        {
            error.Text = result.Error ?? string.Empty;
            error.Visibility = result.IsValid ? Visibility.Collapsed : Visibility.Visible;
        }

        box.TextChanged += (_, _) => parts.Push(() => Show(binding.SetText(control, box.Text)));
        row.Refresh = () =>
        {
            var text = binding.GetText(control.Id);
            if (box.Text != text)
            {
                box.Text = text;
            }

            Show(ValueValidator.Validate(constraint, text));
        };
        parts.TextBoxes[control.Id] = box;

        var stack = new StackPanel();
        if (control is NumericControl numeric && numeric.Minimum > double.MinValue / 2 && numeric.Maximum < double.MaxValue / 2)
        {
            var line = new DockPanel();
            var hint = new TextBlock { Text = string.Create(CultureInfo.InvariantCulture, $"[{numeric.Minimum:0.#}-{numeric.Maximum:0.#}]{numeric.Unit}"), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(hint, Dock.Right);
            line.Children.Add(hint);
            line.Children.Add(box);
            stack.Children.Add(line);
        }
        else
        {
            stack.Children.Add(box);
        }

        stack.Children.Add(error);
        return stack;
    }
}

/// <summary>What a host adds to a <see cref="FormRenderer"/> form: its own widget for a control, or a button's action.</summary>
internal sealed class WpfFormOptions
{
    /// <summary>Replaces a control's widget (the form keeps its label, column and visibility); keyed by <see cref="UiControl.Id"/>.</summary>
    public Dictionary<string, Func<UiControl, FrameworkElement>> CustomWidgets { get; } = new(StringComparer.Ordinal);

    /// <summary>What a <see cref="ButtonControl"/> does, keyed by its id; without one, the bound model's command property of that name runs.</summary>
    public Dictionary<string, Action> Actions { get; } = new(StringComparer.Ordinal);

    /// <summary>The label column's width; null sizes it to the longest label.</summary>
    public double? LabelColumnWidth { get; set; } = 140;
}

/// <summary>One rendered WPF form: its root element plus the lookups a host or test needs, keyed by <see cref="UiControl.Id"/>.</summary>
internal sealed class WpfFormParts
{
    private int _refreshing;

    internal WpfFormParts(FormBinding binding)
    {
        Binding = binding;
    }

    public required StackPanel Root { get; init; }

    public FormBinding Binding { get; }

    /// <summary>Each control's widget element (a text field's is its container; see <see cref="TextBoxes"/>).</summary>
    public Dictionary<string, FrameworkElement> ControlViews { get; } = new(StringComparer.Ordinal);

    /// <summary>Each control's whole row (label plus widget) — what's collapsed when its condition fails.</summary>
    public Dictionary<string, FrameworkElement> Rows { get; } = new(StringComparer.Ordinal);

    /// <summary>Each labeled section's panel (header plus rows) — what's collapsed when the section's condition fails.</summary>
    public Dictionary<string, StackPanel> SectionPanels { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, TextBox> TextBoxes { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, TextBlock> ErrorTexts { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, TextBlock> RowLabels { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, IReadOnlyList<CheckBox>> CheckLists { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, IReadOnlyList<RadioButton>> RadioGroups { get; } = new(StringComparer.Ordinal);

    internal List<WpfFormRow> RowList { get; } = [];

    internal List<(UiSection Section, StackPanel Panel)> SectionList { get; } = [];

    /// <summary>Re-reads every widget from the model and re-applies every visibility condition.</summary>
    public void RefreshAll()
    {
        foreach (var row in RowList)
        {
            RefreshRow(row);
        }

        ApplyVisibility();
    }

    internal void OnModelChanged(string? name)
    {
        foreach (var row in RowList)
        {
            if (name is null || row.Control.Id == name || row.Control is IndicatorControl)
            {
                RefreshRow(row);
            }
        }

        ApplyVisibility();
    }

    /// <summary>Runs a widget → model write, unless the widget is only changing because the model is being read into it.</summary>
    internal void Push(Action write)
    {
        if (_refreshing == 0)
        {
            write();
        }
    }

    private void RefreshRow(WpfFormRow row)
    {
        if (row.Refresh is null)
        {
            return;
        }

        _refreshing++;
        try
        {
            row.Refresh();
        }
        finally
        {
            _refreshing--;
        }
    }

    private void ApplyVisibility()
    {
        foreach (var (section, panel) in SectionList)
        {
            panel.Visibility = Binding.IsVisible(section.VisibleWhen) ? Visibility.Visible : Visibility.Collapsed;
        }

        foreach (var row in RowList)
        {
            row.Element.Visibility = Binding.IsVisible(row.Control.VisibleWhen) ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}

/// <summary>One control's row element and how to re-read it from the model.</summary>
internal sealed class WpfFormRow(UiControl control, FrameworkElement element)
{
    public UiControl Control { get; } = control;

    public FrameworkElement Element { get; } = element;

    public Action? Refresh { get; set; }
}
