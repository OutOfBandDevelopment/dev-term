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
/// Builds controls directly in code-behind rather than via XAML data binding (unlike
/// <see cref="DeviceProfilesWindow"/>'s view-model-bound form) because the control set is generated
/// from a runtime <see cref="UiDefinition"/>, not a fixed compile-time set of named fields.
/// </remarks>
public partial class ControlPanelWindow : Window
{
    private readonly IControlSurface _surface;
    private readonly Dictionary<string, FrameworkElement> _controlViews = [];
    private readonly Dictionary<string, TextBlock> _indicatorLabels = [];
    private readonly Dictionary<string, TextBlock> _controlLabels = [];

    /// <summary>Every interactive/display view, keyed by its <c>UiControl.Id</c> — for tests to drive/assert against, mirroring <c>ControlPanelWindowParts.ControlViews</c> in the TUI renderer.</summary>
    internal IReadOnlyDictionary<string, FrameworkElement> ControlViews => _controlViews;

    /// <summary>Each row's left-hand label <see cref="TextBlock"/> (the <c>control.Label + ":"</c> caption), keyed by <c>UiControl.Id</c> — for tests asserting the label wraps instead of overlapping its row content when a profile's label text is long (see <see cref="BuildControlRow"/>).</summary>
    internal IReadOnlyDictionary<string, TextBlock> ControlLabels => _controlLabels;

    /// <summary>The subset of <see cref="ControlViews"/> that are <see cref="IndicatorControl"/> labels, for tests asserting a live value update.</summary>
    internal IReadOnlyDictionary<string, TextBlock> IndicatorLabels => _indicatorLabels;

    public ControlPanelWindow(UiDefinition definition, IControlSurface surface, IPresenter? structuredSource)
    {
        InitializeComponent();
        Title = $"dev-term — {definition.Name}";
        _surface = surface;

        if (!string.IsNullOrWhiteSpace(definition.Description))
        {
            DescriptionText.Text = definition.Description;
            DescriptionText.Visibility = Visibility.Visible;
        }

        foreach (var section in definition.Sections)
        {
            var group = new GroupBox { Header = section.Label ?? string.Empty, Margin = new Thickness(0, 0, 0, 8) };
            var stack = new StackPanel();
            foreach (var control in section.Controls)
            {
                stack.Children.Add(BuildControlRow(control));
            }

            group.Content = stack;
            SectionsPanel.Children.Add(group);
        }

        StatusText.Text = structuredSource is IStructuredPresenter
            ? string.Empty
            : "Not decoding — connect with the matching --presenter to see live values.";

        if (structuredSource is IStructuredPresenter structuredPresenter)
        {
            structuredPresenter.ValuesChanged += OnValuesChanged;
            Closed += (_, _) => structuredPresenter.ValuesChanged -= OnValuesChanged;
        }
    }

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
        });
    }

    private FrameworkElement BuildControlRow(UiControl control)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var label = new TextBlock { Text = control.Label + ":", Width = 120, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(label, Dock.Left);
        row.Children.Add(label);
        _controlLabels[control.Id] = label;

        var (rowContent, tracked) = BuildWidget(control);
        row.Children.Add(rowContent);
        _controlViews[control.Id] = tracked;
        return row;
    }

    private (FrameworkElement RowContent, FrameworkElement Tracked) BuildWidget(UiControl control)
    {
        switch (control)
        {
            case ButtonControl { ColorPickerTargetCommandId: { } colorTargetId } button:
                {
                    var view = new Button { Content = control.Label, Padding = new Thickness(8, 2, 8, 2), HorizontalAlignment = HorizontalAlignment.Left };
                    view.Click += (_, _) => OpenColorPicker(button.Id, colorTargetId);
                    return (view, view);
                }

            case ButtonControl { ParameterFieldIds: { } parameterFieldIds } button:
                {
                    var view = new Button { Content = control.Label, Padding = new Thickness(8, 2, 8, 2), HorizontalAlignment = HorizontalAlignment.Left };
                    view.Click += (_, _) =>
                    {
                        var joined = string.Join(',', parameterFieldIds.Select(id => _controlViews.TryGetValue(id, out var fieldView) ? GetCurrentValue(fieldView) : string.Empty));
                        Invoke(button.CommandId ?? button.Id, joined);
                    };
                    return (view, view);
                }

            case ButtonControl button:
                {
                    var view = new Button { Content = control.Label, Padding = new Thickness(8, 2, 8, 2), HorizontalAlignment = HorizontalAlignment.Left };
                    view.Click += (_, _) => Invoke(button.CommandId ?? button.Id, null);
                    return (view, view);
                }

            case ToggleControl toggle:
                {
                    var view = new CheckBox { IsChecked = toggle.DefaultValue, VerticalAlignment = VerticalAlignment.Center };
                    view.Checked += (_, _) => Invoke(toggle.Id, "1");
                    view.Unchecked += (_, _) => Invoke(toggle.Id, "0");
                    return (view, view);
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
                    return (panel, view);
                }

            case NumericControl numeric:
                {
                    var view = new TextBox { Width = 80, Text = numeric.DefaultValue.ToString(CultureInfo.InvariantCulture), VerticalAlignment = VerticalAlignment.Center };
                    void Commit() => CommitNumeric(view, numeric.Id, numeric.Minimum, numeric.Maximum, numeric.DefaultValue);
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
                    return (panel, view);
                }

            case ChoiceControl { Style: ChoiceStyle.RadioGroup } choice:
                {
                    var panel = new StackPanel { Orientation = Orientation.Horizontal };
                    var groupName = "choice_" + control.Id;
                    foreach (var option in choice.Options)
                    {
                        var radio = new RadioButton { Content = option, GroupName = groupName, Margin = new Thickness(0, 0, 8, 0), IsChecked = option == choice.DefaultValue };
                        radio.Checked += (_, _) => Invoke(choice.Id, option);
                        panel.Children.Add(radio);
                    }

                    return (panel, panel);
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
                    return (view, view);
                }

            case TextFieldControl textField:
                {
                    var view = new TextBox { Text = textField.DefaultValue ?? string.Empty, Width = 160, VerticalAlignment = VerticalAlignment.Center };
                    if (textField.MaxLength is { } max)
                    {
                        view.MaxLength = max;
                    }

                    void Commit() => Invoke(textField.Id, view.Text);
                    view.LostFocus += (_, _) => Commit();
                    view.KeyDown += (_, e) =>
                    {
                        if (e.Key == Key.Enter)
                        {
                            Commit();
                        }
                    };
                    return (view, view);
                }

            case IndicatorControl indicator:
                {
                    var view = new TextBlock { Text = indicator.DefaultValue ?? string.Empty, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Bold };
                    _indicatorLabels[control.Id] = view;
                    return (view, view);
                }

            default:
                var fallback = new TextBlock { Text = "(unsupported control)" };
                return (fallback, fallback);
        }
    }

    private void OpenColorPicker(string buttonId, string targetCommandId)
    {
        // Shared across panel openings (see LastPickedColors), not per window instance - a
        // per-window dictionary here lost the color every time the panel was closed and reopened.
        var (r, g, b) = LastPickedColors.Get(buttonId);
        var picker = new ColorPickerWindow(r, g, b) { Owner = this };
        if (picker.ShowDialog() == true)
        {
            LastPickedColors.Set(buttonId, (picker.SelectedR, picker.SelectedG, picker.SelectedB));
            var value = string.Create(CultureInfo.InvariantCulture, $"{picker.SelectedR},{picker.SelectedG},{picker.SelectedB}");
            Invoke(targetCommandId, value);
        }
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

    private void CommitNumeric(TextBox field, string id, double minimum, double maximum, double fallback)
    {
        var parsed = double.TryParse(field.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;
        var clamped = Math.Clamp(parsed, minimum, maximum);
        field.Text = clamped.ToString(CultureInfo.InvariantCulture);
        Invoke(id, clamped.ToString(CultureInfo.InvariantCulture));
    }

    private static string FormatUnit(double value, string? unit) => $"{value:0.#}{unit}";

    /// <summary>Reads a sibling control's current value for <see cref="ButtonControl.ParameterFieldIds"/> — see the branch above.</summary>
    private static string GetCurrentValue(FrameworkElement view) => view switch
    {
        TextBox textBox => textBox.Text,
        ComboBox { SelectedItem: string selected } => selected,
        CheckBox checkBox => checkBox.IsChecked == true ? "1" : "0",
        TextBlock textBlock => textBlock.Text,
        _ => string.Empty,
    };
}
