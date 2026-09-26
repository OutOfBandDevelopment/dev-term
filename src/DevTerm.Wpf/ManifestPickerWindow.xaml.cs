using System.IO;
using System.Windows;
using System.Windows.Input;
using DevTerm.Core.Sessions;
using DevTerm.DeviceManifests;

namespace DevTerm.Wpf;

/// <summary>
/// The WPF "Device > Device Manifest..." picker (see <c>DevTerm.Console.ManifestPanelMode</c> for
/// the TUI's): lists the discovered manifests (the user's and installed ones), or takes a typed or
/// browsed path to a manifest file, folder, or <c>.zip</c>, and loads the choice with
/// <see cref="DeviceManifestLoader"/>. A manifest that fails to load is reported inline and the
/// picker stays open (no modal message box), so the user can pick another.
/// </summary>
public partial class ManifestPickerWindow : Window
{
    public ManifestPickerWindow(IReadOnlyList<ManifestEntry> entries)
    {
        InitializeComponent();
        ManifestList.ItemsSource = entries;
        if (entries.Count > 0)
        {
            ManifestList.SelectedIndex = 0;
        }
    }

    /// <summary>The loaded manifest, once one was picked and loaded successfully.</summary>
    public DeviceManifest? Chosen { get; private set; }

    /// <summary>
    /// Opens <paramref name="manifest"/>'s panel on <paramref name="session"/> as an ordinary
    /// non-modal <see cref="ControlPanelWindow"/>, with the manifest's reply presenter bound into the
    /// session until that window closes (<see cref="ManifestPanel"/>).
    /// </summary>
    public static ControlPanelWindow OpenPanel(Window? owner, Session session, DeviceManifest manifest)
    {
        var panel = ManifestPanel.Attach(session, manifest);
        var window = new ControlPanelWindow(panel.Definition, panel.Surface, panel.Presenter) { Owner = owner };
        window.Closed += (_, _) => panel.Dispose();
        window.Show();
        return window;
    }

    /// <summary>Loads the typed path when there is one, else the selected entry; true (and <see cref="Chosen"/> set) when it loaded.</summary>
    internal bool TryLoadChoice()
    {
        var path = !string.IsNullOrWhiteSpace(PathBox.Text)
            ? PathBox.Text.Trim().Trim('"')
            : (ManifestList.SelectedItem as ManifestEntry)?.Path;
        if (path is null)
        {
            ErrorText.Text = "Pick a manifest, or enter a path to one.";
            return false;
        }

        try
        {
            var manifest = DeviceManifestLoader.Load(path);

            // Compiles its response patterns now, so a bad regex is reported here, not on open.
            _ = new ManifestReplyPresenter(manifest);
            Chosen = manifest;
            ErrorText.Text = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException or System.Text.Json.JsonException or InvalidDataException)
        {
            ErrorText.Text = $"Couldn't load '{path}': {ex.Message}";
            return false;
        }
    }

    private void Commit()
    {
        if (TryLoadChoice())
        {
            DialogResult = true;
        }
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e) => Commit();

    private void ManifestList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Commit();

    private void BrowseFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Device manifests (*.json;*.zip)|*.json;*.zip|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) == true)
        {
            PathBox.Text = dialog.FileName;
        }
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Folder containing device.json" };
        if (dialog.ShowDialog(this) == true)
        {
            PathBox.Text = dialog.FolderName;
        }
    }
}
