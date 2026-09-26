using System.Collections.ObjectModel;
using System.Drawing;
using DevTerm.Configuration;
using DevTerm.DeviceManifests;
using DevTerm.DeviceManifests.Editing;
using DevTerm.UiDefinitions.Forms;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace DevTerm.Console;

/// <summary>
/// The TUI's device manifest editor (Device > Edit Device Manifest...): an outline of the manifest
/// (identity, commands and their parameters, response patterns, the panel's sections and controls)
/// on the left; on the right, the selected part's form — generated from the editor's annotated form
/// models (<see cref="EditorForm"/>) and drawn by the same <see cref="FormRenderer"/> as the Connection
/// Editor — or, with Preview, the panel itself, drawn live by <see cref="ControlPanelMode"/> against a
/// surface that sends nothing (what a control would send appears in the status line). All the logic
/// is the shared <see cref="ManifestEditorViewModel"/>; see docs/specs/manifest-editor.md.
/// </summary>
internal static class ManifestEditorMode
{
    /// <summary>Runs the editor (a nested <c>Application.Run</c>), opening <paramref name="path"/> first when given.</summary>
    public static void Run(IApplication app, string? path = null)
    {
        var editor = new ManifestEditorViewModel(DevTermUserDataPaths.UserManifestsDirectory, DevTermUserDataPaths.AppManifestsDirectory);
        if (path is not null)
        {
            editor.Open(path);
        }

        var parts = BuildWindow(app, editor);
        try
        {
            app.Run(parts.Window);
        }
        finally
        {
            parts.Window.Dispose();
        }
    }

    /// <summary>Builds the editor window around <paramref name="editor"/> without running it — the seam tests drive headlessly.</summary>
    public static ManifestEditorParts BuildWindow(IApplication app, ManifestEditorViewModel editor)
    {
        ArgumentNullException.ThrowIfNull(editor);

        var window = new Window { Title = editor.Title, X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };

        // Shadowless, one-line toolbar buttons: with shadows the seven don't fit an 80-column
        // terminal, and each row of buttons would cost a second line.
        static Button ToolButton(string text) => new() { Text = text, ShadowStyle = ShadowStyles.None };

        var newButton = ToolButton("New");
        var openButton = ToolButton("Open...");
        var saveButton = ToolButton("Save");
        var saveAsButton = ToolButton("Save As...");
        var validateButton = ToolButton("Check");
        var previewButton = ToolButton("Preview");
        var closeButton = ToolButton("Close");
        Button? previous = null;
        foreach (var button in new[] { newButton, openButton, saveButton, saveAsButton, validateButton, previewButton, closeButton })
        {
            button.X = previous is null ? 0 : Pos.Right(previous);
            button.Y = 0;
            previous = button;
        }

        const int StatusWidth = 76;
        var status = new Label { X = 0, Y = 1, Width = Dim.Fill(), Height = 2 };
        void ShowStatus(string text) => status.Text = string.Join('\n', ControlPanelMode.WordWrap(text, StatusWidth).Take(2));
        ShowStatus(editor.StatusMessage);

        const int OutlineWidth = 30;
        var outlineFrame = new FrameView { Title = "Outline", X = 0, Y = 3, Width = OutlineWidth, Height = Dim.Fill(1) };
        var outline = new ListView { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
        outlineFrame.Add(outline);

        var addButton = ToolButton("Add");
        var removeButton = ToolButton("Remove");
        var upButton = ToolButton("Up");
        var downButton = ToolButton("Down");
        addButton.X = 0;
        removeButton.X = Pos.Right(addButton) + 1;
        upButton.X = Pos.Right(removeButton) + 1;
        downButton.X = Pos.Right(upButton) + 1;
        foreach (var button in new[] { addButton, removeButton, upButton, downButton })
        {
            button.Y = Pos.AnchorEnd(1);
        }

        var pane = new FrameView { Title = "Edit", X = OutlineWidth, Y = 3, Width = Dim.Fill(), Height = Dim.Fill(1) };

        var parts = new ManifestEditorParts
        {
            ViewModel = editor,
            Window = window,
            Outline = outline,
            Pane = pane,
            StatusLabel = status,
            NewButton = newButton,
            OpenButton = openButton,
            SaveButton = saveButton,
            SaveAsButton = saveAsButton,
            ValidateButton = validateButton,
            PreviewButton = previewButton,
            CloseButton = closeButton,
            AddButton = addButton,
            RemoveButton = removeButton,
            UpButton = upButton,
            DownButton = downButton,
        };

        FormBinding? binding = null;
        Window? previewWindow = null;

        void ClearPane()
        {
            binding?.Dispose();
            binding = null;
            parts.Form = null;
            parts.Preview = null;
            foreach (var view in pane.SubViews.ToList())
            {
                pane.Remove(view);
                view.Dispose();
            }

            previewWindow = null;
        }

        void RefreshOutline()
        {
            var index = editor.SelectedNode is { } node ? editor.Nodes.IndexOf(node) : 0;
            outline.SetSource(new ObservableCollection<string>(editor.Nodes.Select(n => n.Display)));
            outline.SelectedItem = index >= 0 ? index : 0;
        }

        void RefreshButtons()
        {
            addButton.Text = editor.AddLabel ?? "Add";
            addButton.Enabled = editor.AddLabel is not null;
            removeButton.Enabled = editor.CanRemove;
            upButton.Enabled = downButton.Enabled = editor.CanMove;
        }

        void ShowSelection()
        {
            ClearPane();
            parts.ShowingPreview = false;
            previewButton.Text = "Preview";
            RefreshButtons();
            if (editor.SelectedNode is not { } node)
            {
                return;
            }

            pane.Title = node.Display.Trim();
            if (node.Form is not { } form)
            {
                var hint = new Label { X = 0, Y = 0, Width = Dim.Fill(), Text = string.Join('\n', ControlPanelMode.WordWrap(node.Hint ?? string.Empty, 44)) };
                pane.Add(hint);
                if (node.Kind == ManifestNodeKind.Panel)
                {
                    var create = new Button { X = 0, Y = Pos.Bottom(hint) + 1, Text = "Create panel from commands" };
                    create.Accepting += (_, e) =>
                    {
                        e.Handled = true;
                        editor.CreatePanelFromCommands();
                    };
                    pane.Add(create);
                }

                return;
            }

            // The selected part's form: generated from its form model, rendered like any other form,
            // inside a view that scrolls when the form is taller than the pane.
            binding = new FormBinding(form);
            var formParts = FormRenderer.Build(app, FormDefinitionGenerator.Generate(form.GetType(), form), binding, new TuiFormOptions { AvailableWidth = Math.Max((app.Screen.Width > 0 ? app.Screen.Width : 80) - OutlineWidth - 4, 30) });
            var scroller = new View { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), CanFocus = true };
            scroller.ViewportSettings |= ViewportSettingsFlags.HasVerticalScrollBar;
            scroller.Add(formParts.Root);

            // Content as wide as the pane (known only once laid out) and as tall as the form.
            void SizeContent() => scroller.SetContentSize(new Size(Math.Max(scroller.Viewport.Width, 40), Math.Max(formParts.Rows, 1)));
            SizeContent();
            formParts.Reflowed += (_, _) => SizeContent();
            scroller.SubViewsLaidOut += (_, _) =>
            {
                if (scroller.GetContentSize().Width != Math.Max(scroller.Viewport.Width, 40))
                {
                    SizeContent();
                }
            };
            formParts.RowFocused += (top, height) =>
            {
                var viewport = scroller.Viewport;
                var y = top < viewport.Y ? top : top + height > viewport.Y + viewport.Height ? top + height - viewport.Height : viewport.Y;
                scroller.Viewport = viewport with { Y = Math.Max(0, y) };
            };
            scroller.MouseEvent += (_, mouse) =>
            {
                var delta = mouse.Flags.HasFlag(MouseFlags.WheeledDown) ? 1 : mouse.Flags.HasFlag(MouseFlags.WheeledUp) ? -1 : 0;
                if (delta != 0)
                {
                    var maxY = Math.Max(0, formParts.Rows - scroller.Viewport.Height);
                    scroller.Viewport = scroller.Viewport with { Y = Math.Clamp(scroller.Viewport.Y + delta, 0, maxY) };
                    mouse.Handled = true;
                }
            };
            pane.Add(scroller);
            parts.Form = formParts;
        }

        void ShowPreview()
        {
            ClearPane();
            parts.ShowingPreview = true;
            previewButton.Text = "Edit";
            pane.Title = "Preview (sends nothing)";

            // The real control-panel renderer, against a surface that only reports what it would
            // send - the panel exactly as Device > Device Manifest... would open it.
            var definition = editor.BuildPreviewDefinition();
            var surface = editor.CreatePreviewSurface(definition);
            surface.PreviewInvoked += (_, sent) => ShowStatus($"Preview: would send {sent}");
            var panelParts = ControlPanelMode.BuildWindow(app, definition, surface, editor.CreatePreviewPresenter(), definition.Name);
            previewWindow = panelParts.Window;
            previewWindow.X = 0;
            previewWindow.Y = 0;
            previewWindow.Width = Dim.Fill();
            previewWindow.Height = Dim.Fill();
            pane.Add(previewWindow);
            parts.Preview = panelParts;
        }

        parts.ShowPreviewAction = ShowPreview;
        parts.ShowSelectionAction = ShowSelection;

        outline.ValueChanged += (_, _) =>
        {
            if (outline.SelectedItem is int index)
            {
                editor.Select(index);
            }
        };

        editor.SelectionChanged += (_, _) => ShowSelection();
        editor.StructureChanged += (_, _) =>
        {
            RefreshOutline();
            ShowSelection();
        };
        editor.Edited += (_, _) =>
        {
            window.Title = editor.Title;
            if (outline.Source?.Count == editor.Nodes.Count)
            {
                RefreshOutline();
            }
        };
        editor.StatusChanged += (_, _) => ShowStatus(editor.StatusMessage);

        editor.ConfirmDiscardChanges = () =>
            MessageBox.Query(app, "dev-term", "The manifest has unsaved changes. Discard them?", ["Yes", "No"]) == 0;
        editor.ConfirmOverwrite = target =>
            MessageBox.Query(app, "dev-term", $"'{target}' already exists. Overwrite it?", ["Yes", "No"]) == 0;

        void OnClick(Button button, Action action)
        {
            button.Accepting += (_, e) =>
            {
                e.Handled = true;
                action();
            };
        }

        OnClick(newButton, () =>
        {
            if (editor.New())
            {
                RefreshOutline();
                ShowSelection();
            }
        });
        OnClick(openButton, () =>
        {
            if (ManifestPanelMode.Pick(app, InstalledManifests.Discover()) is { } path && editor.Open(path))
            {
                RefreshOutline();
                ShowSelection();
            }
        });
        OnClick(saveButton, () => editor.Save());
        OnClick(saveAsButton, () =>
        {
            var dialog = new SaveDialog { Title = "Save manifest as (a .json file, or a folder for device.json)", Path = editor.SaveTarget };
            app.Run(dialog);
            if (!dialog.Canceled && dialog.FileName is { Length: > 0 } fileName)
            {
                editor.SaveAs(fileName);
            }

            dialog.Dispose();
        });
        OnClick(validateButton, () => editor.Validate());
        OnClick(previewButton, () =>
        {
            if (parts.ShowingPreview)
            {
                ShowSelection();
            }
            else
            {
                ShowPreview();
            }
        });
        OnClick(closeButton, () =>
        {
            if (!editor.IsDirty || MessageBox.Query(app, "dev-term", "The manifest has unsaved changes. Close without saving?", ["Yes", "No"]) == 0)
            {
                app.RequestStop();
            }
        });
        OnClick(addButton, editor.AddChild);
        OnClick(removeButton, editor.Remove);
        OnClick(upButton, editor.MoveUp);
        OnClick(downButton, editor.MoveDown);

        window.Add(newButton, openButton, saveButton, saveAsButton, validateButton, previewButton, closeButton, status, outlineFrame, pane, addButton, removeButton, upButton, downButton);
        window.Disposing += (_, _) => ClearPane();

        RefreshOutline();
        ShowSelection();
        return parts;
    }
}

/// <summary>The editor's controls, for tests to drive headlessly.</summary>
internal sealed class ManifestEditorParts
{
    public required ManifestEditorViewModel ViewModel { get; init; }

    public required Window Window { get; init; }

    public required ListView Outline { get; init; }

    /// <summary>The right-hand pane: the selected part's form, a heading's hint, or the preview.</summary>
    public required FrameView Pane { get; init; }

    public required Label StatusLabel { get; init; }

    public required Button NewButton { get; init; }

    public required Button OpenButton { get; init; }

    public required Button SaveButton { get; init; }

    public required Button SaveAsButton { get; init; }

    public required Button ValidateButton { get; init; }

    public required Button PreviewButton { get; init; }

    public required Button CloseButton { get; init; }

    public required Button AddButton { get; init; }

    public required Button RemoveButton { get; init; }

    public required Button UpButton { get; init; }

    public required Button DownButton { get; init; }

    /// <summary>The selected part's rendered form, while one is shown.</summary>
    public TuiFormParts? Form { get; set; }

    /// <summary>The live panel preview, while it's shown.</summary>
    public ControlPanelWindowParts? Preview { get; set; }

    public bool ShowingPreview { get; set; }

    internal Action ShowPreviewAction { get; set; } = () => { };

    internal Action ShowSelectionAction { get; set; } = () => { };

    public void ShowPreview() => ShowPreviewAction();

    public void ShowSelection() => ShowSelectionAction();
}
