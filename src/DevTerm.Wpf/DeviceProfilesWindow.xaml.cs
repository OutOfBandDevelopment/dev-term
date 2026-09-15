using System.Windows;
using DevTerm.Configuration;

namespace DevTerm.Wpf;

/// <summary>
/// Lets the user pick a saved <see cref="ConnectionProfileStore"/> profile and apply it as the
/// untracked default (<see cref="DevTermConfiguration.SaveLocalProfile"/>) — mirrors the TUI's
/// <c>ConfigureMode</c> "Device Profiles..." menu item at the same scope: applying a profile saves
/// it as the default and asks for a restart, rather than live-swapping the running session's
/// transport (a bigger change — see docs/design/connection-profiles.md's still-open menu-driven
/// switching item).
/// </summary>
public partial class DeviceProfilesWindow : Window
{
    private readonly ConnectionProfileStore _store;

    public DeviceProfilesWindow(ConnectionProfileStore store)
    {
        InitializeComponent();
        _store = store;

        foreach (var name in _store.List())
        {
            ProfilesList.Items.Add(name);
        }
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilesList.SelectedItem is not string name)
        {
            MessageBox.Show(this, "Select a profile first.", "dev-term", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var options = _store.Load(name);
        DevTermConfiguration.SaveLocalProfile(options);
        MessageBox.Show(
            this,
            $"Saved '{ConnectionDescription.For(options)}' as the default profile — restart dev-term to connect with it.",
            "dev-term",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
