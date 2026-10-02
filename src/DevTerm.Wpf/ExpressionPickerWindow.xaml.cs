using System.Windows;
using System.Windows.Controls;
using DevTerm.DeviceManifests.Editing;

namespace DevTerm.Wpf;

/// <summary>
/// The WPF expression picker: a modal over <see cref="ExpressionPickerViewModel"/> with the expression text, a
/// filterable list of the paths it may read (double-click inserts at the caret), the language's functions, live
/// diagnostics and the result against sample data. The Terminal.Gui equivalent is <c>DevTerm.Console.ExpressionPickerDialog</c>.
/// See docs/specs/expression-picker.md.
/// </summary>
public partial class ExpressionPickerWindow : Window
{
    private readonly ExpressionPickerViewModel _viewModel;
    private bool _syncing;

    public ExpressionPickerWindow(ExpressionPickerViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        InitializeComponent();
        WpfTheme.Attach(this);

        foreach (var function in ExpressionPickerViewModel.Functions)
        {
            var captured = function;
            var button = new Button { Content = function.Name, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(10, 2, 10, 2), ToolTip = $"{function.Signature}: {function.Description}" };
            button.Click += (_, _) =>
            {
                SyncCaret();
                _viewModel.InsertFunction(captured);
                ExpressionBox.Focus();
            };
            FunctionPanel.Children.Add(button);
        }

        if (_viewModel.IsChannelList)
        {
            // A channel list has no single result or functions: choosing a value appends it as a channel.
            Title = "dev-term - Channels";
            ExpressionLabel.Text = "Channels (id[:label[:#RRGGBB[:expression]]], separated by ;):";
            FunctionPanel.Visibility = Visibility.Collapsed;
            ResultText.Visibility = Visibility.Collapsed;
            NextSampleButton.Visibility = Visibility.Collapsed;
            PathList.ToolTip = "Double-click a value to add it as a channel";
        }

        _viewModel.Changed += (_, _) => Refresh();
        Refresh();
        ExpressionBox.CaretIndex = _viewModel.CaretIndex;
        Loaded += (_, _) => ExpressionBox.Focus();
    }

    /// <summary>The expression chosen with OK, or null when cancelled.</summary>
    public string? Accepted { get; private set; }

    internal ExpressionPickerViewModel ViewModel => _viewModel;

    private void SyncCaret()
    {
        _viewModel.CaretIndex = ExpressionBox.CaretIndex;
        _viewModel.SelectionLength = ExpressionBox.SelectionLength;
    }

    private void Refresh()
    {
        _syncing = true;
        try
        {
            if (ExpressionBox.Text != _viewModel.Text)
            {
                ExpressionBox.Text = _viewModel.Text;
            }

            ExpressionBox.CaretIndex = _viewModel.CaretIndex;
            DiagnosticsText.Text = _viewModel.Diagnostics;
            if (_viewModel.IsEmpty || (_viewModel.IsValid && _viewModel.Warnings.Count == 0))
            {
                DiagnosticsText.ClearValue(TextBlock.ForegroundProperty);
            }
            else
            {
                DiagnosticsText.SetResourceReference(TextBlock.ForegroundProperty, "DevTerm.Error");
            }

            ResultText.Text = $"Sample result: {_viewModel.ResultText}";
            PathList.ItemsSource = _viewModel.Paths;
        }
        finally
        {
            _syncing = false;
        }
    }

    private void ExpressionBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_syncing)
        {
            _viewModel.Text = ExpressionBox.Text;
        }
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_syncing)
        {
            _viewModel.Filter = FilterBox.Text;
        }
    }

    private void PathList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (PathList.SelectedItem is PickerPath path)
        {
            SyncCaret();
            _viewModel.InsertPath(path);
            ExpressionBox.Focus();
        }
    }

    private void NextSample_Click(object sender, RoutedEventArgs e) => _viewModel.NextSample();

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Accepted = _viewModel.Text;
        DialogResult = true;
    }
}
