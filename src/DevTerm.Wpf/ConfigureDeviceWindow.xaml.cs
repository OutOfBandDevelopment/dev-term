using System.Windows;
using System.Windows.Controls;
using DevTerm.Configuration;
using DevTerm.Core.Control;
using DevTerm.UiDefinitions;

namespace DevTerm.Wpf;

/// <summary>
/// Device &gt; Configure device: the WPF half of the flow over <see cref="DeviceConfigViewModel"/> (the TUI's is
/// <c>ConfigureDeviceMode</c>). Pick an editor, type the host and port, Read, edit the form the editor declares,
/// review the change list, then Write. Reads and writes await the editor, so the window stays responsive.
/// </summary>
public partial class ConfigureDeviceWindow : Window
{
    public ConfigureDeviceWindow(DeviceConfigViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;
        InitializeComponent();
        WpfTheme.Attach(this);
        EditorBox.ItemsSource = viewModel.Editors;
        EditorBox.SelectedItem = viewModel.Editor;
        HostBox.Text = viewModel.Host;
        PortBox.Text = viewModel.Port;
        Refresh();
    }

    internal DeviceConfigViewModel ViewModel { get; }

    internal Grid Form => FormGrid;

    internal string Status => StatusText.Text;

    internal bool CanWrite => WriteButton.IsEnabled;

    internal async Task ReadNowAsync()
    {
        ViewModel.Host = HostBox.Text;
        ViewModel.Port = PortBox.Text;
        await ViewModel.ReadAsync();
        BuildForm();
        Refresh();
    }

    internal async Task WriteNowAsync()
    {
        await ViewModel.WriteAsync();
        BuildForm();
        Refresh();
    }

    private async void ReadButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await ReadNowAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async void WriteButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await WriteNowAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private void EditorBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EditorBox.SelectedItem is IDeviceConfigEditor editor && editor.Id != ViewModel.Editor?.Id)
        {
            ViewModel.PickEditor(editor.Id);
            BuildForm();
            Refresh();
        }
    }

    private void Refresh()
    {
        var lines = new List<string>();
        lines.AddRange(ViewModel.ChangeLines().Select(l => "change: " + l));
        lines.AddRange(ViewModel.IssueLines().Select(l => "problem: " + l));
        if (ViewModel.WillDropConnection)
        {
            lines.Add("Writing these values changes the address, so the connection will drop.");
        }

        SummaryText.Text = string.Join(Environment.NewLine, lines);
        StatusText.Text = ViewModel.Message ?? string.Empty;
        WriteButton.IsEnabled = ViewModel.CanWrite;
        ReadButton.IsEnabled = ViewModel.Editor is not null;
    }

    private void BuildForm()
    {
        FormGrid.Children.Clear();
        FormGrid.RowDefinitions.Clear();
        if (ViewModel.Session is null)
        {
            return;
        }

        var row = 0;
        foreach (var control in ViewModel.Fields)
        {
            var id = control.Id;
            FormGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Text = control.Label, Margin = new Thickness(0, 0, 10, 8), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(label, row);
            FormGrid.Children.Add(label);

            FrameworkElement editor;
            switch (control)
            {
                case ToggleControl:
                    var box = new CheckBox { IsChecked = ViewModel.IsOn(id), HorizontalAlignment = HorizontalAlignment.Left };
                    box.Click += (_, _) =>
                    {
                        ViewModel.SetOn(id, box.IsChecked == true);
                        Refresh();
                    };
                    editor = box;
                    break;
                case ChoiceControl choice:
                    var combo = new ComboBox { ItemsSource = choice.Options, SelectedItem = ViewModel.Value(id) };
                    combo.SelectionChanged += (_, _) =>
                    {
                        if (combo.SelectedItem is string option)
                        {
                            ViewModel.Set(id, option);
                            Refresh();
                        }
                    };
                    editor = combo;
                    break;
                default:
                    var text = new TextBox { Text = ViewModel.Value(id) };
                    text.TextChanged += (_, _) =>
                    {
                        ViewModel.Set(id, text.Text);
                        Refresh();
                    };
                    editor = text;
                    break;
            }

            editor.Margin = new Thickness(0, 0, 0, 8);
            Grid.SetRow(editor, row);
            Grid.SetColumn(editor, 1);
            FormGrid.Children.Add(editor);
            row++;
        }
    }
}
