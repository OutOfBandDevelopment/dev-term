using System.Windows;
using System.Windows.Input;
using DevTerm.Core.Control;

namespace DevTerm.Wpf;

/// <summary>
/// The WPF half of the instrument picker (see <c>TuiMode.PickFromList</c> for the TUI half): lists the provider's auto-detect,
/// generic and per-profile choices (<see cref="InstrumentPanelProviderExtensions.PickerChoices"/>) and returns whichever the user
/// selects via <see cref="Chosen"/>.
/// </summary>
public partial class InstrumentPickerWindow : Window
{
    public string? Chosen { get; private set; }

    public InstrumentPickerWindow(IInstrumentPanelProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        InitializeComponent();
        WpfTheme.Attach(this);
        Title = provider.PickerTitle;
        ProfileList.ItemsSource = provider.PickerChoices();
        ProfileList.SelectedIndex = 0;
    }

    private void SelectButton_Click(object sender, RoutedEventArgs e) => Commit();

    private void ProfileList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Commit();

    private void Commit()
    {
        if (ProfileList.SelectedItem is string selected)
        {
            Chosen = selected;
            DialogResult = true;
        }
    }
}
