using System.Windows;
using System.Windows.Input;
using DevTerm.Devices.Scpi;

namespace DevTerm.Wpf;

/// <summary>
/// The WPF half of the SCPI instrument picker (see <c>TuiMode.PickFromList</c> for the TUI half):
/// lists <see cref="ScpiProfileCatalog.All"/> plus the two synthetic choices, "Auto-detect (*IDN?)"
/// and "Generic (manual)", and returns whichever the user selects via <see cref="Chosen"/>.
/// </summary>
public partial class ScpiInstrumentPickerWindow : Window
{
    public const string AutoDetectChoice = ScpiProfileCatalog.AutoDetectChoiceName;
    public static string GenericChoice => ScpiProfileCatalog.Generic.Name;

    public string? Chosen { get; private set; }

    public ScpiInstrumentPickerWindow()
    {
        InitializeComponent();
        WpfTheme.Attach(this);

        var items = new List<string> { AutoDetectChoice, GenericChoice };
        items.AddRange(ScpiProfileCatalog.All.Select(p => p.Name));
        ProfileList.ItemsSource = items;
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
