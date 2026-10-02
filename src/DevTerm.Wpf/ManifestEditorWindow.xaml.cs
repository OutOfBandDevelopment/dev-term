using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DevTerm.Configuration;
using DevTerm.DeviceManifests;
using DevTerm.DeviceManifests.Editing;
using DevTerm.UiDefinitions.Forms;
using Microsoft.Win32;

namespace DevTerm.Wpf;

/// <summary>
/// The WPF device manifest editor (Device > Edit Device Manifest..., see <c>DevTerm.Console.ManifestEditorMode</c>
/// for the TUI's): the manifest's outline on the left; the selected part's form in the middle,
/// generated from the editor's annotated form models (<see cref="EditorForm"/>) and drawn by the same
/// <see cref="FormRenderer"/> as the Connection Editor; and on the right a live preview of the panel,
/// rebuilt after every edit by the real <see cref="ControlPanelWindow"/> renderer against a surface
/// that sends nothing (what a control would send shows under it). All the logic is the shared
/// <see cref="ManifestEditorViewModel"/>; see docs/specs/manifest-editor.md.
/// </summary>
public partial class ManifestEditorWindow : Window
{
    private FormBinding? _binding;
    private ControlPanelWindow? _previewPanel;
    private bool _previewQueued;

    public ManifestEditorWindow(ManifestEditorViewModel editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        InitializeComponent();

        // Every other window did this; the editor didn't, so it stayed light under a dark theme
        // (found by UiLayoutReviewTests' dark-theme checks).
        WpfTheme.Attach(this);
        Editor = editor;
        OutlineList.ItemsSource = editor.Nodes;

        editor.ConfirmDiscardChanges = () => MessageBox.Show(this, "The manifest has unsaved changes. Discard them?", "dev-term", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
        editor.ConfirmOverwrite = target => MessageBox.Show(this, $"'{target}' already exists. Overwrite it?", "dev-term", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
        editor.SelectionChanged += (_, _) => ShowSelection();
        editor.StructureChanged += (_, _) => ShowSelection();
        editor.StatusChanged += (_, _) => StatusText.Text = editor.StatusMessage;
        editor.Edited += (_, _) =>
        {
            Title = editor.Title;
            RefreshUndoRedo();
            QueuePreview();
        };

        // MenuItem.InputGestureText/ToolTip only labels the shortcut - it doesn't register a live
        // accelerator by itself (same gotcha as MainWindow's Ctrl+Q), so Ctrl+Z/Ctrl+Y need an
        // explicit handler too.
        PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control)
            {
                return;
            }

            if (e.Key == Key.Z)
            {
                e.Handled = true;
                Editor.Undo();
            }
            else if (e.Key == Key.Y)
            {
                e.Handled = true;
                Editor.Redo();
            }
        };

        Closing += (_, e) =>
        {
            if (editor.IsDirty && MessageBox.Show(this, "The manifest has unsaved changes. Close without saving?", "dev-term", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                e.Cancel = true;
            }
        };
        Closed += (_, _) =>
        {
            _binding?.Dispose();
            _previewPanel?.Close();
        };

        Title = editor.Title;
        StatusText.Text = editor.StatusMessage;
        ShowSelection();
        RefreshUndoRedo();
        RefreshPreview();
    }

    private void RefreshUndoRedo()
    {
        UndoButton.IsEnabled = Editor.CanUndo;
        RedoButton.IsEnabled = Editor.CanRedo;
    }

    /// <summary>Opens the editor with the user's and installed manifest folders, optionally on <paramref name="path"/>.</summary>
    public static ManifestEditorWindow Create(string? path = null)
    {
        var editor = new ManifestEditorViewModel(DevTermUserDataPaths.UserManifestsDirectory, DevTermUserDataPaths.AppManifestsDirectory);
        if (path is not null)
        {
            editor.Open(path);
        }

        return new ManifestEditorWindow(editor);
    }

    public ManifestEditorViewModel Editor { get; }

    /// <summary>The selected part's rendered form, while one is shown.</summary>
    internal WpfFormParts? Form { get; private set; }

    /// <summary>The (never shown) panel window whose content is the preview — its lookups (<see cref="ControlPanelWindow.ControlViews"/>, ...) are the preview's.</summary>
    internal ControlPanelWindow? Preview => _previewPanel;

    private void ShowSelection()
    {
        _binding?.Dispose();
        _binding = null;
        Form = null;
        FormHost.Content = null;

        var node = Editor.SelectedNode;
        if (!ReferenceEquals(OutlineList.SelectedItem, node))
        {
            OutlineList.SelectedItem = node;
        }

        AddButton.Content = Editor.AddLabel ?? "Add";
        AddButton.IsEnabled = Editor.AddLabel is not null;
        RemoveButton.IsEnabled = Editor.CanRemove;
        UpButton.IsEnabled = DownButton.IsEnabled = Editor.CanMove;
        PaneTitle.Text = node?.Display.Trim() ?? string.Empty;
        HintText.Text = node?.Form is null ? node?.Hint ?? string.Empty : string.Empty;
        HintText.Visibility = HintText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        CreatePanelButton.Visibility = node is { Kind: ManifestNodeKind.Panel, Form: null } ? Visibility.Visible : Visibility.Collapsed;

        if (node?.Form is not { } form)
        {
            return;
        }

        _binding = new FormBinding(form);
        var options = new WpfFormOptions { LabelColumnWidth = 130, HideFirstSectionHeaderIfEquals = PaneTitle.Text };
        if (form is ControlForm)
        {
            options.TextPickers[nameof(ControlForm.IndicatorExpression)] = text =>
            {
                var picker = new ExpressionPickerWindow(new ExpressionPickerViewModel(ValuePathCatalog.Enumerate(Editor.Manifest), text)) { Owner = this };
                return picker.ShowDialog() == true ? picker.Accepted : null;
            };
            options.TextPickers[nameof(ControlForm.Channels)] = text =>
            {
                var picker = new ExpressionPickerWindow(new ExpressionPickerViewModel(ValuePathCatalog.Enumerate(Editor.Manifest), text, mode: PickerMode.Channels)) { Owner = this };
                return picker.ShowDialog() == true ? picker.Accepted : null;
            };
            options.TextPickers[nameof(ControlForm.XId)] = text => PickFor(text, PickerMode.ValueId);
            options.TextPickers[nameof(ControlForm.YId)] = text => PickFor(text, PickerMode.ValueId);
            options.TextPickers[nameof(ControlForm.ZId)] = text => PickFor(text, PickerMode.ValueId);
            options.TextPickers[nameof(ControlForm.RadiusId)] = text => PickFor(text, PickerMode.ValueId);
            options.TextPickers[nameof(ControlForm.AngleId)] = text => PickFor(text, PickerMode.ValueId);
            options.TextPickers[nameof(ControlForm.HueId)] = text => PickFor(text, PickerMode.ValueId);
            options.TextPickers[nameof(ControlForm.SaturationId)] = text => PickFor(text, PickerMode.ValueId);
            options.TextPickers[nameof(ControlForm.BrightnessId)] = text => PickFor(text, PickerMode.ValueId);
            options.TextPickers[nameof(ControlForm.VisibleWhenId)] = text => PickFor(text, PickerMode.ValueId);
            options.TextPickers[nameof(ControlForm.ParameterExpressions)] = text => PickFor(text, PickerMode.ExpressionList);
        }

        Form = FormRenderer.Build(FormDefinitionGenerator.Generate(form.GetType(), form), _binding, options);
        FormHost.Content = Form.Root;
    }

    private string? PickFor(string text, PickerMode mode)
    {
        var picker = new ExpressionPickerWindow(new ExpressionPickerViewModel(ValuePathCatalog.Enumerate(Editor.Manifest), text, mode: mode)) { Owner = this };
        return picker.ShowDialog() == true ? picker.Accepted : null;
    }

    // Rebuilt at most once per burst of edits (every keystroke is an edit), after they've all applied.
    private void QueuePreview()
    {
        if (_previewQueued)
        {
            return;
        }

        _previewQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _previewQueued = false;
            RefreshPreview();
        });
    }

    /// <summary>
    /// Rebuilds the preview: a real <see cref="ControlPanelWindow"/> for the panel as it would open,
    /// never shown — its content is moved into this window's preview area, so the preview is exactly
    /// the live panel's rendering (sections, alignment, ⓘ command previews) without a second window.
    /// </summary>
    internal void RefreshPreview()
    {
        var definition = Editor.BuildPreviewDefinition();
        var surface = Editor.CreatePreviewSurface(definition);
        surface.PreviewInvoked += (_, sent) => Dispatcher.BeginInvoke(() => PreviewSentText.Text = $"Would send: {sent}");
        var presenter = Editor.CreatePreviewPresenter();
        var panel = new ControlPanelWindow(definition, surface, presenter);
        Editor.PublishSampleData(presenter);
        var content = (UIElement)panel.Content;
        panel.Content = null;
        PreviewHost.Child = content;

        _previewPanel?.Close();
        _previewPanel = panel;
    }

    private void OutlineList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OutlineList.SelectedItem is ManifestEditorNode node)
        {
            Editor.Select(node);
        }
    }

    private void New_Click(object sender, RoutedEventArgs e) => Editor.New();

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ManifestPickerWindow(InstalledManifests.Discover(), pathOnly: true) { Owner = this, Title = "dev-term — Open Device Manifest to Edit" };
        if (picker.ShowDialog() == true && picker.ChosenPath is { } path)
        {
            Editor.Open(path);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e) => Editor.Save();

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Device manifest (*.json)|*.json",
            FileName = "device.json",
            InitialDirectory = Directory.Exists(Editor.SaveTarget) ? Editor.SaveTarget : DevTermUserDataPaths.UserManifestsDirectory,
        };
        if (dialog.ShowDialog(this) == true)
        {
            Editor.SaveAs(dialog.FileName);
        }
    }

    private void Validate_Click(object sender, RoutedEventArgs e) => Editor.Validate();

    private void Undo_Click(object sender, RoutedEventArgs e) => Editor.Undo();

    private void Redo_Click(object sender, RoutedEventArgs e) => Editor.Redo();

    private void ImportKsy_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Kaitai Struct (*.ksy)|*.ksy|All files (*.*)|*.*", Title = "Import a .ksy file as the binary frame" };
        if (dialog.ShowDialog(this) == true)
        {
            Editor.ImportKsy(dialog.FileName);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Add_Click(object sender, RoutedEventArgs e) => Editor.AddChild();

    private void Remove_Click(object sender, RoutedEventArgs e) => Editor.Remove();

    private void Up_Click(object sender, RoutedEventArgs e) => Editor.MoveUp();

    private void Down_Click(object sender, RoutedEventArgs e) => Editor.MoveDown();

    private void CreatePanel_Click(object sender, RoutedEventArgs e) => Editor.CreatePanelFromCommands();
}
