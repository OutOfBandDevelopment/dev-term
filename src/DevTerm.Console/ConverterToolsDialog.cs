using System.Collections.ObjectModel;
using System.Globalization;
using DevTerm.Configuration;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace DevTerm.Console;

/// <summary>
/// The Terminal.Gui "Converter tools" dialog: add, edit, remove and reorder the Stream Monitor's registered converter
/// tools over a <see cref="ConverterToolsEditor"/>. The WPF equivalent is <c>DevTerm.Wpf.ConverterToolsWindow</c>.
/// See docs/specs/converter-tools-editor.md.
/// </summary>
internal static class ConverterToolsDialog
{
    /// <summary>The dialog's parts, so a test can read and drive them without a real key injector.</summary>
    internal sealed class Parts
    {
        public required Dialog Dialog { get; init; }

        public required ListView List { get; init; }

        public required TextField Name { get; init; }

        public required TextField Path { get; init; }

        public required TextField Arguments { get; init; }

        public required TextField Formats { get; init; }

        public required TextField Extension { get; init; }

        public required TextField Dpi { get; init; }

        public required Label Error { get; init; }

        public required Button Add { get; init; }

        public required Button Remove { get; init; }

        public required Button Up { get; init; }

        public required Button Down { get; init; }

        public required Button Ok { get; init; }

        public required Button Cancel { get; init; }

        /// <summary>Set by OK once the list validated.</summary>
        public IReadOnlyList<StreamConvertToolOptions>? Accepted { get; internal set; }
    }

    /// <summary>Opens the dialog over <paramref name="tools"/>; returns the edited list, or null when cancelled.</summary>
    public static IReadOnlyList<StreamConvertToolOptions>? Show(IApplication app, IEnumerable<StreamConvertToolOptions> tools)
    {
        ArgumentNullException.ThrowIfNull(app);
        var parts = Build(app, new ConverterToolsEditor(tools));
        app.Run(parts.Dialog);
        return parts.Accepted;
    }

    internal static Parts Build(IApplication app, ConverterToolsEditor editor)
    {
        var screenWidth = app.Screen.Width > 0 ? app.Screen.Width : 80;
        var screenHeight = app.Screen.Height > 0 ? app.Screen.Height : 25;
        var dialog = new Dialog { Title = "Converter tools", Width = Math.Min(screenWidth - 2, 80), Height = Math.Min(screenHeight - 1, 20) };
        const int left = 22;
        var labelSpecifier = (System.Text.Rune)0xFFFF;

        var list = new ListView { X = 0, Y = 0, Width = left, Height = Dim.Fill(5) };

        TextField Field(int row, string label, out Label labelView)
        {
            labelView = new Label { X = left + 1, Y = row * 2, Text = label, HotKeySpecifier = labelSpecifier };
            return new TextField { X = left + 1, Y = row * 2 + 1, Width = Dim.Fill() };
        }

        var name = Field(0, "Name", out var nameLabel);
        var path = Field(1, "Path (executable)", out var pathLabel);
        var arguments = Field(2, "Arguments ({input} {output} {dpi})", out var argumentsLabel);
        var formats = Field(3, "Formats (ps, pcl, hpgl, image, bmp...; empty = any)", out var formatsLabel);
        var extension = Field(4, "Output extension", out var extensionLabel);
        var dpi = Field(5, "DPI", out var dpiLabel);
        extension.Width = 12;
        dpi.Width = 12;

        var error = new Label { X = 0, Y = Pos.AnchorEnd(4), Width = Dim.Fill(), HotKeySpecifier = labelSpecifier };
        var add = new Button { X = 0, Y = Pos.AnchorEnd(3), Text = "Add", ShadowStyle = ShadowStyles.None };
        var remove = new Button { X = Pos.Right(add) + 1, Y = Pos.AnchorEnd(3), Text = "Remove", ShadowStyle = ShadowStyles.None };
        var up = new Button { X = Pos.Right(remove) + 1, Y = Pos.AnchorEnd(3), Text = "Up", ShadowStyle = ShadowStyles.None };
        var down = new Button { X = Pos.Right(up) + 1, Y = Pos.AnchorEnd(3), Text = "Down", ShadowStyle = ShadowStyles.None };
        var ok = new Button { X = 0, Y = Pos.AnchorEnd(1), Text = "OK", IsDefault = true, ShadowStyle = ShadowStyles.None };
        var cancel = new Button { X = Pos.Right(ok) + 1, Y = Pos.AnchorEnd(1), Text = "Cancel", ShadowStyle = ShadowStyles.None };

        var parts = new Parts
        {
            Dialog = dialog,
            List = list,
            Name = name,
            Path = path,
            Arguments = arguments,
            Formats = formats,
            Extension = extension,
            Dpi = dpi,
            Error = error,
            Add = add,
            Remove = remove,
            Up = up,
            Down = down,
            Ok = ok,
            Cancel = cancel,
        };

        var loading = false;
        var selected = -1;

        static string Label(StreamConvertToolOptions tool) => string.IsNullOrWhiteSpace(tool.Name) ? "(unnamed)" : tool.Name;

        void LoadFields()
        {
            loading = true;
            var tool = selected >= 0 && selected < editor.Tools.Count ? editor.Tools[selected] : null;
            name.Text = tool?.Name ?? string.Empty;
            path.Text = tool?.Path ?? string.Empty;
            arguments.Text = tool?.Arguments ?? string.Empty;
            formats.Text = tool?.Formats ?? string.Empty;
            extension.Text = tool?.OutputExtension ?? string.Empty;
            dpi.Text = tool is null ? string.Empty : tool.Dpi.ToString(CultureInfo.InvariantCulture);
            foreach (var field in new[] { name, path, arguments, formats, extension, dpi })
            {
                field.Enabled = tool is not null;
            }

            loading = false;
        }

        void Refresh(int select)
        {
            loading = true;
            list.SetSource(new ObservableCollection<string>(editor.Tools.Select(Label)));
            selected = Math.Min(select, editor.Tools.Count - 1);
            if (selected >= 0)
            {
                list.SelectedItem = selected;
            }

            loading = false;
            LoadFields();
        }

        list.ValueChanged += (_, _) =>
        {
            if (!loading && list.SelectedItem is int index && index != selected)
            {
                selected = index;
                LoadFields();
            }
        };

        void Edited()
        {
            if (loading || selected < 0 || selected >= editor.Tools.Count)
            {
                return;
            }

            var tool = editor.Tools[selected];
            tool.Name = name.Text;
            tool.Path = path.Text;
            tool.Arguments = arguments.Text;
            tool.Formats = formats.Text;
            tool.OutputExtension = extension.Text;
            if (int.TryParse(dpi.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                tool.Dpi = value;
            }

            error.Text = string.Empty;
            loading = true;
            list.SetSource(new ObservableCollection<string>(editor.Tools.Select(Label)));
            list.SelectedItem = selected;
            loading = false;
        }

        foreach (var field in new[] { name, path, arguments, formats, extension, dpi })
        {
            field.TextChanged += (_, _) => Edited();
        }

        void Click(Button button, Action action) => button.Accepting += (_, e) =>
        {
            e.Handled = true;
            action();
        };

        Click(add, () =>
        {
            editor.Add();
            Refresh(editor.Tools.Count - 1);
            name.SetFocus();
        });
        Click(remove, () => Refresh(editor.Remove(selected)));
        Click(up, () => Refresh(editor.MoveUp(selected)));
        Click(down, () => Refresh(editor.MoveDown(selected)));
        Click(ok, () =>
        {
            if (editor.Validate() is { } problem)
            {
                error.Text = problem;
                error.SchemeName = FormRenderer.ErrorSchemeName;
                return;
            }

            parts.Accepted = editor.ToList();
            app.RequestStop();
        });
        Click(cancel, app.RequestStop);

        dialog.Add(list, nameLabel, name, pathLabel, path, argumentsLabel, arguments, formatsLabel, formats, extensionLabel, extension, dpiLabel, dpi, error, add, remove, up, down, ok, cancel);
        Refresh(editor.Tools.Count > 0 ? 0 : -1);
        return parts;
    }
}
