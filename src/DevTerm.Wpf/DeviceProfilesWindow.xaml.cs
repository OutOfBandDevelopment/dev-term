using System.Windows;
using DevTerm.Configuration;
using Microsoft.Win32;

namespace DevTerm.Wpf;

/// <summary>
/// A full connection editor — pick a saved profile to load, edit any field by hand, save it under
/// a name, import/export a profile as a standalone JSON file, or connect with the current fields.
/// All of that logic lives in <see cref="ConnectionEditorViewModel"/>, shared with the TUI's
/// <c>ConfigureMode</c>: this window is just XAML bound to it (<c>Command="{Binding ...}"</c>), no
/// business logic in code-behind — the one exception is the native file-browse dialog, which has
/// no pure-binding equivalent. See docs/design/connection-profiles.md.
/// </summary>
public partial class DeviceProfilesWindow : Window
{
    public ConnectionEditorViewModel ViewModel { get; }

    /// <summary>
    /// Set once the user presses Connect with fields that validate; <see langword="null"/> if they
    /// close the window instead. What "Connect" means depends on the caller: at startup, with no
    /// valid configuration yet, it's used directly to build the DI host and connect immediately
    /// (see <see cref="App"/>). From <see cref="MainWindow"/>'s "Device Profiles..." menu item
    /// (already connected), it's saved as the default profile and a restart is requested instead —
    /// see docs/design/connection-profiles.md's note on why this doesn't live-swap the running
    /// session's transport.
    /// </summary>
    public CliOptions? Result => ViewModel.Result;

    public DeviceProfilesWindow(ConnectionProfileStore store, CliOptions initial, string? statusText = null)
    {
        InitializeComponent();
        ViewModel = new ConnectionEditorViewModel(store, initial, statusText);
        DataContext = ViewModel;
        ViewModel.CloseRequested += (_, _) =>
        {
            try
            {
                // Setting DialogResult both closes the window and makes ShowDialog() return true -
                // both real callers (App/MainWindow) need that. It throws if this window wasn't
                // actually shown via ShowDialog() though, which is exactly the case in tests that
                // drive ViewModel.ConnectCommand directly without ever showing the window (the same
                // "don't Show() a window under direct test" convention MainWindowTests already
                // follows, just hitting WPF's dialog-specific version of it) - ViewModel.Result is
                // already set correctly by that point regardless, so there's nothing else to do.
                DialogResult = true;
            }
            catch (InvalidOperationException)
            {
            }
        };
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "dev-term connection profile (*.json)|*.json" };
        if (dialog.ShowDialog(this) == true)
        {
            ViewModel.ImportExportPath = dialog.FileName;
        }
    }
}
