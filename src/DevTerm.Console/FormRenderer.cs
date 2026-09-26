using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using DevTerm.UiDefinitions;
using DevTerm.UiDefinitions.Forms;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace DevTerm.Console;

/// <summary>
/// The TUI's generic <em>form</em> renderer: walks a <see cref="UiDefinition"/> (typically one
/// <see cref="FormDefinitionGenerator"/> made from an annotated model) and builds real Terminal.Gui
/// controls two-way bound to the model through a <see cref="FormBinding"/> — the settings-form sibling
/// of <see cref="ControlPanelMode"/>, which renders the same vocabulary as a <em>command</em> panel
/// (a control there sends on commit; a field here writes a property). Returns an embeddable
/// <see cref="View"/> rather than a window, so a host (the Connection Editor, the manifest editor)
/// places it among its own hand-built parts. See docs/design/ui-definitions.md's "Forms from one
/// definition".
/// </summary>
/// <remarks>
/// <para>
/// Layout: a labeled section starts with a <c>── Label ──</c> header line; within a section every
/// widget starts in one column (the section's longest label plus padding), a toggle's or button's
/// text standing in for its label. A section or row whose <see cref="UiCondition"/> doesn't hold is hidden and everything
/// below moves up (absolute rows, recomputed on every model change) — the hand-built editor this
/// replaced left a gap where a hidden transport's fields had been.
/// </para>
/// <para>
/// Widgets, with the installed Terminal.Gui v2.5.0 having no combo box: a choice that fits on one
/// line is an <see cref="OptionSelector"/>; one too wide for the terminal wraps as radio-style check
/// boxes over up to three lines; a longer list is a text field plus a <c>Pick...</c> button that opens
/// a list (the "type it or pick it" pattern the editor already used for its SCPI profile). A
/// <see cref="ChoiceStyle.CheckList"/> is a row of check boxes, wrapped the same way. A host can
/// replace any control's widget (<see cref="TuiFormOptions.CustomWidgets"/>) — the editor's
/// detected-device pickers — while the form still owns its label, alignment and visibility.
/// </para>
/// </remarks>
internal static class FormRenderer
{
    internal const int MaxWrappedChoiceRows = 3;

    /// <summary>How far a section's rows sit in from its header.</summary>
    internal const int Indent = 2;

    /// <summary>The Terminal.Gui scheme warning text is drawn with - the theme's <c>Error</c> role (see <see cref="TuiTheme"/>), so a theme switch recolors it.</summary>
    internal const string ErrorSchemeName = "Error";

    public static TuiFormParts Build(IApplication app, UiDefinition definition, FormBinding binding, TuiFormOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(binding);
        options ??= new TuiFormOptions();

        var available = options.AvailableWidth ?? Math.Max((app.Screen.Width > 0 ? app.Screen.Width : 80) - 4, 40);
        var parts = new TuiFormParts(binding)
        {
            Root = new View { X = 0, Y = 0, Width = Dim.Fill(), Height = 1, CanFocus = true },
        };

        foreach (var section in definition.Sections)
        {
            // Every widget in a section starts in the same column: its longest "Label:" plus a
            // space (per section, like the control panels, so one long label elsewhere doesn't
            // squeeze every other section's widgets to the right).
            var labelWidth = section.Controls.Where(HasRowLabel).Select(c => c.Label.Length + 1).DefaultIfEmpty(0).Max();
            var column = Indent + (labelWidth > 0 ? labelWidth + 1 : 0);
            var block = new SectionRows(section);
            if (!string.IsNullOrWhiteSpace(section.Label))
            {
                var header = new Label { X = 0, Text = $"── {section.Label} ──" };
                parts.Root.Add(header);
                parts.SectionHeaderLabels[section.Label] = header;
                block.Header = header;
            }

            foreach (var control in section.Controls)
            {
                var row = BuildRow(app, parts, control, Indent, column, available, options);
                row.Label = parts.RowLabels.GetValueOrDefault(control.Id);
                row.CaptureColumnOffsets(column);
                foreach (var item in row.Items)
                {
                    parts.Root.Add(item.View);
                }

                block.Rows.Add(row);
            }

            parts.Sections.Add(block);
        }

        parts.RefreshAll();
        binding.Changed += (_, name) => parts.OnModelChanged(name);
        return parts;
    }

    /// <summary>
    /// A small modal "pick one" list — the stand-in for the combo box Terminal.Gui doesn't have.
    /// Returns the picked <em>index</em>, not the text: two entries can show identical text (three
    /// attached K8055 boards sharing a VID/PID with no serial), and indexing the caller's own list is
    /// unambiguous where a text match isn't.
    /// </summary>
    public static int? PickFromList(IApplication app, string title, IReadOnlyList<string> items, string emptyMessage = "Nothing was detected.")
    {
        if (items.Count == 0)
        {
            MessageBox.Query(app, "dev-term", emptyMessage, ["OK"]);
            return null;
        }

        int? picked = null;
        var dialog = new Dialog { Title = title, Width = ListDialogWidth(app, items), Height = Math.Min(items.Count + 5, 20) };
        var listView = new ListView { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(2) };
        listView.SetSource(new ObservableCollection<string>(items));

        void Choose()
        {
            if (listView.SelectedItem is int index && index >= 0 && index < items.Count)
            {
                picked = index;
            }

            app.RequestStop();
        }

        listView.Accepting += (_, e) =>
        {
            e.Handled = true;
            Choose();
        };
        var selectButton = new Button { X = 0, Y = Pos.Bottom(listView), Text = "Select", IsDefault = true };
        selectButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            Choose();
        };
        var cancelButton = new Button { X = Pos.Right(selectButton) + 1, Y = Pos.Top(selectButton), Text = "Cancel" };
        cancelButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            app.RequestStop();
        };
        dialog.Add(listView, selectButton, cancelButton);
        app.Run(dialog);
        return picked;
    }

    /// <summary>
    /// A list picker's width: wide enough for its longest item (the SCPI profile names ran past a
    /// fixed 60 columns and were cut off) but never wider than the screen, and at least 60. Its list
    /// leaves the last two rows to the Select/Cancel row and its shadow, which a one-row gap cut off.
    /// </summary>
    internal static int ListDialogWidth(IApplication app, IReadOnlyList<string> items)
    {
        var screen = app.Screen.Width > 0 ? app.Screen.Width : 80;
        var widest = items.Count == 0 ? 0 : items.Max(i => i.Length);
        return Math.Clamp(widest + 4, 60, Math.Max(screen - 4, 60));
    }

    /// <summary>Greedy placement of items <paramref name="widths"/> wide, <paramref name="gap"/> apart, into lines of at most <paramref name="width"/> columns: each item's (column offset, line).</summary>
    internal static List<(int X, int Line)> Wrap(IReadOnlyList<int> widths, int width, int gap)
    {
        var placed = new List<(int X, int Line)>(widths.Count);
        var x = 0;
        var line = 0;
        foreach (var w in widths)
        {
            if (x > 0 && x + w > width)
            {
                x = 0;
                line++;
            }

            placed.Add((x, line));
            x += w + gap;
        }

        return placed;
    }

    private static bool HasRowLabel(UiControl control) =>
        control is not ToggleControl and not ButtonControl && !string.IsNullOrEmpty(control.Label);

    private static FormRow BuildRow(IApplication app, TuiFormParts parts, UiControl control, int indent, int column, int available, TuiFormOptions options)
    {
        var binding = parts.Binding;
        var row = new FormRow(control);
        if (HasRowLabel(control))
        {
            var label = new Label { X = indent, Text = control.Label + ":" };
            row.Add(label);
            parts.RowLabels[control.Id] = label;
        }

        var width = Math.Max(available - column, 10);
        if (options.CustomWidgets.TryGetValue(control.Id, out var custom))
        {
            var widget = custom(control);
            widget.View.X = column;
            row.Add(widget.View);
            row.Height = Math.Max(widget.Rows, 1);
            parts.ControlViews[control.Id] = widget.View;
            return row;
        }

        switch (control)
        {
            case ToggleControl:
                {
                    var checkBox = new CheckBox { X = column, Text = control.Label };
                    checkBox.ValueChanged += (_, _) => parts.Push(() => binding.SetBool(control.Id, checkBox.Value == CheckState.Checked));
                    row.Add(checkBox);
                    row.Refresh = () => checkBox.Value = binding.GetBool(control.Id) ? CheckState.Checked : CheckState.UnChecked;
                    parts.ControlViews[control.Id] = checkBox;
                    break;
                }

            case ChoiceControl { Style: ChoiceStyle.CheckList } choice:
                {
                    var boxes = choice.Options.Select(option => new CheckBox { Text = option }).ToList();
                    var placed = Wrap([.. choice.Options.Select(o => o.Length + 2)], width, 1);
                    for (var i = 0; i < boxes.Count; i++)
                    {
                        boxes[i].X = column + placed[i].X;
                        row.Add(boxes[i], placed[i].Line);
                        boxes[i].ValueChanged += (_, _) => parts.Push(() =>
                            binding.SetSelection(control.Id, choice.Options.Where((_, index) => boxes[index].Value == CheckState.Checked)));
                    }

                    row.Height = placed.Count == 0 ? 1 : placed[^1].Line + 1;
                    row.Refresh = () =>
                    {
                        var selected = binding.GetSelection(control.Id);
                        for (var i = 0; i < boxes.Count; i++)
                        {
                            boxes[i].Value = selected.Contains(choice.Options[i], StringComparer.OrdinalIgnoreCase) ? CheckState.Checked : CheckState.UnChecked;
                        }
                    };
                    parts.CheckLists[control.Id] = boxes;
                    parts.ControlViews[control.Id] = boxes.Count > 0 ? boxes[0] : new View();
                    break;
                }

            case ChoiceControl choice:
                BuildChoice(app, parts, row, choice, column, width);
                break;

            case IndicatorControl indicator:
                {
                    var label = new Label { X = column, Text = indicator.DefaultValue ?? string.Empty };
                    if (indicator.Style == IndicatorStyle.Warning)
                    {
                        // The theme's Error color, like WPF's form renderer and the inline "! ..."
                        // messages below - a "(not found ...)" hint used to look like plain help text.
                        label.SchemeName = ErrorSchemeName;
                    }

                    row.Add(label);
                    row.Refresh = () =>
                    {
                        var text = binding.Has(control.Id) ? binding.GetText(control.Id) : indicator.DefaultValue ?? string.Empty;
                        var lines = ControlPanelMode.WordWrap(text, width);
                        label.Text = string.Join('\n', lines);
                        label.Width = Math.Max(lines.Max(l => l.Length), 1);
                        label.Height = lines.Count;
                        row.Height = lines.Count;
                    };
                    parts.ControlViews[control.Id] = label;
                    break;
                }

            case ButtonControl button:
                {
                    var view = new Button { X = column, Text = control.Label };
                    view.Accepting += (_, e) =>
                    {
                        e.Handled = true;
                        if (options.Actions.TryGetValue(control.Id, out var action))
                        {
                            action();
                        }
                        else
                        {
                            binding.Invoke(button.CommandId ?? button.Id);
                        }
                    };
                    row.Add(view);
                    row.Height = 2; // the button's shadow gets its own line
                    parts.ControlViews[control.Id] = view;
                    break;
                }

            default:
                BuildTextField(parts, row, control, column, width);
                break;
        }

        return row;
    }

    private static void BuildTextField(TuiFormParts parts, FormRow row, UiControl control, int column, int width)
    {
        var binding = parts.Binding;
        var constraint = ValueValidator.ConstraintFor(control);
        var numeric = constraint is { Kind: not ValueKind.Text };
        var fieldWidth = numeric ? 10 : control is TextFieldControl { MaxLength: { } max } ? Math.Clamp(max + 1, 4, 40) : 30;

        // Never wider than the room left in the row: in a narrow form (the manifest editor's pane
        // at 80 columns) a 30-wide field ran past the pane's edge and was cut off.
        fieldWidth = Math.Min(fieldWidth, Math.Max(width, 4));
        var field = new TextField { X = column, Width = fieldWidth };
        row.Add(field);
        var next = column + fieldWidth + 1;

        if (control is NumericControl or SliderControl && RangeHint(control) is { } hintText)
        {
            var hint = new Label { X = next, Text = hintText };
            row.Add(hint);
            next += hintText.Length + 1;
        }

        // Always "shown" with the row; empty (so invisible) while the value is valid.
        var error = new Label { X = next, Text = string.Empty, SchemeName = ErrorSchemeName };
        row.Add(error);
        parts.ErrorLabels[control.Id] = error;

        void ShowResult(ValueValidationResult result) => error.Text = result.IsValid ? string.Empty : "! " + result.Error;

        field.TextChanged += (_, _) => parts.Push(() => ShowResult(binding.SetText(control, field.Text)));
        row.Refresh = () =>
        {
            var text = binding.GetText(control.Id);
            if (field.Text != text)
            {
                field.Text = text;
            }

            ShowResult(binding.Validate(control, text));
        };
        field.ReadOnly = binding.IsReadOnly(control.Id);
        parts.ControlViews[control.Id] = field;
    }

    private static string? RangeHint(UiControl control)
    {
        var (min, max, unit) = control switch
        {
            NumericControl n => (n.Minimum, n.Maximum, n.Unit),
            SliderControl s => (s.Minimum, s.Maximum, s.Unit),
            _ => (0d, 0d, null),
        };

        return min <= double.MinValue / 2 || max >= double.MaxValue / 2
            ? unit
            : string.Create(CultureInfo.InvariantCulture, $"[{min:0.#}-{max:0.#}]{unit}");
    }

    private static void BuildChoice(IApplication app, TuiFormParts parts, FormRow row, ChoiceControl choice, int column, int width)
    {
        var binding = parts.Binding;
        var options = choice.Options;
        var labels = options.Select(ChoiceLabel).ToList();
        var selectorWidth = labels.Sum(o => o.Length + 2) + (2 * Math.Max(options.Count - 1, 0));

        if (selectorWidth <= width)
        {
            var selector = new OptionSelector
            {
                X = column,
                Orientation = Orientation.Horizontal,
                HorizontalSpace = 2,
                Labels = labels,

                // Options are data, not menu text — an '_' in one isn't a hotkey marker.
                HotKeySpecifier = (Rune)0xFFFF,
            };
            selector.ValueChanged += (_, _) =>
            {
                if (selector.Value is int index && index >= 0 && index < options.Count)
                {
                    parts.Push(() => binding.SetText(choice, options[index]));
                }
            };
            row.Add(selector);
            row.Refresh = () =>
            {
                var index = IndexOf(options, binding.GetText(choice.Id));
                if (selector.Value != (index >= 0 ? index : null))
                {
                    selector.Value = index >= 0 ? index : null;
                }
            };
            parts.ControlViews[choice.Id] = selector;
            parts.Choices[choice.Id] = new TuiChoice(options, selector, () => selector.Value is int i && i >= 0 && i < options.Count ? options[i] : null, value =>
            {
                var index = IndexOf(options, value);
                selector.Value = index >= 0 ? index : null;
            });
            return;
        }

        var placed = Wrap([.. labels.Select(o => o.Length + 2)], width, 2);
        if (placed.Count > 0 && placed[^1].Line < MaxWrappedChoiceRows)
        {
            var radios = labels.Select(label => new CheckBox { Text = label, RadioStyle = true, HotKeySpecifier = (Rune)0xFFFF }).ToList();
            for (var i = 0; i < radios.Count; i++)
            {
                var radio = radios[i];
                var option = options[i];
                radio.X = column + placed[i].X;
                row.Add(radio, placed[i].Line);
                radio.ValueChanged += (_, _) =>
                {
                    if (parts.IsRefreshing)
                    {
                        return;
                    }

                    if (radio.Value == CheckState.Checked)
                    {
                        parts.Push(() => binding.SetText(choice, option));
                    }

                    // Keep exactly the model's value checked (a radio can't be unchecked by itself).
                    parts.RefreshRow(row);
                };
            }

            row.Height = placed[^1].Line + 1;
            row.Refresh = () =>
            {
                var current = IndexOf(options, binding.GetText(choice.Id));
                for (var i = 0; i < radios.Count; i++)
                {
                    var check = i == current ? CheckState.Checked : CheckState.UnChecked;
                    if (radios[i].Value != check)
                    {
                        radios[i].Value = check;
                    }
                }
            };
            parts.CheckLists[choice.Id] = radios;
            parts.ControlViews[choice.Id] = radios[0];
            parts.Choices[choice.Id] = new TuiChoice(
                options,
                radios[0],
                () => radios.FindIndex(r => r.Value == CheckState.Checked) is var i and >= 0 ? options[i] : null,
                value =>
                {
                    if (IndexOf(options, value) is var i and >= 0)
                    {
                        radios[i].Value = CheckState.Checked;
                    }
                });
            return;
        }

        // Too many to show at once: type it, or pick it from the list.
        // Room for the "⟦ Pick... ⟧" button and its shadow (12 columns) plus a gap after the field.
        var field = new TextField { X = column, Width = Math.Min(30, Math.Max(width - 13, 10)) };
        var pick = new Button { X = Pos.Right(field) + 1, Text = "Pick..." };
        field.TextChanged += (_, _) => parts.Push(() => binding.SetText(choice, field.Text));
        pick.Accepting += (_, e) =>
        {
            e.Handled = true;
            if (PickFromList(app, choice.Label, options) is int index)
            {
                field.Text = options[index];
            }
        };
        row.Add(field);
        row.Add(pick);
        row.Height = 2;
        row.Refresh = () =>
        {
            var text = binding.GetText(choice.Id);
            if (field.Text != text)
            {
                field.Text = text;
            }
        };
        parts.ControlViews[choice.Id] = field;
        parts.PickButtons[choice.Id] = pick;
        parts.Choices[choice.Id] = new TuiChoice(options, field, () => field.Text, value => field.Text = value ?? string.Empty);
    }

    /// <summary>How a choice option is shown: an empty option (the "not set" choice, e.g. a manifest's transport hint) as "(none)" rather than a bare radio button with nothing beside it.</summary>
    internal static string ChoiceLabel(string option) => option.Length == 0 ? "(none)" : option;

    private static int IndexOf(IReadOnlyList<string> options, string? value)
    {
        for (var i = 0; i < options.Count; i++)
        {
            if (string.Equals(options[i], value, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>What a host adds to a <see cref="FormRenderer"/> form: its own widget for a control, or a button's action.</summary>
internal sealed class TuiFormOptions
{
    /// <summary>Replaces a control's widget (the form keeps its label, column and visibility); keyed by <see cref="UiControl.Id"/>.</summary>
    public Dictionary<string, Func<UiControl, TuiCustomWidget>> CustomWidgets { get; } = new(StringComparer.Ordinal);

    /// <summary>What a <see cref="ButtonControl"/> does, keyed by its id; without one, the bound model's command property of that name runs.</summary>
    public Dictionary<string, Action> Actions { get; } = new(StringComparer.Ordinal);

    /// <summary>The columns the form may use (for wrapping choices and indicator text); the screen width less the window's frame by default.</summary>
    public int? AvailableWidth { get; set; }
}

/// <summary>A host-built widget and how many lines it takes.</summary>
internal sealed record TuiCustomWidget(View View, int Rows);

/// <summary>A single-choice control, whichever widget it rendered as — read or set its value by option text (setting it drives the real widget, whose own change event updates the model).</summary>
internal sealed class TuiChoice(IReadOnlyList<string> options, View widget, Func<string?> get, Action<string?> set)
{
    public IReadOnlyList<string> Options { get; } = options;

    /// <summary>The <see cref="OptionSelector"/>, the first radio check box, or the pick text field.</summary>
    public View Widget { get; } = widget;

    public string? Value
    {
        get => get();
        set => set(value);
    }
}

/// <summary>One rendered form: its root view plus the lookups a host or test needs, keyed by <see cref="UiControl.Id"/>.</summary>
internal sealed class TuiFormParts
{
    private int _refreshing;

    internal TuiFormParts(FormBinding binding)
    {
        Binding = binding;
    }

    public required View Root { get; init; }

    public FormBinding Binding { get; }

    /// <summary>Each control's main widget (text field, check box, option selector, label, button, or the host's own).</summary>
    public Dictionary<string, View> ControlViews { get; } = new(StringComparer.Ordinal);

    /// <summary>Single-choice controls by id, whatever widget each became.</summary>
    public Dictionary<string, TuiChoice> Choices { get; } = new(StringComparer.Ordinal);

    /// <summary>A check list's check boxes, or a wrapped single choice's radio check boxes, in option order.</summary>
    public Dictionary<string, IReadOnlyList<CheckBox>> CheckLists { get; } = new(StringComparer.Ordinal);

    /// <summary>The <c>Pick...</c> button beside a long choice's text field.</summary>
    public Dictionary<string, Button> PickButtons { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, Label> RowLabels { get; } = new(StringComparer.Ordinal);

    /// <summary>The inline <c>! message</c> after a text field whose value fails its constraint.</summary>
    public Dictionary<string, Label> ErrorLabels { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, Label> SectionHeaderLabels { get; } = new(StringComparer.Ordinal);

    /// <summary>The form's height as last laid out.</summary>
    public int Rows { get; private set; }

    /// <summary>A widget in the form took focus: its row's content-relative (top, height) — for a scrolling host to reveal it.</summary>
    public event Action<int, int>? RowFocused;

    /// <summary>The form's layout changed (a section or row was shown or hidden, a wrapped text changed height).</summary>
    public event EventHandler? Reflowed;

    internal List<SectionRows> Sections { get; } = [];

    internal bool IsRefreshing => _refreshing > 0;

    /// <summary>Whether <paramref name="id"/>'s row is currently shown (its section's and its own condition both hold).</summary>
    public bool IsRowVisible(string id) =>
        Sections.Any(s => Binding.IsVisible(s.Section.VisibleWhen) && s.Rows.Any(r => r.Control.Id == id && Binding.IsVisible(r.Control.VisibleWhen)));

    /// <summary>Re-reads every widget from the model and re-lays out the form.</summary>
    public void RefreshAll()
    {
        foreach (var row in Sections.SelectMany(s => s.Rows))
        {
            RefreshRow(row);
        }

        Reflow();
    }

    internal void OnModelChanged(string? name)
    {
        foreach (var row in Sections.SelectMany(s => s.Rows))
        {
            if (name is null || row.Control.Id == name || row.Control is IndicatorControl)
            {
                RefreshRow(row);
            }
        }

        Reflow();
    }

    /// <summary>Runs a widget → model write, unless the widget is only changing because the model is being read into it.</summary>
    internal void Push(Action write)
    {
        if (_refreshing == 0)
        {
            write();
        }
    }

    internal void RefreshRow(FormRow row)
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

    private void Reflow()
    {
        var y = 0;
        var any = false;
        foreach (var section in Sections)
        {
            var sectionVisible = Binding.IsVisible(section.Section.VisibleWhen);
            var shownRows = section.Rows.Where(r => sectionVisible && Binding.IsVisible(r.Control.VisibleWhen)).ToList();
            var shown = sectionVisible && shownRows.Count > 0;
            if (section.Header is { } header)
            {
                header.Visible = shown;
            }

            if (!shown)
            {
                foreach (var row in section.Rows)
                {
                    row.SetVisible(false);
                }

                continue;
            }

            if (any)
            {
                y++; // a blank line between sections
            }

            any = true;
            if (section.Header is { } shownHeader)
            {
                shownHeader.Y = y;
                y++;
            }

            // The widget column follows the longest label among the rows actually shown, so a hidden
            // row's long label (e.g. "Detected USBTMC devices" while HID is selected) doesn't push the rest right.
            var labelWidth = shownRows.Where(r => r.Label is not null).Select(r => r.Control.Label.Length + 1).DefaultIfEmpty(0).Max();
            var column = FormRenderer.Indent + (labelWidth > 0 ? labelWidth + 1 : 0);
            foreach (var row in section.Rows)
            {
                var visible = shownRows.Contains(row);
                row.SetVisible(visible);
                if (visible)
                {
                    row.Top = y;
                    row.Place(y);
                    row.MoveToColumn(column);
                    y += row.Height;
                }
            }
        }

        var height = Math.Max(y, 1);
        var changed = height != Rows;
        Rows = height;
        Root.Height = height;
        if (changed)
        {
            Reflowed?.Invoke(this, EventArgs.Empty);
        }

        foreach (var row in Sections.SelectMany(s => s.Rows).Where(r => !r.FocusWired))
        {
            row.FocusWired = true;
            foreach (var item in row.Items)
            {
                item.View.HasFocusChanged += (_, e) =>
                {
                    if (e.NewValue)
                    {
                        RowFocused?.Invoke(row.Top, row.Height);
                    }
                };
            }
        }
    }
}

/// <summary>One section's header (null when unlabeled) and rows.</summary>
internal sealed class SectionRows(UiSection section)
{
    public UiSection Section { get; } = section;

    public Label? Header { get; set; }

    public List<FormRow> Rows { get; } = [];
}

/// <summary>One control's views (each at a line offset within the row), its height, and how to re-read it from the model.</summary>
internal sealed class FormRow(UiControl control)
{
    private readonly Dictionary<View, int> _columnOffsets = [];

    public UiControl Control { get; } = control;

    public List<(View View, int Line, Func<bool>? ShowWhen)> Items { get; } = [];

    /// <summary>The row's "Label:" (null for a toggle, a button, or an empty label) — it stays at the indent; everything else follows the section's widget column.</summary>
    public Label? Label { get; set; }

    /// <summary>Remembers where each widget sits relative to <paramref name="column"/>, the column it was built at, so <see cref="MoveToColumn"/> can move it.</summary>
    public void CaptureColumnOffsets(int column)
    {
        foreach (var (view, _, _) in Items)
        {
            if (view != Label && view.X is PosAbsolute absolute)
            {
                _columnOffsets[view] = absolute.Position - column;
            }
        }
    }

    /// <summary>Puts the row's widgets at <paramref name="column"/> (views positioned relative to another view follow it).</summary>
    public void MoveToColumn(int column)
    {
        foreach (var (view, offset) in _columnOffsets)
        {
            view.X = column + offset;
        }
    }

    public int Height { get; set; } = 1;

    public int Top { get; set; }

    public Action? Refresh { get; set; }

    public bool FocusWired { get; set; }

    public void Add(View view, int line = 0, Func<bool>? showWhen = null) => Items.Add((view, line, showWhen));

    public void SetVisible(bool visible)
    {
        foreach (var (view, _, showWhen) in Items)
        {
            view.Visible = visible && (showWhen?.Invoke() ?? true);
        }
    }

    public void Place(int top)
    {
        foreach (var (view, line, _) in Items)
        {
            view.Y = top + line;
        }
    }
}
