using System.Text;
using DevTerm.Configuration;
using DevTerm.UiDefinitions;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace DevTerm.Console;

/// <summary>The dialog and the actions a test can drive without key injection.</summary>
internal sealed record ConfigureDeviceParts(Dialog Dialog, TextField HostField, TextField PortField, Action Read, Action Write, Label Status, View Form, Button WriteButton);

/// <summary>
/// The TUI's "Device > Configure device..." flow over <see cref="DeviceConfigViewModel"/>: pick an editor (when several are
/// registered), type the host and port, Read, edit the form the editor declares, review the change list, then Write.
/// Reading and writing block the UI for the length of the network call; both are bounded by the editor's own timeouts.
/// </summary>
internal static class ConfigureDeviceMode
{
    private const int _labelWidth = 24;

    private static readonly Rune _noHotKey = (Rune)0xFFFF;

    public static void Run(IApplication app, DeviceConfigViewModel viewModel)
    {
        var parts = BuildWindow(app, viewModel);
        try
        {
            app.Run(parts.Dialog);
        }
        finally
        {
            parts.Dialog.Dispose();
        }
    }

    public static ConfigureDeviceParts BuildWindow(IApplication app, DeviceConfigViewModel viewModel)
    {
        var dialog = new Dialog { Title = "Configure device", Width = Dim.Percent(90), Height = Dim.Percent(90) };

        var editorLabel = new Label { X = 0, Y = 0, Text = "Editor:" };
        var editorSelector = new OptionSelector { X = _labelWidth, Y = 0, Orientation = Orientation.Horizontal, HorizontalSpace = 2, Labels = [.. viewModel.Editors.Select(e => e.Title)] };
        if (viewModel.Editor is { } preselected)
        {
            editorSelector.Value = IndexOfEditor(viewModel, preselected.Id);
        }

        var hostLabel = new Label { X = 0, Y = 1, Text = "Host:" };
        var hostField = new TextField { X = _labelWidth, Y = 1, Width = 30, Text = viewModel.Host };
        var portLabel = new Label { X = Pos.Right(hostField) + 2, Y = 1, Text = "Port:" };
        var portField = new TextField { X = Pos.Right(portLabel) + 1, Y = 1, Width = 8, Text = viewModel.Port };
        var form = new View { X = 0, Y = 3, Width = Dim.Fill(), Height = Dim.Fill(7), CanFocus = true };
        var summary = new Label { X = 0, Y = Pos.AnchorEnd(6), Width = Dim.Fill(), Height = 3, Text = string.Empty, HotKeySpecifier = _noHotKey };
        var status = new Label { X = 0, Y = Pos.AnchorEnd(3), Width = Dim.Fill(), Text = string.Empty, HotKeySpecifier = _noHotKey };
        var readButton = new Button { X = 0, Y = Pos.AnchorEnd(2), Text = "Read from device", ShadowStyle = ShadowStyles.None };
        var writeButton = new Button { X = Pos.Right(readButton), Y = Pos.AnchorEnd(2), Text = "Write to device", ShadowStyle = ShadowStyles.None };
        var closeButton = new Button { X = Pos.Right(writeButton), Y = Pos.AnchorEnd(2), Text = "Close", ShadowStyle = ShadowStyles.None };

        void Refresh()
        {
            var lines = new List<string>();
            lines.AddRange(viewModel.ChangeLines().Select(l => "  change  " + l));
            lines.AddRange(viewModel.IssueLines().Select(l => "  problem " + l));
            if (viewModel.WillDropConnection)
            {
                lines.Add("Writing these values changes the address, so the connection will drop.");
            }

            summary.Text = string.Join('\n', lines.Take(3));
            status.Text = viewModel.Message ?? string.Empty;
            writeButton.Enabled = viewModel.CanWrite;
            app.LayoutAndDraw(true);
        }

        void BuildForm()
        {
            foreach (var child in form.SubViews.ToList())
            {
                form.Remove(child);
                child.Dispose();
            }

            if (viewModel.Session is null)
            {
                return;
            }

            var row = 0;
            foreach (var control in viewModel.Fields)
            {
                var id = control.Id;
                form.Add(new Label { X = 0, Y = row, Width = _labelWidth - 1, Text = control.Label, HotKeySpecifier = _noHotKey });
                switch (control)
                {
                    case ToggleControl:
                        var box = new CheckBox { X = _labelWidth, Y = row, Value = viewModel.IsOn(id) ? CheckState.Checked : CheckState.UnChecked };
                        box.ValueChanged += (_, _) =>
                        {
                            viewModel.SetOn(id, box.Value == CheckState.Checked);
                            Refresh();
                        };
                        form.Add(box);
                        break;
                    case ChoiceControl choice:
                        var selector = new OptionSelector { X = _labelWidth, Y = row, Orientation = Orientation.Horizontal, HorizontalSpace = 2, Labels = choice.Options };
                        selector.Value = Math.Max(0, choice.Options.IndexOf(viewModel.Value(id)));
                        selector.ValueChanged += (_, _) =>
                        {
                            if (selector.Value is { } index && index >= 0 && index < choice.Options.Count)
                            {
                                viewModel.Set(id, choice.Options[index]);
                                Refresh();
                            }
                        };
                        form.Add(selector);
                        break;
                    default:
                        var field = new TextField { X = _labelWidth, Y = row, Width = Dim.Fill(2), Text = viewModel.Value(id) };
                        field.TextChanged += (_, _) =>
                        {
                            viewModel.Set(id, field.Text ?? string.Empty);
                            Refresh();
                        };
                        form.Add(field);
                        break;
                }

                row++;
            }
        }

        void Read()
        {
            viewModel.Host = hostField.Text ?? string.Empty;
            viewModel.Port = portField.Text ?? string.Empty;
            viewModel.ReadAsync().GetAwaiter().GetResult();
            BuildForm();
            Refresh();
        }

        void Write()
        {
            viewModel.WriteAsync().GetAwaiter().GetResult();
            BuildForm();
            Refresh();
        }

        editorSelector.ValueChanged += (_, _) =>
        {
            if (editorSelector.Value is { } index && index >= 0 && index < viewModel.Editors.Count)
            {
                viewModel.PickEditor(viewModel.Editors[index].Id);
                BuildForm();
                Refresh();
            }
        };
        readButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            Read();
        };
        writeButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            Write();
        };
        closeButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            app.RequestStop();
        };

        dialog.Add(editorLabel, editorSelector, hostLabel, hostField, portLabel, portField, form, summary, status, readButton, writeButton, closeButton);
        Refresh();
        return new ConfigureDeviceParts(dialog, hostField, portField, Read, Write, status, form, writeButton);
    }

    private static int IndexOfEditor(DeviceConfigViewModel viewModel, string id)
    {
        for (var i = 0; i < viewModel.Editors.Count; i++)
        {
            if (viewModel.Editors[i].Id == id)
            {
                return i;
            }
        }

        return 0;
    }
}
