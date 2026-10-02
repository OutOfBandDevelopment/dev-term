using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using DevTerm.Configuration;
using Microsoft.Win32;

namespace DevTerm.Wpf;

/// <summary>
/// The WPF "Converter tools" dialog: add, edit, remove and reorder the Stream Monitor's registered converter tools
/// (<see cref="StreamConvertToolOptions"/>) over a <see cref="ConverterToolsEditor"/>. The Terminal.Gui equivalent is
/// <c>DevTerm.Console.ConverterToolsDialog</c>. See docs/specs/converter-tools-editor.md.
/// </summary>
public partial class ConverterToolsWindow : Window
{
    private readonly ConverterToolsEditor _editor;
    private bool _loading;

    public ConverterToolsWindow(ConverterToolsEditor editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        _editor = editor;
        InitializeComponent();
        WpfTheme.Attach(this);
        RefreshList(_editor.Tools.Count > 0 ? 0 : -1);
    }

    /// <summary>The edited tools; read after the dialog returned true.</summary>
    public IReadOnlyList<StreamConvertToolOptions> Result => _editor.ToList();

    internal ListBox List => ToolList;

    internal TextBox NameField => NameBox;

    internal TextBox PathField => PathBox;

    internal TextBox ArgumentsField => ArgumentsBox;

    internal TextBox FormatsField => FormatsBox;

    internal TextBox ExtensionField => ExtensionBox;

    internal TextBox DpiField => DpiBox;

    internal TextBlock Error => ErrorText;

    internal void AddTool() => Add_Click(this, new RoutedEventArgs());

    internal void MoveUp() => Up_Click(this, new RoutedEventArgs());

    internal void MoveDown() => Down_Click(this, new RoutedEventArgs());

    internal void RemoveSelected() => Remove_Click(this, new RoutedEventArgs());

    internal void TryAccept() => Ok_Click(this, new RoutedEventArgs());

    private StreamConvertToolOptions? Selected => ToolList.SelectedIndex >= 0 && ToolList.SelectedIndex < _editor.Tools.Count ? _editor.Tools[ToolList.SelectedIndex] : null;

    private static string Label(StreamConvertToolOptions tool) => string.IsNullOrWhiteSpace(tool.Name) ? "(unnamed)" : tool.Name;

    private void RefreshList(int select)
    {
        _loading = true;
        ToolList.Items.Clear();
        foreach (var tool in _editor.Tools)
        {
            ToolList.Items.Add(Label(tool));
        }

        ToolList.SelectedIndex = Math.Min(select, _editor.Tools.Count - 1);
        _loading = false;
        LoadFields();
    }

    private void LoadFields()
    {
        _loading = true;
        var tool = Selected;
        DetailGrid.IsEnabled = tool is not null;
        NameBox.Text = tool?.Name ?? string.Empty;
        PathBox.Text = tool?.Path ?? string.Empty;
        ArgumentsBox.Text = tool?.Arguments ?? string.Empty;
        FormatsBox.Text = tool?.Formats ?? string.Empty;
        ExtensionBox.Text = tool?.OutputExtension ?? string.Empty;
        DpiBox.Text = tool is null ? string.Empty : tool.Dpi.ToString(CultureInfo.InvariantCulture);
        RemoveButton.IsEnabled = tool is not null;
        UpButton.IsEnabled = ToolList.SelectedIndex > 0;
        DownButton.IsEnabled = ToolList.SelectedIndex >= 0 && ToolList.SelectedIndex < _editor.Tools.Count - 1;
        _loading = false;
    }

    private void ToolList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading)
        {
            LoadFields();
        }
    }

    private void Field_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || Selected is not { } tool)
        {
            return;
        }

        tool.Name = NameBox.Text;
        tool.Path = PathBox.Text;
        tool.Arguments = ArgumentsBox.Text;
        tool.Formats = FormatsBox.Text;
        tool.OutputExtension = ExtensionBox.Text;
        if (int.TryParse(DpiBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dpi))
        {
            tool.Dpi = dpi;
        }

        if (ReferenceEquals(sender, NameBox))
        {
            var index = ToolList.SelectedIndex;
            _loading = true;
            ToolList.Items[index] = Label(tool);
            ToolList.SelectedIndex = index;
            _loading = false;
        }

        ErrorText.Text = string.Empty;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        _editor.Add();
        RefreshList(_editor.Tools.Count - 1);
        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void Remove_Click(object sender, RoutedEventArgs e) => RefreshList(_editor.Remove(ToolList.SelectedIndex));

    private void Up_Click(object sender, RoutedEventArgs e) => RefreshList(_editor.MoveUp(ToolList.SelectedIndex));

    private void Down_Click(object sender, RoutedEventArgs e) => RefreshList(_editor.MoveDown(ToolList.SelectedIndex));

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Converter executable", Filter = "Programs (*.exe;*.bat;*.cmd)|*.exe;*.bat;*.cmd|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) == true)
        {
            PathBox.Text = dialog.FileName;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_editor.Validate() is { } problem)
        {
            ErrorText.Text = problem;
            return;
        }

        try
        {
            DialogResult = true;
        }
        catch (InvalidOperationException)
        {
            // Not shown via ShowDialog() (a test drives the window directly): Result is already correct.
        }
    }
}
