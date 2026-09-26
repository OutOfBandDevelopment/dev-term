using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DevTerm.Configuration;
using DevTerm.Core.Control;
using DevTerm.Core.Presenters;
using DevTerm.UiDefinitions;

namespace DevTerm.Wpf;

/// <summary>
/// Walks any <see cref="UiDefinition"/> and renders it as real, wired WPF controls against any
/// <see cref="IControlSurface"/> — the WPF half of the generic renderer described in
/// docs/design/ui-definitions.md (see <c>DevTerm.Console.ControlPanelMode</c> for the TUI half),
/// proven first against the K8055's own definition but not specific to it. Unlike the TUI renderer,
/// WPF has native <see cref="Slider"/>/<see cref="RadioButton"/>/<see cref="ComboBox"/> widgets, so
/// this renderer doesn't need the TUI renderer's "one widget covers two kinds" deviations — Slider
/// gets a real <see cref="Slider"/>, and <see cref="ChoiceControl"/> picks <see cref="RadioButton"/>
/// or <see cref="ComboBox"/> per its own <see cref="ChoiceStyle"/>.
/// </summary>
/// <remarks>
/// <para>
/// Builds controls directly in code-behind rather than via XAML data binding (unlike
/// <see cref="DeviceProfilesWindow"/>'s view-model-bound form) because the control set is generated
/// from a runtime <see cref="UiDefinition"/>, not a fixed compile-time set of named fields.
/// </para>
/// <para>
/// Each labeled section is an <see cref="Expander"/> (expanded by default) holding a two-column
/// <see cref="Grid"/>: an auto-sized, never-wrapping label column, so every control in a section
/// starts at the same x, and the control column. The definition's <see cref="UiDefinition.Description"/>
/// renders as a final collapsible "Notes" section. A control that sends a command the surface can
/// preview (<see cref="ICommandPreview"/>) gets an "ⓘ" icon whose tooltip is recomputed from the
/// control's current value every time it opens.
/// </para>
/// </remarks>
public partial class ControlPanelWindow : Window
{
    internal const string NotesSectionLabel = "Notes";
    internal const string InfoGlyph = "ⓘ";

    // How far the notes text sits in from the sections' left edge: the expander content border's
    // margin, line and padding, the text's own margins, and a little slack.
    private const double _notesIndent = 36;

    private readonly IControlSurface _surface;
    private readonly ICommandPreview? _preview;
    private readonly string _baseStatus;
    private readonly Dictionary<string, UiControl> _controlsById = [];
    private readonly Dictionary<string, FrameworkElement> _controlViews = [];
    private readonly Dictionary<string, TextBlock> _indicatorLabels = [];
    private readonly Dictionary<string, Border> _colorSwatches = [];

    // "Custom" choice options backed by a color button (CustomColorChoices), each radio group's
    // buttons by choice id, and a flag so checking the Custom radio after a pick doesn't resend.
    private readonly IReadOnlyDictionary<string, (ButtonControl Button, string Option)> _customColorLinks;
    private readonly Dictionary<string, List<RadioButton>> _choiceRadios = [];
    private bool _suppressChoiceSend;
    private readonly Dictionary<string, TextBlock> _controlLabels = [];
    private readonly Dictionary<string, TextBlock> _infoIcons = [];
    private readonly Dictionary<string, Func<string?>> _previewSources = [];
    private readonly Dictionary<string, Expander> _sectionExpanders = [];
    private readonly Dictionary<string, LiveDisplayElement> _displays = [];
    private readonly string _definitionName;
    private bool _showingValidationError;

    /// <summary>Every interactive/display view, keyed by its <c>UiControl.Id</c> — for tests to drive/assert against, mirroring <c>ControlPanelWindowParts.ControlViews</c> in the TUI renderer.</summary>
    internal IReadOnlyDictionary<string, FrameworkElement> ControlViews => _controlViews;

    /// <summary>Each row's left-hand label <see cref="TextBlock"/> (the <c>control.Label + ":"</c> caption), keyed by <c>UiControl.Id</c> — for tests asserting labels stay on one line in an aligned column (see <see cref="BuildSectionGrid"/>).</summary>
    internal IReadOnlyDictionary<string, TextBlock> ControlLabels => _controlLabels;

    /// <summary>The subset of <see cref="ControlViews"/> that are <see cref="IndicatorControl"/> labels, for tests asserting a live value update.</summary>
    internal IReadOnlyDictionary<string, TextBlock> IndicatorLabels => _indicatorLabels;

    /// <summary>Each color-picker button's swatch (keyed by the button's id) - see <see cref="BuildWidget"/>.</summary>
    internal IReadOnlyDictionary<string, Border> ColorSwatches => _colorSwatches;

    /// <summary>The "ⓘ" icon next to each control that sends a previewable command, keyed by <c>UiControl.Id</c>.</summary>
    internal IReadOnlyDictionary<string, TextBlock> InfoIcons => _infoIcons;

    /// <summary>The bar graph / strip chart / vector displays, keyed by <c>UiControl.Id</c> — each exposing its live state.</summary>
    internal IReadOnlyDictionary<string, LiveDisplayElement> Displays => _displays;

    /// <summary>Each labeled section's <see cref="Expander"/>, keyed by section label (<see cref="NotesSectionLabel"/> for the notes).</summary>
    internal IReadOnlyDictionary<string, Expander> SectionExpanders => _sectionExpanders;

    public ControlPanelWindow(UiDefinition definition, IControlSurface surface, IPresenter? structuredSource)
    {
        InitializeComponent();
        WpfTheme.Attach(this);
        Title = $"dev-term — {definition.Name}";
        _definitionName = definition.Name;
        _customColorLinks = CustomColorChoices.Find(definition);
        _surface = surface;
        _preview = surface as ICommandPreview;

        foreach (var control in definition.Sections.SelectMany(s => s.Controls))
        {
            _controlsById.TryAdd(control.Id, control);
        }

        foreach (var section in definition.Sections)
        {
            var grid = BuildSectionGrid(section);
            if (string.IsNullOrWhiteSpace(section.Label))
            {
                // Nothing to name a header with (e.g. Busylight's lone Apply button) — always shown,
                // indented to line up with the expanded sections' own rows.
                grid.Margin = new Thickness(18, 0, 0, 8);
                SectionsPanel.Children.Add(grid);
            }
            else
            {
                SectionsPanel.Children.Add(BuildExpander(section.Label, grid));
            }
        }

        if (!string.IsNullOrWhiteSpace(definition.Description))
        {
            var notes = new TextBlock { Text = definition.Description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 2, 4, 2) }.Themed(TextBlock.ForegroundProperty, ThemeRole.MutedForeground);
            SectionsPanel.Children.Add(BuildExpander(NotesSectionLabel, notes));

            // The sections scroll sideways when a row is wider than the window (a long label plus
            // a long button used to be cut off at the right edge at the minimum width), so the
            // notes wrap to the visible width instead of the scrolled content's.
            SectionsScroller.ScrollChanged += (_, e) =>
            {
                if (e.ViewportWidthChange != 0 || notes.MaxWidth is double.PositiveInfinity)
                {
                    notes.MaxWidth = Math.Max(120, SectionsScroller.ViewportWidth - _notesIndent);
                }
            };
        }

        _baseStatus = structuredSource is IStructuredPresenter
            ? string.Empty
            : "Not decoding — connect with the matching --presenter to see live values.";
        StatusText.Text = _baseStatus;

        if (structuredSource is IStructuredPresenter structuredPresenter)
        {
            structuredPresenter.ValuesChanged += OnValuesChanged;
            Closed += (_, _) => structuredPresenter.ValuesChanged -= OnValuesChanged;
        }
    }

    /// <summary>
    /// What the control <paramref name="controlId"/> would send right now (e.g. <c>Sends: *IDN?\n</c>),
    /// recomputed from its current value — the text its "ⓘ" tooltip shows each time it opens. Null
    /// when the control sends nothing previewable.
    /// </summary>
    internal string? PreviewFor(string controlId) =>
        _previewSources.TryGetValue(controlId, out var source) ? source() : null;

    private Expander BuildExpander(string label, UIElement content)
    {
        var expander = new Expander
        {
            Header = new TextBlock { Text = label, FontWeight = FontWeights.SemiBold },
            // Opens the way it was last left for this definition (expanded the first time).
            IsExpanded = SectionExpansionState.IsExpanded(_definitionName, label),
            Margin = new Thickness(0, 0, 0, 8),
            Content = new Border
            {
                BorderThickness = new Thickness(1, 0, 0, 0),
                Margin = new Thickness(8, 4, 0, 0),
                Padding = new Thickness(8, 0, 0, 0),
                Child = content,
            }.Themed(Border.BorderBrushProperty, ThemeRole.ControlBorder),
        };
        expander.Expanded += (_, _) => SectionExpansionState.Set(_definitionName, label, expanded: true);
        expander.Collapsed += (_, _) => SectionExpansionState.Set(_definitionName, label, expanded: false);
        _sectionExpanders.TryAdd(label, expander);
        return expander;
    }

    private Grid BuildSectionGrid(UiSection section)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (var row = 0; row < section.Controls.Count; row++)
        {
            var control = section.Controls[row];
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // No wrapping: the Auto column grows to the section's longest label, so every control
            // in the section starts at the same x instead of a long label wrapping onto two lines.
            var label = new TextBlock
            {
                Text = control.Label + ":",
                TextWrapping = TextWrapping.NoWrap,

                // A chart's label sits at its top, not beside its middle.
                VerticalAlignment = control is BarGraphControl or StripChartControl or VectorControl ? VerticalAlignment.Top : VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 4),
            };
            Grid.SetRow(label, row);
            Grid.SetColumn(label, 0);
            grid.Children.Add(label);
            _controlLabels[control.Id] = label;

            var content = BuildRowContent(control);
            content.Margin = new Thickness(0, 0, 0, 4);

            // Left-aligned in the star column, or a fixed-width widget (e.g. a ComboBox) centers.
            content.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetRow(content, row);
            Grid.SetColumn(content, 1);
            grid.Children.Add(content);
        }

        return grid;
    }

    private FrameworkElement BuildRowContent(UiControl control)
    {
        var (rowContent, tracked, preview, probe) = BuildWidget(control);
        _controlViews[control.Id] = tracked;

        if (preview is null || probe is not { } p || _preview?.PreviewCommand(p.CommandId, p.Value) is null || IsValueHolderOnly(control))
        {
            return rowContent;
        }

        _previewSources[control.Id] = preview;
        var icon = new TextBlock
        {
            Text = InfoGlyph,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 15,
            Cursor = Cursors.Help,
            ToolTip = preview() ?? string.Empty,
        }.Themed(TextBlock.ForegroundProperty, ThemeRole.Accent);

        // Recomputed on every open, so the tooltip reflects the slider position/typed text/selection
        // at hover time rather than whatever it was when the panel was built.
        icon.ToolTipOpening += (_, _) => icon.ToolTip = preview() ?? string.Empty;
        ToolTipService.SetShowDuration(icon, 30000);
        _infoIcons[control.Id] = icon;

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(rowContent);
        panel.Children.Add(icon);
        return panel;
    }

    /// <summary>True for a field only ever read by a parameter button — committing it on its own sends nothing worth previewing (the button has the icon).</summary>
    private bool IsValueHolderOnly(UiControl control) =>
        control is not ButtonControl
        && _controlsById.Values.Any(c => c is ButtonControl { ParameterFieldIds: { } ids } && ids.Contains(control.Id));

    private string? SendsText(string commandId, string? value) =>
        _preview?.PreviewCommand(commandId, value) is { } preview ? $"Sends: {preview}" : null;

    private Func<string?> ValuePreview(UiControl control, Func<string> currentText) => () =>
    {
        var result = ValueValidator.Validate(ValueValidator.ConstraintFor(control), currentText());
        return result.IsValid ? SendsText(control.Id, result.Value) : $"Won't send: {result.Error}";
    };

    private void OnValuesChanged(object? sender, IReadOnlyDictionary<string, string> values)
    {
        Dispatcher.Invoke(() =>
        {
            foreach (var (id, value) in values)
            {
                if (_indicatorLabels.TryGetValue(id, out var label))
                {
                    label.Text = value;
                }
            }

            foreach (var display in _displays.Values)
            {
                display.Apply(values);
            }
        });
    }

    private (FrameworkElement RowContent, FrameworkElement Tracked, Func<string?>? Preview, (string CommandId, string? Value)? Probe) BuildWidget(UiControl control)
    {
        switch (control)
        {
            case ButtonControl { ColorPickerTargetCommandId: { } colorTargetId } button:
                {
                    var view = new Button { Content = control.Label, Padding = new Thickness(8, 2, 8, 2), HorizontalAlignment = HorizontalAlignment.Left };

                    // A swatch next to the button showing the current custom color's hex value on a
                    // background of that color - hidden until a custom color has been set (including
                    // one picked in an earlier opening of this panel - see LastPickedColors).
                    var swatch = new Border
                    {
                        Margin = new Thickness(6, 0, 0, 0),
                        Padding = new Thickness(8, 2, 8, 2),
                        MinWidth = 80,
                        BorderThickness = new Thickness(1),
                        VerticalAlignment = VerticalAlignment.Center,
                        Visibility = Visibility.Collapsed,
                        Child = new TextBlock { FontFamily = new System.Windows.Media.FontFamily("Consolas"), HorizontalAlignment = HorizontalAlignment.Center },
                    }.Themed(Border.BorderBrushProperty, ThemeRole.SwatchBorder);
                    _colorSwatches[button.Id] = swatch;
                    if (LastPickedColors.TryGet(button.Id, out var current))
                    {
                        ShowSwatch(swatch, current);
                    }

                    view.Click += (_, _) => OpenColorPicker(button.Id, colorTargetId);
                    var row = new StackPanel { Orientation = Orientation.Horizontal };
                    row.Children.Add(view);
                    row.Children.Add(swatch);

                    string LastColor()
                    {
                        var (r, g, b) = LastPickedColors.Get(button.Id);
                        return string.Create(CultureInfo.InvariantCulture, $"{r},{g},{b}");
                    }

                    return (row, view, () => SendsText(colorTargetId, LastColor()), (colorTargetId, LastColor()));
                }

            case ButtonControl { ParameterFieldIds: { } parameterFieldIds } button:
                {
                    var view = new Button { Content = control.Label, Padding = new Thickness(8, 2, 8, 2), HorizontalAlignment = HorizontalAlignment.Left };
                    var commandId = button.CommandId ?? button.Id;
                    view.Click += (_, _) =>
                    {
                        if (TryReadParameters(parameterFieldIds, out var joined, out var error))
                        {
                            ClearValidationError();
                            Invoke(commandId, joined);
                        }
                        else
                        {
                            ShowValidationError(error!);
                        }
                    };
                    var raw = string.Join(',', parameterFieldIds.Select(id => _controlViews.TryGetValue(id, out var fieldView) ? GetCurrentValue(fieldView) : string.Empty));
                    Func<string?> preview = () => TryReadParameters(parameterFieldIds, out var joined, out var error)
                        ? SendsText(commandId, joined)
                        : $"Won't send: {error}";
                    return (view, view, preview, (commandId, raw));
                }

            case ButtonControl button:
                {
                    var view = new Button { Content = control.Label, Padding = new Thickness(8, 2, 8, 2), HorizontalAlignment = HorizontalAlignment.Left };
                    var commandId = button.CommandId ?? button.Id;
                    view.Click += (_, _) => Invoke(commandId, null);
                    return (view, view, () => SendsText(commandId, null), (commandId, null));
                }

            case ToggleControl toggle:
                {
                    var view = new CheckBox { IsChecked = toggle.DefaultValue, VerticalAlignment = VerticalAlignment.Center };
                    view.Checked += (_, _) => Invoke(toggle.Id, "1");
                    view.Unchecked += (_, _) => Invoke(toggle.Id, "0");

                    // What toggling it would send — the next state, not the current one.
                    return (view, view, () => SendsText(toggle.Id, view.IsChecked == true ? "0" : "1"), (toggle.Id, toggle.DefaultValue ? "0" : "1"));
                }

            case SliderControl slider:
                {
                    var view = new Slider
                    {
                        Minimum = slider.Minimum,
                        Maximum = slider.Maximum,
                        TickFrequency = slider.Step,
                        IsSnapToTickEnabled = slider.Step > 0,
                        Value = slider.DefaultValue,
                        Width = 160,
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    var valueLabel = new TextBlock { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Text = FormatUnit(slider.DefaultValue, slider.Unit) };
                    view.ValueChanged += (_, e) =>
                    {
                        valueLabel.Text = FormatUnit(e.NewValue, slider.Unit);
                        Invoke(slider.Id, e.NewValue.ToString(CultureInfo.InvariantCulture));
                    };
                    var panel = new StackPanel { Orientation = Orientation.Horizontal };
                    panel.Children.Add(view);
                    panel.Children.Add(valueLabel);
                    var current = () => view.Value.ToString(CultureInfo.InvariantCulture);
                    return (panel, view, ValuePreview(slider, current), (slider.Id, current()));
                }

            case NumericControl numeric:
                {
                    var view = new TextBox { Width = 80, Text = numeric.DefaultValue.ToString(CultureInfo.InvariantCulture), VerticalAlignment = VerticalAlignment.Center };
                    void Commit() => CommitValue(view, numeric);
                    view.LostFocus += (_, _) => Commit();
                    view.KeyDown += (_, e) =>
                    {
                        if (e.Key == Key.Enter)
                        {
                            Commit();
                        }
                    };
                    var hint = new TextBlock { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Text = $"[{numeric.Minimum:0.#}-{numeric.Maximum:0.#}]{numeric.Unit}" };
                    var panel = new StackPanel { Orientation = Orientation.Horizontal };
                    panel.Children.Add(view);
                    panel.Children.Add(hint);
                    return (panel, view, ValuePreview(numeric, () => view.Text), (numeric.Id, view.Text));
                }

            case ChoiceControl { Style: ChoiceStyle.RadioGroup } choice:
                {
                    var panel = new StackPanel { Orientation = Orientation.Horizontal };
                    var groupName = "choice_" + control.Id;
                    var radios = new List<RadioButton>();
                    _choiceRadios[choice.Id] = radios;
                    foreach (var option in choice.Options)
                    {
                        var radio = new RadioButton { Content = option, GroupName = groupName, Margin = new Thickness(0, 0, 8, 0), IsChecked = option == choice.DefaultValue };
                        radio.Checked += (_, _) =>
                        {
                            if (_suppressChoiceSend)
                            {
                                return;
                            }

                            if (_customColorLinks.TryGetValue(choice.Id, out var link) && option == link.Option)
                            {
                                ApplyCustomColor(choice.Id, link.Button);
                            }
                            else
                            {
                                Invoke(choice.Id, option);
                            }
                        };
                        radios.Add(radio);
                        panel.Children.Add(radio);
                    }

                    return (panel, panel, () => SendsText(choice.Id, GetCurrentValue(panel)), (choice.Id, GetCurrentValue(panel)));
                }

            case ChoiceControl choice:
                {
                    var view = new ComboBox { ItemsSource = choice.Options, SelectedItem = choice.DefaultValue ?? choice.Options.FirstOrDefault(), Width = 160, VerticalAlignment = VerticalAlignment.Center };
                    view.SelectionChanged += (_, _) =>
                    {
                        if (view.SelectedItem is string selected)
                        {
                            Invoke(choice.Id, selected);
                        }
                    };
                    return (view, view, () => SendsText(choice.Id, GetCurrentValue(view)), (choice.Id, GetCurrentValue(view)));
                }

            case TextFieldControl textField:
                {
                    var view = new TextBox { Text = textField.DefaultValue ?? string.Empty, Width = 160, VerticalAlignment = VerticalAlignment.Center };
                    if (textField.MaxLength is { } max)
                    {
                        view.MaxLength = max;
                    }

                    void Commit() => CommitValue(view, textField);
                    view.LostFocus += (_, _) => Commit();
                    view.KeyDown += (_, e) =>
                    {
                        if (e.Key == Key.Enter)
                        {
                            Commit();
                        }
                    };
                    return (view, view, ValuePreview(textField, () => view.Text), (textField.Id, view.Text));
                }

            case IndicatorControl indicator:
                {
                    var view = new TextBlock { Text = indicator.DefaultValue ?? string.Empty, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Bold };
                    _indicatorLabels[control.Id] = view;
                    return (view, view, null, null);
                }

            case BarGraphControl or StripChartControl or VectorControl:
                {
                    LiveDisplayElement view = LiveDisplayState.For(control) switch
                    {
                        BarGraphState bars => new BarGraphElement(bars),
                        StripChartState strip => new StripChartElement(strip),
                        VectorState vector => new VectorElement(vector),
                        _ => throw new InvalidOperationException($"No live display for '{control.Id}'."),
                    };
                    view.Margin = new Thickness(0, 2, 0, 2);
                    _displays[control.Id] = view;
                    return (view, view, null, null);
                }

            default:
                var fallback = new TextBlock { Text = "(unsupported control)" };
                return (fallback, fallback, null, null);
        }
    }

    private void OpenColorPicker(string buttonId, string targetCommandId)
    {
        if (TryPickColor(buttonId, out var picked))
        {
            Invoke(targetCommandId, FormatColor(picked));
            SelectCustomColorOption(targetCommandId, buttonId);
        }
    }

    // Shared across panel openings (see LastPickedColors), not per window instance - a per-window
    // dictionary here lost the color every time the panel was closed and reopened.
    private bool TryPickColor(string buttonId, out (byte R, byte G, byte B) picked)
    {
        var (r, g, b) = LastPickedColors.Get(buttonId);
        // The window showing this panel's content: this one, or the manifest editor hosting it as a
        // preview (a never-shown window can't own a dialog).
        var picker = new ColorPickerWindow(r, g, b) { Owner = GetWindow(SectionsPanel) ?? this };
        if (picker.ShowDialog() != true)
        {
            picked = default;
            return false;
        }

        picked = (picker.SelectedR, picker.SelectedG, picker.SelectedB);
        LastPickedColors.Set(buttonId, picked);
        if (_colorSwatches.TryGetValue(buttonId, out var swatch))
        {
            ShowSwatch(swatch, picked);
        }

        return true;
    }

    private static string FormatColor((byte R, byte G, byte B) color) =>
        string.Create(CultureInfo.InvariantCulture, $"{color.R},{color.G},{color.B}");

    /// <summary>
    /// The linked "Custom" radio was selected (see <see cref="ButtonControl.ColorPickerChoiceOption"/>):
    /// send the button's last picked color instead of the word "Custom" - opening the picker if none
    /// has been picked yet, rather than silently sending white.
    /// </summary>
    internal void ApplyCustomColor(string choiceId, ButtonControl button)
    {
        if (LastPickedColors.TryGet(button.Id, out var color))
        {
            if (_colorSwatches.TryGetValue(button.Id, out var swatch))
            {
                ShowSwatch(swatch, color);
            }
        }
        else if (!TryPickColor(button.Id, out color))
        {
            return;
        }

        Invoke(choiceId, FormatColor(color));
    }

    /// <summary>After a color pick, check its linked choice option - without sending it a second time.</summary>
    private void SelectCustomColorOption(string choiceId, string buttonId)
    {
        if (!_customColorLinks.TryGetValue(choiceId, out var link) || link.Button.Id != buttonId
            || !_choiceRadios.TryGetValue(choiceId, out var radios))
        {
            return;
        }

        _suppressChoiceSend = true;
        try
        {
            radios.First(radio => (string)radio.Content == link.Option).IsChecked = true;
        }
        finally
        {
            _suppressChoiceSend = false;
        }
    }

    private static void ShowSwatch(Border swatch, (byte R, byte G, byte B) color)
    {
        swatch.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(color.R, color.G, color.B));
        var text = (TextBlock)swatch.Child;
        text.Text = LastPickedColors.ToHex(color);
        text.Foreground = LastPickedColors.UseDarkText(color) ? System.Windows.Media.Brushes.Black : System.Windows.Media.Brushes.White;
        swatch.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Sends one control's command without ever letting its failure escape: a rejected value (the
    /// control surface's own validation) or a device-side failure (the session has then already
    /// disconnected itself, and the main window reports that too) is shown in this panel's status
    /// line. Not a message box: a modal dialog would block the panel's own automated tests.
    /// </summary>
    private void Invoke(string commandId, string? value)
    {
        void Report(Exception ex) =>
            Dispatcher.BeginInvoke(() => StatusText.Text = $"Command failed: {ex.GetBaseException().Message}");

        Task task;
        try
        {
            task = _surface.InvokeAsync(commandId, value);
        }
        catch (Exception ex)
        {
            Report(ex);
            return;
        }

        _ = task.ContinueWith(t => Report(t.Exception!), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    /// <summary>
    /// Commits a typed value through the shared <see cref="ValueValidator"/>: an invalid value is
    /// reported in the status line (never a modal) and not sent; a valid one is normalized back into
    /// the box (e.g. clamped for a <see cref="NumericControl"/>) and sent.
    /// </summary>
    private void CommitValue(TextBox field, UiControl control)
    {
        var result = ValueValidator.Validate(ValueValidator.ConstraintFor(control), field.Text);
        if (!result.IsValid)
        {
            ShowValidationError($"{control.Label}: {result.Error}");
            return;
        }

        ClearValidationError();
        field.Text = result.Value;
        Invoke(control.Id, result.Value);
    }

    /// <summary>Reads and validates every named parameter field's current value, comma-joined; fails on the first invalid one.</summary>
    private bool TryReadParameters(IReadOnlyList<string> fieldIds, out string joined, out string? error)
    {
        var values = new List<string>(fieldIds.Count);
        foreach (var fieldId in fieldIds)
        {
            var raw = _controlViews.TryGetValue(fieldId, out var view) ? GetCurrentValue(view) : string.Empty;
            var constraint = _controlsById.TryGetValue(fieldId, out var fieldControl) ? ValueValidator.ConstraintFor(fieldControl) : null;
            var result = ValueValidator.Validate(constraint, raw);
            if (!result.IsValid)
            {
                joined = string.Empty;
                error = $"{fieldControl?.Label ?? fieldId}: {result.Error}";
                return false;
            }

            values.Add(result.Value);
        }

        joined = string.Join(',', values);
        error = null;
        return true;
    }

    private void ShowValidationError(string error)
    {
        StatusText.Text = $"{error} Not sent.";
        _showingValidationError = true;
    }

    private void ClearValidationError()
    {
        if (_showingValidationError)
        {
            StatusText.Text = _baseStatus;
            _showingValidationError = false;
        }
    }

    private static string FormatUnit(double value, string? unit) => $"{value:0.#}{unit}";

    /// <summary>Reads a sibling control's current value for <see cref="ButtonControl.ParameterFieldIds"/> — see the branch above.</summary>
    private static string GetCurrentValue(FrameworkElement view) => view switch
    {
        TextBox textBox => textBox.Text,
        ComboBox { SelectedItem: string selected } => selected,
        CheckBox checkBox => checkBox.IsChecked == true ? "1" : "0",
        Slider slider => slider.Value.ToString(CultureInfo.InvariantCulture),
        StackPanel radios => radios.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Content as string ?? string.Empty,
        TextBlock textBlock => textBlock.Text,
        _ => string.Empty,
    };
}
