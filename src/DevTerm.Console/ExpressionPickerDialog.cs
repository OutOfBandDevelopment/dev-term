using System.Collections.ObjectModel;
using DevTerm.DeviceManifests.Editing;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace DevTerm.Console;

/// <summary>
/// The Terminal.Gui expression picker: a modal over <see cref="ExpressionPickerViewModel"/> with the expression
/// text, a filterable list of the paths it may read, the language's functions, live diagnostics and the result
/// against sample data. See docs/specs/expression-picker.md.
/// </summary>
internal static class ExpressionPickerDialog
{
    /// <summary>Opens the picker over <paramref name="viewModel"/>; returns the final expression, or null when cancelled.</summary>
    public static string? Show(IApplication app, ExpressionPickerViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(viewModel);

        var screenWidth = app.Screen.Width > 0 ? app.Screen.Width : 80;
        var screenHeight = app.Screen.Height > 0 ? app.Screen.Height : 25;
        var dialog = new Dialog { Title = "Expression", Width = Math.Min(screenWidth - 2, 78), Height = Math.Min(screenHeight - 1, 24) };

        var expressionLabel = new Label { X = 0, Y = 0, Text = "Expression:", HotKeySpecifier = (System.Text.Rune)0xFFFF };
        var expression = new TextField { X = 0, Y = 1, Width = Dim.Fill(), Text = viewModel.Text };
        var diagnostics = new Label { X = 0, Y = 2, Width = Dim.Fill(), Height = 2, HotKeySpecifier = (System.Text.Rune)0xFFFF };
        var result = new Label { X = 0, Y = 4, Width = Dim.Fill(), HotKeySpecifier = (System.Text.Rune)0xFFFF };

        var filterLabel = new Label { X = 0, Y = 6, Text = "Find a value:", HotKeySpecifier = (System.Text.Rune)0xFFFF };
        var filter = new TextField { X = Pos.Right(filterLabel) + 1, Y = 6, Width = Dim.Fill() };
        var paths = new ListView { X = 0, Y = 7, Width = Dim.Fill(), Height = Dim.Fill(5) };

        var functionLabel = new Label { X = 0, Y = Pos.AnchorEnd(4), Text = "Functions:", HotKeySpecifier = (System.Text.Rune)0xFFFF };
        Button? previous = null;
        var functionButtons = new List<Button>();
        foreach (var function in ExpressionPickerViewModel.Functions)
        {
            var captured = function;
            var button = new Button { X = previous is null ? Pos.Right(functionLabel) + 1 : Pos.Right(previous), Y = Pos.AnchorEnd(4), Text = function.Name, ShadowStyle = ShadowStyles.None };
            button.Accepting += (_, e) =>
            {
                e.Handled = true;
                viewModel.CaretIndex = expression.InsertionPoint;
                viewModel.InsertFunction(captured);
                expression.SetFocus();
            };
            functionButtons.Add(button);
            previous = button;
        }

        var nextSample = new Button { X = Pos.Right(previous!), Y = Pos.AnchorEnd(4), Text = "Next sample", ShadowStyle = ShadowStyles.None };
        nextSample.Accepting += (_, e) =>
        {
            e.Handled = true;
            viewModel.NextSample();
        };

        if (viewModel.IsChannelList)
        {
            // A channel list has no single result or functions: choosing a value appends it as a channel.
            dialog.Title = "Channels";
            expressionLabel.Text = "Channels (id[:label[:#RRGGBB[:expression]]], separated by ;):";
            result.Visible = false;
            functionLabel.Visible = false;
            nextSample.Visible = false;
            foreach (var button in functionButtons)
            {
                button.Visible = false;
            }
        }

        string? accepted = null;
        var ok = new Button { X = 0, Y = Pos.AnchorEnd(2), Text = "OK", IsDefault = true, ShadowStyle = ShadowStyles.None };
        ok.Accepting += (_, e) =>
        {
            e.Handled = true;
            accepted = viewModel.Text;
            app.RequestStop();
        };
        var cancel = new Button { X = Pos.Right(ok) + 1, Y = Pos.AnchorEnd(2), Text = "Cancel", ShadowStyle = ShadowStyles.None };
        cancel.Accepting += (_, e) =>
        {
            e.Handled = true;
            app.RequestStop();
        };

        var syncing = false;

        void Refresh()
        {
            syncing = true;
            try
            {
                if (expression.Text != viewModel.Text)
                {
                    expression.Text = viewModel.Text;
                    expression.InsertionPoint = viewModel.CaretIndex;
                }

                diagnostics.Text = viewModel.Diagnostics;
                diagnostics.SchemeName = viewModel.IsEmpty || (viewModel.IsValid && viewModel.Warnings.Count == 0) ? null : FormRenderer.ErrorSchemeName;
                result.Text = viewModel.IsChannelList ? string.Empty : $"Result with sample data: {viewModel.ResultText}";
                var rows = viewModel.Paths.Select(p => p.ToString()).ToList();
                paths.SetSource(new ObservableCollection<string>(rows));
            }
            finally
            {
                syncing = false;
            }
        }

        viewModel.Changed += (_, _) => Refresh();
        expression.TextChanged += (_, _) =>
        {
            if (!syncing)
            {
                viewModel.Text = expression.Text;
            }
        };
        filter.TextChanged += (_, _) =>
        {
            if (!syncing)
            {
                viewModel.Filter = filter.Text;
            }
        };

        void InsertSelected()
        {
            if (paths.SelectedItem is int index && index >= 0 && index < viewModel.Paths.Count)
            {
                viewModel.CaretIndex = expression.InsertionPoint;
                viewModel.InsertPath(viewModel.Paths[index]);
            }
        }

        paths.Accepting += (_, e) =>
        {
            e.Handled = true;
            InsertSelected();
        };

        dialog.Add(expressionLabel, expression, diagnostics, result, filterLabel, filter, paths, functionLabel);
        foreach (var button in functionButtons)
        {
            dialog.Add(button);
        }

        dialog.Add(nextSample, ok, cancel);
        Refresh();
        expression.InsertionPoint = viewModel.CaretIndex;
        app.Run(dialog);
        return accepted;
    }
}
