using System.Drawing;
using System.Globalization;
using DevTerm.Core.Control;
using DevTerm.Core.Presenters;
using DevTerm.UiDefinitions;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Views;
using Terminal.Gui.ViewBase;

namespace DevTerm.Console;

/// <summary>
/// Walks any <see cref="UiDefinition"/> and renders it as real, wired Terminal.Gui controls against
/// any <see cref="IControlSurface"/> — the generic renderer described in
/// docs/design/ui-definitions.md, proven first against the K8055's own definition
/// (<c>DevTerm.Devices.K8055.K8055UiDefinition</c>) but not specific to it. Mirrors
/// <see cref="ConfigureMode"/>'s split between a testable <see cref="BuildWindow"/> and the caller
/// (<see cref="TuiMode"/>'s "_K8055 Control Panel..." menu item) owning the nested
/// <c>Application.Run</c> call.
/// </summary>
/// <remarks>
/// Terminal.Gui v2.5.0 has no generic slider or combo-box/radio-group widget (checked directly by
/// reflecting the installed package — see docs/coding-standards.md's "check the installed API shape"
/// rule) — <c>Slider</c>/<c>Numeric</c> controls render as a bounded <see cref="TextField"/> instead
/// of a drag affordance, and <c>Choice</c> controls (both <see cref="ChoiceStyle"/> values) render as
/// an <see cref="OptionSelector"/>, the one selection widget the installed package actually has.
/// </remarks>
internal static class ControlPanelMode
{
    internal static ControlPanelWindowParts BuildWindow(UiDefinition definition, IControlSurface surface, IPresenter? structuredSource, string title)
    {
        var window = new Window
        {
            Title = title,
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };

        var contentHeight = definition.Sections.Sum(s => s.Controls.Count + 3) + 1;
        var formContent = new View
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),

            // A plain View defaults to CanFocus = false, which blocks focus (and so input) from
            // ever reaching any child — the same gotcha ConfigureMode's own scrollable container
            // hit first (see its comment on this same line).
            CanFocus = true,
        };
        formContent.SetContentSize(new Size(100, contentHeight));
        formContent.ViewportSettings |= ViewportSettingsFlags.AllowNegativeY | ViewportSettingsFlags.HasVerticalScrollBar;

        var controlViews = new Dictionary<string, View>();
        var indicatorLabels = new Dictionary<string, Label>();

        View? previousFrame = null;
        foreach (var section in definition.Sections)
        {
            var frame = new FrameView
            {
                Title = section.Label ?? string.Empty,
                X = 0,
                Y = previousFrame is null ? 0 : Pos.Bottom(previousFrame) + 1,
                Width = Dim.Fill(2),
                Height = section.Controls.Count + 2,
            };

            for (var row = 0; row < section.Controls.Count; row++)
            {
                AddControlRow(frame, row, section.Controls[row], surface, controlViews, indicatorLabels);
            }

            formContent.Add(frame);
            previousFrame = frame;
        }

        var statusLabel = new Label
        {
            X = 0,
            Y = previousFrame is null ? 0 : Pos.Bottom(previousFrame) + 1,
            Width = Dim.Fill(),
            Text = structuredSource is IStructuredPresenter
                ? string.Empty
                : "Not decoding — connect with the matching --presenter to see live values.",
        };
        formContent.Add(statusLabel);

        window.Add(formContent);

        EventHandler<IReadOnlyDictionary<string, string>>? onValuesChanged = null;
        if (structuredSource is IStructuredPresenter structuredPresenter)
        {
            onValuesChanged = (_, values) =>
            {
                try
                {
                    Application.Invoke(() =>
                    {
                        foreach (var (id, value) in values)
                        {
                            if (indicatorLabels.TryGetValue(id, out var label))
                            {
                                label.Text = value;
                            }
                        }
                    });
                }
                catch (NotInitializedException)
                {
                }
            };
            structuredPresenter.ValuesChanged += onValuesChanged;
            window.Disposing += (_, _) => structuredPresenter.ValuesChanged -= onValuesChanged;
        }

        // PageUp/PageDown scroll the form when it doesn't fit — same global-KeyDown-based approach
        // as ConfigureMode's own scrollable form, for the same reason (a per-view KeyDown handler
        // doesn't reliably see a key already routed to a focused child first).
        void ScrollBy(int delta)
        {
            var maxY = Math.Max(0, contentHeight - formContent.Viewport.Height);
            var newY = Math.Clamp(formContent.Viewport.Y + delta, 0, maxY);
            formContent.Viewport = formContent.Viewport with { Y = newY };
        }

        EventHandler<Key>? scrollOnKey = null;
        scrollOnKey = (_, key) =>
        {
            var delta = key == Key.PageDown ? formContent.Viewport.Height
                : key == Key.PageUp ? -formContent.Viewport.Height
                : 0;

            if (delta == 0)
            {
                return;
            }

            ScrollBy(delta);
            key.Handled = true;
        };
        Application.KeyDown += scrollOnKey;
        window.Disposing += (_, _) => Application.KeyDown -= scrollOnKey;

        formContent.MouseEvent += (_, mouse) =>
        {
            if (mouse.Flags.HasFlag(MouseFlags.WheeledDown))
            {
                ScrollBy(1);
                mouse.Handled = true;
            }
            else if (mouse.Flags.HasFlag(MouseFlags.WheeledUp))
            {
                ScrollBy(-1);
                mouse.Handled = true;
            }
        };

        foreach (var child in formContent.SubViews)
        {
            child.HasFocusChanged += (_, e) =>
            {
                if (e.NewValue)
                {
                    var top = child.Frame.Y;
                    var bottom = top + child.Frame.Height;
                    var viewport = formContent.Viewport;
                    if (top < viewport.Y)
                    {
                        ScrollBy(top - viewport.Y);
                    }
                    else if (bottom > viewport.Y + viewport.Height)
                    {
                        ScrollBy(bottom - (viewport.Y + viewport.Height));
                    }
                }
            };
        }

        return new ControlPanelWindowParts
        {
            Window = window,
            ControlViews = controlViews,
            IndicatorLabels = indicatorLabels,
        };
    }

    private static void AddControlRow(
        FrameView frame,
        int row,
        UiControl control,
        IControlSurface surface,
        Dictionary<string, View> controlViews,
        Dictionary<string, Label> indicatorLabels)
    {
        var label = new Label { X = 0, Y = row, Text = control.Label + ":" };
        frame.Add(label);

        switch (control)
        {
            case ButtonControl button:
                var buttonView = new Button { X = Pos.Right(label) + 1, Y = row, Text = control.Label };
                buttonView.Accepting += (_, e) =>
                {
                    _ = surface.InvokeAsync(button.CommandId ?? button.Id, null);
                    e.Handled = true;
                };
                frame.Add(buttonView);
                controlViews[control.Id] = buttonView;
                break;

            case ToggleControl toggle:
                var checkBox = new CheckBox
                {
                    X = Pos.Right(label) + 1,
                    Y = row,
                    Value = toggle.DefaultValue ? CheckState.Checked : CheckState.UnChecked,
                };
                checkBox.ValueChanged += (_, _) =>
                    _ = surface.InvokeAsync(toggle.Id, checkBox.Value == CheckState.Checked ? "1" : "0");
                frame.Add(checkBox);
                controlViews[control.Id] = checkBox;
                break;

            case SliderControl slider:
                var sliderField = new TextField
                {
                    X = Pos.Right(label) + 1,
                    Y = row,
                    Width = 10,
                    Text = slider.DefaultValue.ToString(CultureInfo.InvariantCulture),
                };
                sliderField.Accepting += (_, e) =>
                {
                    var clamped = Math.Clamp(ParseOr(sliderField.Text, slider.DefaultValue), slider.Minimum, slider.Maximum);
                    sliderField.Text = clamped.ToString(CultureInfo.InvariantCulture);
                    _ = surface.InvokeAsync(slider.Id, clamped.ToString(CultureInfo.InvariantCulture));
                    e.Handled = true;
                };
                frame.Add(sliderField);
                var sliderHint = new Label { X = Pos.Right(sliderField) + 1, Y = row, Text = $"[{slider.Minimum:0.#}-{slider.Maximum:0.#}]{slider.Unit}" };
                frame.Add(sliderHint);
                controlViews[control.Id] = sliderField;
                break;

            case NumericControl numeric:
                var numericField = new TextField
                {
                    X = Pos.Right(label) + 1,
                    Y = row,
                    Width = 10,
                    Text = numeric.DefaultValue.ToString(CultureInfo.InvariantCulture),
                };
                numericField.Accepting += (_, e) =>
                {
                    var clamped = Math.Clamp(ParseOr(numericField.Text, numeric.DefaultValue), numeric.Minimum, numeric.Maximum);
                    numericField.Text = clamped.ToString(CultureInfo.InvariantCulture);
                    _ = surface.InvokeAsync(numeric.Id, clamped.ToString(CultureInfo.InvariantCulture));
                    e.Handled = true;
                };
                frame.Add(numericField);
                var numericHint = new Label { X = Pos.Right(numericField) + 1, Y = row, Text = $"[{numeric.Minimum:0.#}-{numeric.Maximum:0.#}]{numeric.Unit}" };
                frame.Add(numericHint);
                controlViews[control.Id] = numericField;
                break;

            case ChoiceControl choice:
                var selector = new OptionSelector
                {
                    X = Pos.Right(label) + 1,
                    Y = row,
                    Orientation = Orientation.Horizontal,
                    HorizontalSpace = 2,
                    Labels = choice.Options,
                };
                var defaultIndex = choice.DefaultValue is { } dv ? choice.Options.IndexOf(dv) : -1;
                selector.Value = defaultIndex >= 0 ? defaultIndex : 0;
                selector.ValueChanged += (_, _) =>
                {
                    if (selector.Value is { } index && index >= 0 && index < choice.Options.Count)
                    {
                        _ = surface.InvokeAsync(choice.Id, choice.Options[index]);
                    }
                };
                frame.Add(selector);
                controlViews[control.Id] = selector;
                break;

            case TextFieldControl textField:
                var textFieldView = new TextField { X = Pos.Right(label) + 1, Y = row, Width = 20, Text = textField.DefaultValue ?? string.Empty };
                textFieldView.Accepting += (_, e) =>
                {
                    var value = textField.MaxLength is { } max && textFieldView.Text.Length > max
                        ? textFieldView.Text[..max]
                        : textFieldView.Text;
                    textFieldView.Text = value;
                    _ = surface.InvokeAsync(textField.Id, value);
                    e.Handled = true;
                };
                frame.Add(textFieldView);
                controlViews[control.Id] = textFieldView;
                break;

            case IndicatorControl indicator:
                var indicatorLabel = new Label { X = Pos.Right(label) + 1, Y = row, Text = indicator.DefaultValue ?? string.Empty };
                frame.Add(indicatorLabel);
                controlViews[control.Id] = indicatorLabel;
                indicatorLabels[control.Id] = indicatorLabel;
                break;
        }
    }

    private static double ParseOr(string text, double fallback) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;
}

/// <summary>The controls a test needs to drive a rendered control panel headlessly.</summary>
internal sealed class ControlPanelWindowParts
{
    public required Window Window { get; init; }

    /// <summary>Every interactive/display view, keyed by its <c>UiControl.Id</c> — a dictionary rather than named properties (as <see cref="ConfigureWindowParts"/> uses) because this renderer's control set is data-driven, not fixed at compile time.</summary>
    public required IReadOnlyDictionary<string, View> ControlViews { get; init; }

    /// <summary>The subset of <see cref="ControlViews"/> that are <see cref="IndicatorControl"/> labels, for tests asserting a live value update.</summary>
    public required IReadOnlyDictionary<string, Label> IndicatorLabels { get; init; }
}
