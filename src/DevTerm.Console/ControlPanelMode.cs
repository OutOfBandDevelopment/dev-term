using System.Drawing;
using System.Globalization;
using System.Text;
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
/// <para>
/// Terminal.Gui v2.5.0 has no generic slider or combo-box/radio-group widget (checked directly by
/// reflecting the installed package — see docs/coding-standards.md's "check the installed API shape"
/// rule) — <c>Slider</c>/<c>Numeric</c> controls render as a bounded <see cref="TextField"/> instead
/// of a drag affordance, and <c>Choice</c> controls (both <see cref="ChoiceStyle"/> values) render as
/// an <see cref="OptionSelector"/>, the one selection widget the installed package actually has.
/// </para>
/// <para>
/// It has no expander either: each labeled section gets a focusable <c>[-] Name</c>/<c>[+] Name</c>
/// header <see cref="Button"/> that shows/hides the section's rows, and every section below it is
/// re-positioned (<c>Reflow</c>) so a collapsed section leaves no gap. The definition's
/// <see cref="UiDefinition.Description"/> renders as a collapsible "Notes" section after the rest.
/// Within a section every control starts in the same column (the longest label plus padding). There's
/// no hover in a terminal, so what a command-sending control would send (via the surface's optional
/// <see cref="ICommandPreview"/>) is shown in a footer line while that control — or a field feeding
/// it — has focus, and such controls get a small <c>(i)</c> marker.
/// </para>
/// </remarks>
internal static class ControlPanelMode
{
    internal const string NotesSectionLabel = "Notes";

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

        var formContent = new View
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),

            // Two rows reserved at the bottom for the always-visible footer (preview + message).
            Height = Dim.Fill(2),

            // A plain View defaults to CanFocus = false, which blocks focus (and so input) from
            // ever reaching any child — the same gotcha ConfigureMode's own scrollable container
            // hit first (see its comment on this same line).
            CanFocus = true,
        };
        formContent.ViewportSettings |= ViewportSettingsFlags.AllowNegativeY | ViewportSettingsFlags.HasVerticalScrollBar | ViewportSettingsFlags.HasHorizontalScrollBar;

        var previewLabel = new Label { X = 0, Y = Pos.AnchorEnd(2), Width = Dim.Fill(), Height = 1, Text = string.Empty };
        var messageLabel = new Label { X = 0, Y = Pos.AnchorEnd(1), Width = Dim.Fill(), Height = 1, Text = string.Empty };

        var panel = new PanelState(app, surface, definition, previewLabel, messageLabel);

        var blocks = definition.Sections.Select(section => BuildSection(panel, section)).ToList();
        if (!string.IsNullOrWhiteSpace(definition.Description))
        {
            blocks.Add(BuildNotesSection(app, definition.Description));
        }

        // Each labeled section opens the way it was last left for this definition (see
        // SectionExpansionState) — expanded the first time.
        foreach (var block in blocks.Where(b => b.Header is not null))
        {
            block.Expanded = SectionExpansionState.IsExpanded(definition.Name, block.Label);
        }

        var statusLabel = new Label
        {
            X = 0,
            Width = Dim.Fill(),
            Text = structuredSource is IStructuredPresenter
                ? string.Empty
                : "Not decoding — connect with the matching --presenter to see live values.",
        };

        foreach (var block in blocks)
        {
            if (block.Header is not null)
            {
                formContent.Add(block.Header);
            }

            formContent.Add(block.Body);
        }

        formContent.Add(statusLabel);
        window.Add(formContent, previewLabel, messageLabel);

        var contentHeight = 0;

        // The form's content width: the widest row as last laid out (see MeasureContentWidth), never
        // narrower than the viewport. Rows wider than the window scroll horizontally instead of
        // running off the right edge.
        var contentWidth = 200;

        // Absolute rows, recomputed on every expand/collapse: header, then (if expanded) the
        // section's rows, then one blank row — so collapsing a section pulls everything below it up.
        void Reflow()
        {
            var y = 0;
            foreach (var block in blocks)
            {
                if (block.Header is { } header)
                {
                    header.Y = y;
                    panel.Tops[header] = y;
                    header.Text = HeaderText(block.Label, block.Expanded);
                    y++;
                }

                block.Body.Visible = block.Expanded;
                if (block.Expanded)
                {
                    block.Body.Y = y;
                    panel.Tops[block.Body] = y;
                    y += block.Rows;
                }

                y++;
            }

            statusLabel.Y = y;
            contentHeight = y + 1;
            formContent.SetContentSize(new Size(Math.Max(contentWidth, 1), contentHeight));
            var maxY = Math.Max(0, contentHeight - formContent.Viewport.Height);
            if (formContent.Viewport.Y > maxY)
            {
                formContent.Viewport = formContent.Viewport with { Y = maxY };
            }
        }

        foreach (var block in blocks)
        {
            if (block.Header is { } header)
            {
                header.Accepting += (_, e) =>
                {
                    block.Expanded = !block.Expanded;
                    SectionExpansionState.Set(definition.Name, block.Label, block.Expanded);
                    Reflow();
                    e.Handled = true;
                };
            }
        }

        Reflow();

        // After every layout pass: re-wrap the Notes if the visible width changed (a terminal
        // resize), and re-measure the widest row so the horizontal scroll range matches it. Each
        // only acts on a real change, so the extra layout pass SetContentSize triggers settles.
        var wrapWidth = -1;
        formContent.SubViewsLaidOut += (_, _) =>
        {
            var viewportWidth = formContent.Viewport.Width;
            if (viewportWidth > 0 && viewportWidth != wrapWidth)
            {
                wrapWidth = viewportWidth;
                var rowsChanged = false;
                foreach (var block in blocks)
                {
                    rowsChanged |= block.Rewrap?.Invoke(viewportWidth) ?? false;
                }

                if (rowsChanged)
                {
                    Reflow();
                }
            }

            var measured = Math.Max(MeasureContentWidth(blocks), viewportWidth);
            if (measured != contentWidth)
            {
                contentWidth = measured;
                formContent.SetContentSize(new Size(Math.Max(contentWidth, 1), contentHeight));
                var maxX = Math.Max(0, contentWidth - viewportWidth);
                if (formContent.Viewport.X > maxX)
                {
                    formContent.Viewport = formContent.Viewport with { X = maxX };
                }
            }
        };

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
                            if (panel.IndicatorLabels.TryGetValue(id, out var label))
                            {
                                label.Text = value;
                            }
                        }

                        foreach (var display in panel.DisplayViews.Values)
                        {
                            display.Apply(values);
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

        void ScrollXBy(int delta)
        {
            var maxX = Math.Max(0, contentWidth - formContent.Viewport.Width);
            var newX = Math.Clamp(formContent.Viewport.X + delta, 0, maxX);
            formContent.Viewport = formContent.Viewport with { X = newX };
        }

        void scrollOnKey(object? _, Key key)
        {
            // Ctrl+PageUp/Ctrl+PageDown scroll sideways by half a screen, for a wide row whose end
            // isn't a focusable control (a long indicator value, a wide chart).
            var halfWidth = Math.Max(formContent.Viewport.Width / 2, 1);
            if (key == Key.PageDown.WithCtrl || key == Key.PageUp.WithCtrl)
            {
                ScrollXBy(key == Key.PageDown.WithCtrl ? halfWidth : -halfWidth);
                key.Handled = true;
                return;
            }

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

        // A surface that binds something into the session's live pipeline for the panel's lifetime
        // (e.g. ZoomH4nControlSurface's wake watcher) unbinds it here, the same way the ValuesChanged
        // subscription above is torn down — every caller already disposes panelParts.Window once its
        // nested app.Run(...) returns (see TuiMode.cs), so this reliably fires unlike the main window's
        // own Disposing (see docs/bugs/020-zoomh4n-wake-watcher-leak.md).
        if (surface is IDisposable disposableSurface)
        {
            window.Disposing += (_, _) => disposableSurface.Dispose();
        }

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
            else if (mouse.Flags.HasFlag(MouseFlags.WheeledRight))
            {
                ScrollXBy(2);
                mouse.Handled = true;
            }
            else if (mouse.Flags.HasFlag(MouseFlags.WheeledLeft))
            {
                ScrollXBy(-2);
                mouse.Handled = true;
            }
        };

        // Scrolls a focused control's content-relative column span into view: its end (the (i)
        // marker included) if it fits, but never so far that its start goes off the left edge.
        panel.RevealColumns = (left, right) =>
        {
            var viewport = formContent.Viewport;
            var newX = viewport.X;
            if (right > viewport.X + viewport.Width)
            {
                newX = Math.Min(left, right - viewport.Width);
            }

            if (left < newX)
            {
                newX = left;
            }

            if (newX != viewport.X)
            {
                ScrollXBy(newX - viewport.X);
            }
        };

        // Scrolls the focused row (a section header, or one control row inside a section) into view.
        panel.Reveal = (top, height) =>
        {
            var viewport = formContent.Viewport;
            if (top < viewport.Y)
            {
                ScrollBy(top - viewport.Y);
            }
            else if (top + height > viewport.Y + viewport.Height)
            {
                ScrollBy(top + height - (viewport.Y + viewport.Height));
            }
        };

        foreach (var header in blocks.Select(b => b.Header).OfType<Button>())
        {
            header.HasFocusChanged += (_, e) =>
            {
                if (e.NewValue)
                {
                    panel.SetPreviewSource(null);
                    panel.Reveal?.Invoke(panel.Tops.GetValueOrDefault(header), 1);
                    panel.RevealColumns?.Invoke(0, 0);
                }
            };
        }

        return new ControlPanelWindowParts
        {
            Window = window,
            ControlViews = panel.ControlViews,
            IndicatorLabels = panel.IndicatorLabels,
            DisplayViews = panel.DisplayViews,
            InfoMarkers = panel.InfoMarkers,
            FormContent = formContent,
            SectionHeaders = blocks.Where(b => b.Header is not null).GroupBy(b => b.Label).ToDictionary(g => g.Key, g => g.First().Header!),
            SectionBodies = blocks.GroupBy(b => b.Label).ToDictionary(g => g.Key, g => g.First().Body),
            PreviewLabel = previewLabel,
            MessageLabel = messageLabel,
        };
    }

    private static string HeaderText(string label, bool expanded) => $"[{(expanded ? '-' : '+')}] {label}";

    private static Button CreateHeader(string label) => new()
    {
        X = 0,
        Text = HeaderText(label, expanded: true),
        NoDecorations = true,
        NoPadding = true,
        ShadowStyle = ShadowStyles.None,

        // Section labels are data, not menu text — never treat an '_' in one as a hotkey marker.
        HotKeySpecifier = (Rune)0xFFFF,
    };

    private static SectionBlock BuildSection(PanelState panel, UiSection section)
    {
        var label = section.Label ?? string.Empty;
        var body = new View
        {
            X = 2,

            // A fixed, generous width rather than Dim.Fill(): a row wider than the window must
            // still lay out at its natural width (the form scrolls sideways to reach it), not be
            // squeezed to the form's current content width.
            Width = _bodyWidth,
            Height = Math.Max(section.Controls.Count, 1),
            CanFocus = true,
        };

        // Every control in the section starts in the same column: the longest "Label:" plus a space.
        // A chart takes several rows; everything else one.
        var columnX = section.Controls.Count == 0 ? 0 : section.Controls.Max(c => c.Label.Length + 1) + 1;
        var y = 0;
        foreach (var control in section.Controls)
        {
            y += AddControlRow(panel, body, y, columnX, control);
        }

        body.Height = Math.Max(y, 1);

        return new SectionBlock
        {
            Label = label,

            // An unlabeled section (e.g. Busylight's lone Apply button) has nothing to name a
            // header with, so it stays always-expanded with no header, the same as before.
            Header = string.IsNullOrWhiteSpace(label) ? null : CreateHeader(label),
            Body = body,
            Rows = y,
        };
    }

    private const int _bodyWidth = 1000;

    /// <summary>The narrowest the Notes ever wrap to, however small the window.</summary>
    internal const int MinimumNotesWidth = 20;

    /// <summary>The Notes' wrap width for a form viewport <paramref name="viewportWidth"/> columns wide (the body's indent and a spare column taken off).</summary>
    internal static int NotesWrapWidth(int viewportWidth) => Math.Max(MinimumNotesWidth, viewportWidth - 3);

    private static SectionBlock BuildNotesSection(IApplication app, string description)
    {
        // A manual word wrap rather than TextFormatter.WordWrap so the section's row count is
        // known for Reflow's absolute positioning. First wrapped to the screen width (before any
        // layout), then re-wrapped to the form's real visible width after every layout pass that
        // changed it - a terminal resize included (see Rewrap below, and BuildWindow).
        var screenWidth = app.Screen.Width > 0 ? app.Screen.Width : 80;
        var lines = WordWrap(description, NotesWrapWidth(screenWidth - 3));
        var body = new View { X = 2, Width = _bodyWidth, Height = lines.Count, CanFocus = false };
        var text = new Label { X = 0, Y = 0, Width = lines.Max(l => l.Length), Height = lines.Count, Text = string.Join('\n', lines) };
        body.Add(text);
        var block = new SectionBlock { Label = NotesSectionLabel, Header = CreateHeader(NotesSectionLabel), Body = body, Rows = lines.Count };
        block.Rewrap = viewportWidth =>
        {
            var wrapped = WordWrap(description, NotesWrapWidth(viewportWidth));
            var joined = string.Join('\n', wrapped);
            if (joined == text.Text)
            {
                return false;
            }

            text.Text = joined;
            text.Width = Math.Max(wrapped.Max(l => l.Length), 1);
            text.Height = wrapped.Count;
            body.Height = wrapped.Count;
            var rowsChanged = block.Rows != wrapped.Count;
            block.Rows = wrapped.Count;
            return rowsChanged;
        };
        return block;
    }

    /// <summary>
    /// The form's widest row as laid out: each shown section's rows (its indent plus its widest
    /// control's right edge) and each header. Views are measured by their laid-out
    /// <see cref="View.Frame"/>, so it covers every widget kind — a long button, a many-option
    /// selector, a chart — without predicting each one's width.
    /// </summary>
    private static int MeasureContentWidth(IEnumerable<SectionBlock> blocks)
    {
        var width = 0;
        foreach (var block in blocks)
        {
            if (block.Header is not null)
            {
                width = Math.Max(width, HeaderText(block.Label, block.Expanded).Length);
            }

            if (block.Expanded)
            {
                foreach (var view in block.Body.SubViews)
                {
                    if (view.Visible)
                    {
                        width = Math.Max(width, block.Body.Frame.X + view.Frame.Right + 1);
                    }
                }
            }
        }

        return width;
    }

    /// <summary>Greedy word wrap to <paramref name="width"/> columns, keeping explicit line breaks; a single word longer than the width is split.</summary>
    internal static List<string> WordWrap(string text, int width)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = new StringBuilder();
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var remaining = word;
                while (remaining.Length > 0)
                {
                    var needed = line.Length == 0 ? remaining.Length : line.Length + 1 + remaining.Length;
                    if (needed <= width)
                    {
                        if (line.Length > 0)
                        {
                            line.Append(' ');
                        }

                        line.Append(remaining);
                        remaining = string.Empty;
                    }
                    else if (line.Length > 0)
                    {
                        lines.Add(line.ToString());
                        line.Clear();
                    }
                    else
                    {
                        lines.Add(remaining[..width]);
                        remaining = remaining[width..];
                    }
                }
            }

            lines.Add(line.ToString());
        }

        return lines;
    }

    /// <summary>
    /// A command button in a one-line row: shadowless, since every row is one line - a button's
    /// shadow is drawn on the line below it, which is the next control's row (it striped every SCPI
    /// "... Reply:" row and covered the reply shown there), or, for a section's last button, was cut
    /// off by the section's edge. Same as the manifest editor's and playback's button rows.
    /// </summary>
    private static Button RowButton(string text, int x, int y) => new()
    {
        X = x,
        Y = y,
        Text = text,
        ShadowStyle = ShadowStyles.None,

        // Button labels come from device data (a profile, a manifest) - an '_' in one isn't a hotkey marker.
        HotKeySpecifier = (Rune)0xFFFF,
    };

    /// <summary>Adds one control's row(s) at <paramref name="row"/>; returns how many rows it took.</summary>
    private static int AddControlRow(PanelState panel, View body, int row, int columnX, UiControl control)
    {
        var app = panel.App;
        var surface = panel.Surface;
        var label = new Label { X = 0, Y = row, Text = control.Label + ":", HotKeySpecifier = (Rune)0xFFFF };
        body.Add(label);

        View? previewAnchor = null;
        string? probeCommandId = null;
        string? probeValue = null;
        Func<string?>? previewSource = null;
        View widget;

        switch (control)
        {
            case ButtonControl { ColorPickerTargetCommandId: { } colorTargetId } button:
                {
                    var colorButtonView = RowButton(control.Label, columnX, row);

                    // A swatch next to the button: the current custom color's hex value on a background of
                    // that color - hidden until one has been set (including in an earlier opening of this
                    // panel, see LastPickedColors). Registered under "{id}.swatch" in ControlViews.
                    var swatchLabel = new Label { X = Pos.Right(colorButtonView) + 1, Y = row, Visible = false };
                    if (LastPickedColors.TryGet(button.Id, out var current))
                    {
                        ShowSwatch(swatchLabel, current);
                    }

                    colorButtonView.Accepting += (_, e) =>
                    {
                        var (lastR, lastG, lastB) = LastPickedColors.Get(button.Id);
                        if (PickColor(app, lastR, lastG, lastB) is { } picked)
                        {
                            LastPickedColors.Set(button.Id, picked);
                            ShowSwatch(swatchLabel, picked);
                            Invoke(app, surface, colorTargetId, $"{picked.R},{picked.G},{picked.B}");
                            SelectCustomColorOption(panel, colorTargetId, button);
                        }

                        e.Handled = true;
                    };
                    body.Add(colorButtonView, swatchLabel);
                    panel.ControlViews[$"{control.Id}.swatch"] = swatchLabel;
                    widget = colorButtonView;
                    previewAnchor = swatchLabel;
                    probeCommandId = colorTargetId;
                    previewSource = () =>
                    {
                        var (r, g, b) = LastPickedColors.Get(button.Id);
                        return panel.SendsText(colorTargetId, $"{r},{g},{b}");
                    };
                    break;
                }

            case ButtonControl { ParameterFieldIds: { } parameterFieldIds } button:
                {
                    var parameterButtonView = RowButton(control.Label, columnX, row);
                    var commandId = button.CommandId ?? button.Id;
                    parameterButtonView.Accepting += (_, e) =>
                    {
                        if (panel.TryReadParameters(parameterFieldIds, reportErrors: true, out var joined))
                        {
                            Invoke(app, surface, commandId, joined);
                        }

                        e.Handled = true;
                    };
                    body.Add(parameterButtonView);
                    widget = parameterButtonView;
                    probeCommandId = commandId;
                    probeValue = panel.RawParameters(parameterFieldIds);
                    previewSource = () => panel.TryReadParameters(parameterFieldIds, reportErrors: false, out var joined)
                        ? panel.SendsText(commandId, joined)
                        : $"Won't send: {panel.LastParameterError}";
                    break;
                }

            case ButtonControl button:
                {
                    var buttonView = RowButton(control.Label, columnX, row);
                    var commandId = button.CommandId ?? button.Id;
                    buttonView.Accepting += (_, e) =>
                    {
                        Invoke(app, surface, commandId, null);
                        e.Handled = true;
                    };
                    body.Add(buttonView);
                    widget = buttonView;
                    probeCommandId = commandId;
                    previewSource = () => panel.SendsText(commandId, null);
                    break;
                }

            case ToggleControl toggle:
                {
                    var checkBox = new CheckBox
                    {
                        X = columnX,
                        Y = row,
                        Value = toggle.DefaultValue ? CheckState.Checked : CheckState.UnChecked,
                    };
                    checkBox.ValueChanged += (_, _) =>
                    {
                        Invoke(app, surface, toggle.Id, checkBox.Value == CheckState.Checked ? "1" : "0");
                        panel.RefreshPreview();
                    };
                    body.Add(checkBox);
                    widget = checkBox;
                    probeCommandId = toggle.Id;
                    probeValue = toggle.DefaultValue ? "0" : "1";

                    // What toggling it would send — the next state, not the current one.
                    previewSource = () => panel.SendsText(toggle.Id, checkBox.Value == CheckState.Checked ? "0" : "1");
                    break;
                }

            case SliderControl or NumericControl:
                {
                    var (minimum, maximum, defaultValue, unit) = control switch
                    {
                        SliderControl s => (s.Minimum, s.Maximum, s.DefaultValue, s.Unit),
                        NumericControl n => (n.Minimum, n.Maximum, n.DefaultValue, n.Unit),
                        _ => (0d, 0d, 0d, (string?)null),
                    };
                    var field = new TextField
                    {
                        X = columnX,
                        Y = row,
                        Width = 10,
                        Text = defaultValue.ToString(CultureInfo.InvariantCulture),
                    };
                    field.Accepting += (_, e) =>
                    {
                        if (panel.TryValidate(control, field.Text, out var value))
                        {
                            field.Text = value;
                            Invoke(app, surface, control.Id, value);
                        }

                        e.Handled = true;
                    };
                    field.TextChanged += (_, _) => panel.RefreshPreview();
                    body.Add(field);
                    var hint = new Label { X = Pos.Right(field) + 1, Y = row, Text = $"[{minimum:0.#}-{maximum:0.#}]{unit}" };
                    body.Add(hint);
                    widget = field;
                    probeCommandId = control.Id;
                    probeValue = field.Text;
                    previewAnchor = hint;
                    previewSource = panel.ValuePreviewSource(control, () => field.Text);
                    break;
                }

            case ChoiceControl choice:
                {
                    var selector = new OptionSelector
                    {
                        X = columnX,
                        Y = row,
                        Orientation = Orientation.Horizontal,
                        HorizontalSpace = 2,
                        Labels = choice.Options,
                    };
                    var defaultIndex = choice.DefaultValue is { } dv ? choice.Options.IndexOf(dv) : -1;
                    selector.Value = defaultIndex >= 0 ? defaultIndex : 0;
                    selector.ValueChanged += (_, _) =>
                    {
                        if (!panel.SuppressChoiceSend && selector.Value is { } index && index >= 0 && index < choice.Options.Count)
                        {
                            var option = choice.Options[index];
                            if (panel.CustomColorLinks.TryGetValue(choice.Id, out var link) && option == link.Option)
                            {
                                // The "Custom" option: re-apply the picked color rather than send the
                                // word "Custom" (the surface only knows presets and "r,g,b").
                                ApplyCustomColor(panel, choice.Id, link.Button);
                            }
                            else
                            {
                                Invoke(app, surface, choice.Id, option);
                            }
                        }

                        panel.RefreshPreview();
                    };
                    body.Add(selector);
                    widget = selector;
                    probeCommandId = choice.Id;
                    probeValue = GetCurrentValue(selector);
                    previewSource = () => panel.SendsText(choice.Id, GetCurrentValue(selector));
                    break;
                }

            case TextFieldControl textField:
                {
                    var textFieldView = new TextField { X = columnX, Y = row, Width = 20, Text = textField.DefaultValue ?? string.Empty };
                    textFieldView.Accepting += (_, e) =>
                    {
                        if (panel.TryValidate(control, textFieldView.Text, out var value))
                        {
                            value = textField.MaxLength is { } max && value.Length > max ? value[..max] : value;
                            textFieldView.Text = value;
                            Invoke(app, surface, textField.Id, value);
                        }

                        e.Handled = true;
                    };
                    textFieldView.TextChanged += (_, _) => panel.RefreshPreview();
                    body.Add(textFieldView);
                    widget = textFieldView;
                    probeCommandId = textField.Id;
                    probeValue = textFieldView.Text;
                    previewSource = panel.ValuePreviewSource(control, () => textFieldView.Text);
                    break;
                }

            case IndicatorControl indicator:
                {
                    var indicatorLabel = new Label { X = columnX, Y = row, Text = indicator.DefaultValue ?? string.Empty };
                    body.Add(indicatorLabel);
                    panel.IndicatorLabels[control.Id] = indicatorLabel;
                    widget = indicatorLabel;
                    break;
                }

            case BarGraphControl or StripChartControl or VectorControl:
                {
                    var state = LiveDisplayState.For(control)!;
                    Func<CellGrid> render = state switch
                    {
                        BarGraphState bars => () => CellCharts.RenderBarGraph(bars),
                        StripChartState strip => () => CellCharts.RenderStripChart(strip),
                        VectorState vector => () => CellCharts.RenderVector(vector),
                        _ => () => new CellGrid(1, 1),
                    };
                    var canvas = new CellCanvasView(state, render) { X = columnX, Y = row };
                    body.Add(canvas);
                    panel.DisplayViews[control.Id] = canvas;
                    panel.ControlViews[control.Id] = canvas;
                    return canvas.Render().Height;
                }

            default:
                return 1;
        }

        panel.ControlViews[control.Id] = widget;

        // Only a control that actually sends something gets its own preview + (i) marker; a
        // value-holder field feeding a parameter button (e.g. a SCPI command's parameter) shows that
        // button's preview instead while it has focus, since that's what editing it changes.
        var sendsSomething = previewSource is not null && probeCommandId is not null && panel.Sends(probeCommandId, probeValue) && !panel.IsValueHolderOnly(control);
        if (sendsSomething)
        {
            panel.PreviewSources[control.Id] = previewSource!;
            var marker = new Label { X = Pos.Right(previewAnchor ?? widget) + 1, Y = row, Text = "(i)" };
            body.Add(marker);
            panel.InfoMarkers[control.Id] = marker;
        }

        var focusSource = sendsSomething ? previewSource : panel.ConsumerPreviewSource(control.Id);
        var rowEnd = sendsSomething ? panel.InfoMarkers[control.Id] : previewAnchor ?? widget;
        widget.HasFocusChanged += (_, e) =>
        {
            if (e.NewValue)
            {
                panel.SetPreviewSource(focusSource);
                panel.Reveal?.Invoke(panel.Tops.GetValueOrDefault(body) + row, 1);

                // A row wider than the window: bring the control and its (i) marker into view.
                panel.RevealColumns?.Invoke(body.Frame.X + widget.Frame.X, body.Frame.X + rowEnd.Frame.Right);
            }
            else
            {
                panel.ClearPreviewSource(focusSource);
            }
        };

        return 1;
    }

    /// <summary>Reads a sibling control's current value for <see cref="ButtonControl.ParameterFieldIds"/> — see the branch above.</summary>
    private static string GetCurrentValue(View view) => view switch
    {
        TextField textField => textField.Text,
        OptionSelector { Value: { } index, Labels: { } labels } when index >= 0 && index < labels.Count => labels[index],
        CheckBox checkBox => checkBox.Value == CheckState.Checked ? "1" : "0",
        Label label => label.Text,
        _ => string.Empty,
    };

    private static double ParseOr(string text, double fallback) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    /// <summary>
    /// The linked "Custom" choice option was selected: send the button's last picked color. If none
    /// has been picked yet, open the picker - a Custom option with nothing behind it would otherwise
    /// silently send white.
    /// </summary>
    private static void ApplyCustomColor(PanelState panel, string choiceId, ButtonControl button)
    {
        if (!LastPickedColors.TryGet(button.Id, out var color))
        {
            if (PickColor(panel.App, 255, 255, 255) is not { } picked)
            {
                return;
            }

            color = picked;
            LastPickedColors.Set(button.Id, color);
        }

        if (panel.ControlViews.TryGetValue($"{button.Id}.swatch", out var swatch) && swatch is Label swatchLabel)
        {
            ShowSwatch(swatchLabel, color);
        }

        Invoke(panel.App, panel.Surface, choiceId, $"{color.R},{color.G},{color.B}");
    }

    /// <summary>After a color pick, select its linked choice option (without sending it again).</summary>
    private static void SelectCustomColorOption(PanelState panel, string choiceId, ButtonControl button)
    {
        if (!panel.CustomColorLinks.TryGetValue(choiceId, out var link) || link.Button != button
            || !panel.ControlViews.TryGetValue(choiceId, out var view) || view is not OptionSelector selector
            || panel.Definition(choiceId) is not ChoiceControl choice)
        {
            return;
        }

        panel.SuppressChoiceSend = true;
        try
        {
            selector.Value = choice.Options.IndexOf(link.Option);
        }
        finally
        {
            panel.SuppressChoiceSend = false;
        }
    }

    private static void ShowSwatch(Label swatch, (byte R, byte G, byte B) color)
    {
        var background = new Terminal.Gui.Drawing.Color(color.R, color.G, color.B, 255);
        var foreground = LastPickedColors.UseDarkText(color)
            ? new Terminal.Gui.Drawing.Color(0, 0, 0, 255)
            : new Terminal.Gui.Drawing.Color(255, 255, 255, 255);
        swatch.Text = $" {LastPickedColors.ToHex(color)} ";
        swatch.SetScheme(new Terminal.Gui.Drawing.Scheme(new Terminal.Gui.Drawing.Attribute(foreground, background)));
        swatch.Visible = true;
    }

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

    /// <summary>One section's header (null for an unlabeled section), its rows, and whether it's expanded — see <c>Reflow</c>.</summary>
    private sealed class SectionBlock
    {
        public required string Label { get; init; }

        public required Button? Header { get; init; }

        public required View Body { get; init; }

        public required int Rows { get; set; }

        public bool Expanded { get; set; } = true;

        /// <summary>Re-wraps the section for a new visible width (the Notes); true when its row count changed, so the form must reflow.</summary>
        public Func<int, bool>? Rewrap { get; set; }
    }

    /// <summary>
    /// Everything the rows of one panel share: the surface and its optional preview capability, the
    /// id → view lookups, and the footer's preview/message lines.
    /// </summary>
    private sealed class PanelState
    {
        private readonly ICommandPreview? _preview;
        private readonly Label _previewLabel;
        private readonly Label _messageLabel;
        private readonly Dictionary<string, UiControl> _controlsById = [];
        private readonly Dictionary<string, string> _consumerByFieldId = [];
        private Func<string?>? _currentPreviewSource;

        public PanelState(IApplication app, IControlSurface surface, UiDefinition definition, Label previewLabel, Label messageLabel)
        {
            App = app;
            Surface = surface;
            _preview = surface as ICommandPreview;
            _previewLabel = previewLabel;
            _messageLabel = messageLabel;
            CustomColorLinks = CustomColorChoices.Find(definition);

            foreach (var control in definition.Sections.SelectMany(s => s.Controls))
            {
                _controlsById.TryAdd(control.Id, control);
                if (control is ButtonControl { ParameterFieldIds: { } fieldIds })
                {
                    foreach (var fieldId in fieldIds)
                    {
                        _consumerByFieldId.TryAdd(fieldId, control.Id);
                    }
                }
            }
        }

        public IApplication App { get; }

        public IControlSurface Surface { get; }

        /// <summary>Choice ids whose option stands for a color button's picked color - see <see cref="CustomColorChoices"/>.</summary>
        public IReadOnlyDictionary<string, (ButtonControl Button, string Option)> CustomColorLinks { get; private set; } = new Dictionary<string, (ButtonControl, string)>();

        /// <summary>Set while a color pick selects its choice option, so that selection isn't sent a second time.</summary>
        public bool SuppressChoiceSend { get; set; }

        /// <summary>The definition's control with <paramref name="id"/>, if any.</summary>
        public UiControl? Definition(string id) => _controlsById.TryGetValue(id, out var control) ? control : null;

        public Dictionary<string, View> ControlViews { get; } = [];

        public Dictionary<string, Label> IndicatorLabels { get; } = [];

        public Dictionary<string, CellCanvasView> DisplayViews { get; } = [];

        /// <summary>Scrolls a content-relative column span into view; set once the form's scrolling is wired.</summary>
        public Action<int, int>? RevealColumns { get; set; }

        public Dictionary<string, Label> InfoMarkers { get; } = [];

        public Dictionary<string, Func<string?>> PreviewSources { get; } = [];

        /// <summary>Scrolls a content-relative row range into view; set once the form's scrolling is wired.</summary>
        public Action<int, int>? Reveal { get; set; }

        /// <summary>Each header's/section body's content row as of the last <c>Reflow</c> — used for scrolling into view instead of <c>Frame.Y</c>, which lags until the next layout pass (e.g. right after a collapse).</summary>
        public Dictionary<View, int> Tops { get; } = [];

        public string? LastParameterError { get; private set; }

        /// <summary>Whether invoking <paramref name="commandId"/> would send anything at all — decides which controls get a preview and an (i) marker.</summary>
        public bool Sends(string commandId, string? value) => _preview?.PreviewCommand(commandId, value) is not null;

        /// <summary>The named parameter fields' current values, comma-joined, unvalidated — only for probing <see cref="Sends"/>.</summary>
        public string RawParameters(IReadOnlyList<string> fieldIds) =>
            string.Join(',', fieldIds.Select(id => ControlViews.TryGetValue(id, out var view) ? GetCurrentValue(view) : string.Empty));

        /// <summary>The footer text for invoking <paramref name="commandId"/> with <paramref name="value"/>, or null when the surface has no preview for it.</summary>
        public string? SendsText(string commandId, string? value) =>
            _preview?.PreviewCommand(commandId, value) is { } preview ? $"Sends: {preview}" : null;

        /// <summary>A value field's preview: its current text validated first, so the footer shows what would actually be sent (or why nothing would be).</summary>
        public Func<string?> ValuePreviewSource(UiControl control, Func<string> currentText) => () =>
        {
            var result = ValueValidator.Validate(ValueValidator.ConstraintFor(control), currentText());
            return result.IsValid ? SendsText(control.Id, result.Value) : $"Won't send: {result.Error}";
        };

        /// <summary>A field some parameter button reads from shows that button's preview while focused.</summary>
        public Func<string?>? ConsumerPreviewSource(string fieldId) =>
            _consumerByFieldId.TryGetValue(fieldId, out var buttonId)
                ? () => PreviewSources.TryGetValue(buttonId, out var source) ? source() : null
                : null;

        /// <summary>True for a field only ever read by a parameter button — committing it on its own sends nothing worth previewing.</summary>
        public bool IsValueHolderOnly(UiControl control) =>
            control is not ButtonControl && _consumerByFieldId.ContainsKey(control.Id);

        public void SetPreviewSource(Func<string?>? source)
        {
            _currentPreviewSource = source;
            RefreshPreview();
        }

        public void ClearPreviewSource(Func<string?>? source)
        {
            if (ReferenceEquals(_currentPreviewSource, source))
            {
                SetPreviewSource(null);
            }
        }

        public void RefreshPreview() => _previewLabel.Text = _currentPreviewSource?.Invoke() ?? string.Empty;

        public void ShowMessage(string? message) => _messageLabel.Text = message ?? string.Empty;

        /// <summary>Runs the shared <see cref="ValueValidator"/> on a committed value; an invalid one is reported in the footer and must not be sent.</summary>
        public bool TryValidate(UiControl control, string input, out string value)
        {
            var result = ValueValidator.Validate(ValueValidator.ConstraintFor(control), input);
            value = result.Value;
            if (!result.IsValid)
            {
                ShowMessage($"{control.Label}: {result.Error} Not sent.");
                return false;
            }

            ShowMessage(null);
            return true;
        }

        /// <summary>Reads and validates every named parameter field's current value, comma-joined; on the first invalid one, fails (reporting it in the footer when <paramref name="reportErrors"/>).</summary>
        public bool TryReadParameters(IReadOnlyList<string> fieldIds, bool reportErrors, out string joined)
        {
            var values = new List<string>(fieldIds.Count);
            foreach (var fieldId in fieldIds)
            {
                var raw = ControlViews.TryGetValue(fieldId, out var view) ? GetCurrentValue(view) : string.Empty;
                var constraint = _controlsById.TryGetValue(fieldId, out var fieldControl) ? ValueValidator.ConstraintFor(fieldControl) : null;
                var result = ValueValidator.Validate(constraint, raw);
                if (!result.IsValid)
                {
                    LastParameterError = $"{fieldControl?.Label ?? fieldId}: {result.Error}";
                    if (reportErrors)
                    {
                        ShowMessage($"{LastParameterError} Not sent.");
                    }

                    joined = string.Empty;
                    return false;
                }

                values.Add(result.Value);
            }

            if (reportErrors)
            {
                ShowMessage(null);
            }

            joined = string.Join(',', values);
            return true;
        }
    }

    /// <summary>
    /// A small nested modal RGB/HSV color picker, opened by any <c>ButtonControl</c> with
    /// <c>ColorPickerTargetCommandId</c> set (see <see cref="AddControlRow"/>) — the TUI half of the
    /// same generic color-picker support as <c>DevTerm.Wpf.ColorPickerWindow</c>. No slider/hex
    /// widget exists in the installed Terminal.Gui package (see this class's own remarks on that),
    /// so every field is a bounded <see cref="TextField"/>, synced on Enter.
    /// </summary>
    internal static (byte R, byte G, byte B)? PickColor(IApplication app, byte initialR, byte initialG, byte initialB)
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

    /// <summary>The bar graph / strip chart / vector displays, keyed by <c>UiControl.Id</c> — each with its live state and its rendered cells.</summary>
    public required IReadOnlyDictionary<string, CellCanvasView> DisplayViews { get; init; }

    /// <summary>The scrolling form holding every section — its <c>Viewport</c> is the visible part of the content (scrolled vertically and horizontally).</summary>
    public required View FormContent { get; init; }

    /// <summary>The <c>(i)</c> marker next to each control that sends a previewable command, keyed by <c>UiControl.Id</c>.</summary>
    public required IReadOnlyDictionary<string, Label> InfoMarkers { get; init; }

    /// <summary>Each labeled section's <c>[-]</c>/<c>[+]</c> expand/collapse header, keyed by section label (<see cref="ControlPanelMode.NotesSectionLabel"/> for the notes).</summary>
    public required IReadOnlyDictionary<string, Button> SectionHeaders { get; init; }

    /// <summary>Each section's container of rows, keyed by section label (an unlabeled section under the empty string).</summary>
    public required IReadOnlyDictionary<string, View> SectionBodies { get; init; }

    /// <summary>The footer line showing what the focused control would send (<c>Sends: ...</c>).</summary>
    public required Label PreviewLabel { get; init; }

    /// <summary>The footer line reporting a rejected (invalid, not sent) value.</summary>
    public required Label MessageLabel { get; init; }
}
