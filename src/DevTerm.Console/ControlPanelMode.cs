using System.Drawing;
using System.Globalization;
using DevTerm.Configuration;
using DevTerm.Core.Control;
using DevTerm.Core.Presenters;
using DevTerm.UiDefinitions;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

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
    internal static ControlPanelWindowParts BuildWindow(IApplication app, UiDefinition definition, IControlSurface surface, IPresenter? structuredSource, string title)
    {
        var window = new Window
        {
            Title = title,
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };

        var hasDescription = !string.IsNullOrWhiteSpace(definition.Description);
        var contentHeight = definition.Sections.Sum(s => s.Controls.Count + 3) + 1 + (hasDescription ? 2 : 0);
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
        if (hasDescription && !string.IsNullOrWhiteSpace(definition.Description))
        {
            var descriptionLabel = new Label
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(2),
                Height = 1,
                Text = definition.Description,
            };
            formContent.Add(descriptionLabel);
            previousFrame = descriptionLabel;
        }

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
                AddControlRow(app, frame, row, section.Controls[row], surface, controlViews, indicatorLabels);
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
                    app.Invoke(() =>
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

        void scrollOnKey(object? _, Key key)
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
        }

        app.Keyboard.KeyDown += scrollOnKey;
        window.Disposing += (_, _) => app.Keyboard.KeyDown -= scrollOnKey;

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
        IApplication app,
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
            case ButtonControl { ColorPickerTargetCommandId: { } colorTargetId } button:
                var colorButtonView = new Button { X = Pos.Right(label) + 1, Y = row, Text = control.Label };
                colorButtonView.Accepting += (_, e) =>
                {
                    var (lastR, lastG, lastB) = LastPickedColors.Get(button.Id);
                    if (PickColor(app, lastR, lastG, lastB) is { } picked)
                    {
                        LastPickedColors.Set(button.Id, picked);
                        Invoke(app, surface, colorTargetId, $"{picked.R},{picked.G},{picked.B}");
                    }

                    e.Handled = true;
                };
                frame.Add(colorButtonView);
                controlViews[control.Id] = colorButtonView;
                break;

            case ButtonControl { ParameterFieldIds: { } parameterFieldIds } button:
                var parameterButtonView = new Button { X = Pos.Right(label) + 1, Y = row, Text = control.Label };
                parameterButtonView.Accepting += (_, e) =>
                {
                    var joined = string.Join(',', parameterFieldIds.Select(id => controlViews.TryGetValue(id, out var fieldView) ? GetCurrentValue(fieldView) : string.Empty));
                    Invoke(app, surface, button.CommandId ?? button.Id, joined);
                    e.Handled = true;
                };
                frame.Add(parameterButtonView);
                controlViews[control.Id] = parameterButtonView;
                break;

            case ButtonControl button:
                var buttonView = new Button { X = Pos.Right(label) + 1, Y = row, Text = control.Label };
                buttonView.Accepting += (_, e) =>
                {
                    Invoke(app, surface, button.CommandId ?? button.Id, null);
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
                    Invoke(app, surface, toggle.Id, checkBox.Value == CheckState.Checked ? "1" : "0");
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
                    Invoke(app, surface, slider.Id, clamped.ToString(CultureInfo.InvariantCulture));
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
                    Invoke(app, surface, numeric.Id, clamped.ToString(CultureInfo.InvariantCulture));
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
                        Invoke(app, surface, choice.Id, choice.Options[index]);
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
                    Invoke(app, surface, textField.Id, value);
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

    /// <summary>Reads a sibling control's current value for <see cref="ButtonControl.ParameterFieldIds"/> — see the branch above.</summary>
    private static string GetCurrentValue(View view) => view switch
    {
        TextField textField => textField.Text,
        OptionSelector { Value: { } index, Labels: { } labels } when index >= 0 && index < labels.Count => labels[index],
        CheckBox checkBox => checkBox.Value == CheckState.Checked ? "1" : "0",
        Label label => label.Text,
        _ => string.Empty,
    };

    /// <summary>
    /// A small nested modal RGB/HSV color picker, opened by any <c>ButtonControl</c> with
    /// <c>ColorPickerTargetCommandId</c> set (see <see cref="AddControlRow"/>) — the TUI half of the
    /// same generic color-picker support as <c>DevTerm.Wpf.ColorPickerWindow</c>. No slider/hex
    /// widget exists in the installed Terminal.Gui package (see this class's own remarks on that),
    /// so every field is a bounded <see cref="TextField"/>, synced on Enter the same way
    /// Slider/Numeric rows above are.
    /// </summary>
    /// <summary>
    /// Sends one control's command without ever letting its failure escape: a rejected value (a
    /// control surface's own validation) or a device-side failure (the session has then already
    /// disconnected itself) is shown in an error dialog over this panel - which, being modal, hides
    /// the main window's output pane where the disconnect is also reported.
    /// </summary>
    private static void Invoke(IApplication app, IControlSurface surface, string commandId, string? value)
    {
        void Report(Exception ex) =>
            app.Invoke(() => MessageBox.ErrorQuery(app, "dev-term — command failed", ex.GetBaseException().Message, "Ok"));

        Task task;
        try
        {
            task = surface.InvokeAsync(commandId, value);
        }
        catch (Exception ex)
        {
            Report(ex);
            return;
        }

        _ = task.ContinueWith(t => Report(t.Exception!), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    private static (byte R, byte G, byte B)? PickColor(IApplication app, byte initialR, byte initialG, byte initialB)
    {
        (byte R, byte G, byte B)? picked = null;
        var dialog = new Dialog { Title = "Custom Color", Width = 40, Height = 12 };

        var rField = new TextField { X = 4, Y = 0, Width = 6, Text = initialR.ToString(CultureInfo.InvariantCulture) };
        var gField = new TextField { X = 4, Y = 1, Width = 6, Text = initialG.ToString(CultureInfo.InvariantCulture) };
        var bField = new TextField { X = 4, Y = 2, Width = 6, Text = initialB.ToString(CultureInfo.InvariantCulture) };
        var hField = new TextField { X = 20, Y = 0, Width = 6 };
        var sField = new TextField { X = 20, Y = 1, Width = 6 };
        var vField = new TextField { X = 20, Y = 2, Width = 6 };
        var hexField = new TextField { X = 4, Y = 4, Width = 10 };

        void SetFromRgb(byte r, byte g, byte b)
        {
            rField.Text = r.ToString(CultureInfo.InvariantCulture);
            gField.Text = g.ToString(CultureInfo.InvariantCulture);
            bField.Text = b.ToString(CultureInfo.InvariantCulture);
            var (h, s, v) = RgbToHsv(r, g, b);
            hField.Text = h.ToString("0", CultureInfo.InvariantCulture);
            sField.Text = (s * 100).ToString("0", CultureInfo.InvariantCulture);
            vField.Text = (v * 100).ToString("0", CultureInfo.InvariantCulture);
            hexField.Text = $"{r:X2}{g:X2}{b:X2}";
        }

        byte CurrentR() => (byte)Math.Clamp(ParseOr(rField.Text, 0), 0, 255);
        byte CurrentG() => (byte)Math.Clamp(ParseOr(gField.Text, 0), 0, 255);
        byte CurrentB() => (byte)Math.Clamp(ParseOr(bField.Text, 0), 0, 255);

        rField.Accepting += (_, e) => { SetFromRgb(CurrentR(), CurrentG(), CurrentB()); e.Handled = true; };
        gField.Accepting += (_, e) => { SetFromRgb(CurrentR(), CurrentG(), CurrentB()); e.Handled = true; };
        bField.Accepting += (_, e) => { SetFromRgb(CurrentR(), CurrentG(), CurrentB()); e.Handled = true; };

        void CommitHsv()
        {
            var h = Math.Clamp(ParseOr(hField.Text, 0), 0, 360);
            var s = Math.Clamp(ParseOr(sField.Text, 0), 0, 100) / 100.0;
            var v = Math.Clamp(ParseOr(vField.Text, 0), 0, 100) / 100.0;
            var (r, g, b) = HsvToRgb(h, s, v);
            SetFromRgb(r, g, b);
        }

        hField.Accepting += (_, e) => { CommitHsv(); e.Handled = true; };
        sField.Accepting += (_, e) => { CommitHsv(); e.Handled = true; };
        vField.Accepting += (_, e) => { CommitHsv(); e.Handled = true; };
        hexField.Accepting += (_, e) =>
        {
            var text = hexField.Text.Trim().TrimStart('#');
            if (text.Length == 6
                && byte.TryParse(text.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hr)
                && byte.TryParse(text.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hg)
                && byte.TryParse(text.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hb))
            {
                SetFromRgb(hr, hg, hb);
            }

            e.Handled = true;
        };

        var okButton = new Button { X = 0, Y = 6, Text = "OK", IsDefault = true };
        okButton.Accepting += (_, e) =>
        {
            picked = (CurrentR(), CurrentG(), CurrentB());
            e.Handled = true;
            app.RequestStop();
        };
        var cancelButton = new Button { X = Pos.Right(okButton) + 1, Y = 6, Text = "Cancel" };
        cancelButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            app.RequestStop();
        };

        dialog.Add(
            new Label { X = 0, Y = 0, Text = "R:" }, rField,
            new Label { X = 0, Y = 1, Text = "G:" }, gField,
            new Label { X = 0, Y = 2, Text = "B:" }, bField,
            new Label { X = 16, Y = 0, Text = "H:" }, hField,
            new Label { X = 16, Y = 1, Text = "S:" }, sField,
            new Label { X = 16, Y = 2, Text = "V:" }, vField,
            new Label { X = 0, Y = 4, Text = "Hex:" }, hexField,
            okButton,
            cancelButton);

        SetFromRgb(initialR, initialG, initialB);
        app.Run(dialog);
        return picked;
    }

    /// <summary>Standard RGB→HSV conversion; hue in degrees [0,360), saturation/value in [0,1] — see <c>DevTerm.Wpf.ColorPickerWindow</c>'s identical WPF-side helper.</summary>
    private static (double H, double S, double V) RgbToHsv(byte r, byte g, byte b)
    {
        double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
        var max = Math.Max(rf, Math.Max(gf, bf));
        var min = Math.Min(rf, Math.Min(gf, bf));
        var delta = max - min;

        double hue;
        if (delta < 1e-9)
        {
            hue = 0;
        }
        else if (max == rf)
        {
            hue = 60 * (((gf - bf) / delta) % 6);
        }
        else if (max == gf)
        {
            hue = 60 * (((bf - rf) / delta) + 2);
        }
        else
        {
            hue = 60 * (((rf - gf) / delta) + 4);
        }

        if (hue < 0)
        {
            hue += 360;
        }

        var saturation = max < 1e-9 ? 0 : delta / max;
        return (hue, saturation, max);
    }

    /// <summary>Standard HSV→RGB conversion; hue in degrees [0,360), saturation/value in [0,1] — see <c>DevTerm.Wpf.ColorPickerWindow</c>'s identical WPF-side helper.</summary>
    private static (byte R, byte G, byte B) HsvToRgb(double h, double s, double v)
    {
        var c = v * s;
        var hPrime = (h % 360) / 60.0;
        var x = c * (1 - Math.Abs((hPrime % 2) - 1));
        var (r1, g1, b1) = hPrime switch
        {
            < 1 => (c, x, 0.0),
            < 2 => (x, c, 0.0),
            < 3 => (0.0, c, x),
            < 4 => (0.0, x, c),
            < 5 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };

        var m = v - c;
        return ((byte)Math.Round((r1 + m) * 255), (byte)Math.Round((g1 + m) * 255), (byte)Math.Round((b1 + m) * 255));
    }
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
