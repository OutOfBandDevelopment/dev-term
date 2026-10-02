using System.Windows;
using System.Windows.Input;
using DevTerm.Configuration;

namespace DevTerm.Wpf;

/// <summary>
/// The WPF "Build/Edit Theme..." flow's first step: pick a seed (a built-in or an existing user
/// theme) and a name for the new theme, before <see cref="ThemeBuilderWindow"/> opens on it. Mirrors
/// <c>DevTerm.Console.ThemeBuilderMode.PickSeed</c> - see that type's doc comment for the shared
/// rationale (<see cref="ThemeBuilderState"/> is framework-agnostic; only the rendering differs).
/// </summary>
public partial class ThemeSeedPickerWindow : Window
{
    private readonly IReadOnlyList<SeedOption> _options;

    public ThemeSeedPickerWindow()
    {
        InitializeComponent();
        WpfTheme.Attach(this);
        _options = ActiveTheme.Catalog.SelectionNames.Select(name => new SeedOption(name, SeedLabel(name))).ToList();
        SeedList.ItemsSource = _options;
        if (_options.Count > 0)
        {
            SeedList.SelectedIndex = 0;
            NameBox.Text = DefaultName(_options[0].Label);
        }
    }

    /// <summary>The picked seed theme, once <see cref="Window.DialogResult"/> is true.</summary>
    public DevTermTheme? ChosenTheme { get; private set; }

    /// <summary>The new theme's name, once <see cref="Window.DialogResult"/> is true.</summary>
    public string? ChosenName { get; private set; }

    /// <summary>What Create/double-click does - exposed for tests, like <see cref="ThemeBuilderWindow.Save"/>.</summary>
    internal void Commit()
    {
        if (SeedList.SelectedItem is not SeedOption option)
        {
            return;
        }

        ChosenTheme = ActiveTheme.Catalog.Resolve(option.Name, out _, ActiveTheme.PrefersDark);
        ChosenName = string.IsNullOrWhiteSpace(NameBox.Text) ? DefaultName(option.Label) : NameBox.Text.Trim();
        try
        {
            // Throws if this window wasn't shown via ShowDialog() - true in tests that call Commit()
            // directly. See DeviceProfilesWindow's CloseRequested handler for the same pattern.
            DialogResult = true;
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void CreateButton_Click(object sender, RoutedEventArgs e) => Commit();

    private void SeedList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Commit();

    private static string SeedLabel(string name) => name switch
    {
        BuiltInThemes.LightName => "Light",
        BuiltInThemes.DarkName => "Dark",
        BuiltInThemes.SystemName => "System (follow Windows)",
        _ => name,
    };

    private static string DefaultName(string seedLabel) => $"{seedLabel} copy";

    private sealed record SeedOption(string Name, string Label);
}
