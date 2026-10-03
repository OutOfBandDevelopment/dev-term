using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DevTerm.Configuration;

namespace DevTerm.Wpf;

/// <summary>
/// The WPF "Build/Edit Theme..." editor (View &gt; Theme &gt; Build/Edit Theme...): mirrors
/// <c>DevTerm.Console.ThemeBuilderMode.BuildWindow</c> field-for-field, reusing the same
/// framework-agnostic <see cref="ThemeBuilderState"/> for color tracking, live-preview building
/// (<see cref="ActiveTheme.Preview"/>), and saving. Per-role color editing reuses
/// <see cref="ColorPickerWindow"/> (RGB/HSV/hex) instead of a plain hex field, since WPF already has
/// that control. See docs/design/features/theme-builder.md.
/// </summary>
public partial class ThemeBuilderWindow : Window
{
    private const string _overriddenMarker = "● ";
    private const string _unchangedMarker = "  ";

    private readonly ThemeBuilderState _state;
    private readonly string _directory;
    private readonly ThemeRole[] _roles = Enum.GetValues<ThemeRole>();
    private bool _initialized;

    /// <param name="state">The theme being edited - shared with <see cref="ActiveTheme.Preview"/> by the caller.</param>
    /// <param name="themesDirectory">Defaults to the real <see cref="DevTermUserDataPaths.ThemesDirectory"/>; a test passes its own temp directory instead.</param>
    public ThemeBuilderWindow(ThemeBuilderState state, string? themesDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
        _directory = themesDirectory ?? DevTermUserDataPaths.ThemesDirectory;

        InitializeComponent();
        WpfTheme.Attach(this);

        ReportValidationError = message => MessageBox.Show(this, message, "dev-term", MessageBoxButton.OK, MessageBoxImage.Error);
        ConfirmOverwrite = name => MessageBox.Show(
            this,
            $"A theme named '{name}' already exists. Overwrite it?",
            "dev-term",
            MessageBoxButton.YesNo) == MessageBoxResult.Yes;

        NameBox.Text = state.Name;
        PaletteBox.SelectedIndex = state.ChartPalette == ChartPaletteVariant.Dark ? 1 : 0;
        _initialized = true;

        RefreshRoles();
        RefreshWarnings();
    }

    /// <summary>For tests: the state this window edits.</summary>
    internal ThemeBuilderState State => _state;

    /// <summary>
    /// Reports a validation/save error - defaults to a real <see cref="MessageBox"/>; a test replaces
    /// it to assert the error path without a blocking real dialog. Mirrors
    /// <c>DeviceProfilesWindow</c>'s <c>ViewModel.ConfirmOverwrite</c>-style injectable hooks.
    /// </summary>
    internal Action<string> ReportValidationError { get; set; } = _ => { };

    /// <summary>Confirms overwriting an existing theme file by name - defaults to a real <see cref="MessageBox"/>.</summary>
    internal Func<string, bool> ConfirmOverwrite { get; set; } = _ => true;

    private void Preview() => ActiveTheme.Preview(_state.Build());

    private void RefreshRoles()
    {
        var selected = RolesList.SelectedIndex;
        RolesList.ItemsSource = new ObservableCollection<RoleRow>(_roles.Select(role => new RoleRow(role, RowText(role))));
        RolesList.SelectedIndex = selected >= 0 && selected < _roles.Length ? selected : -1;
    }

    private void RefreshWarnings()
    {
        var warnings = _state.ContrastWarnings();
        WarningsText.Text = warnings.Count == 0 ? "No contrast problems." : string.Join('\n', warnings.Take(3));
    }

    private string RowText(ThemeRole role) =>
        (_state.IsOverridden(role) ? _overriddenMarker : _unchangedMarker) + ThemeFile.RoleName(role).PadRight(24) + _state[role].ToHex();

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initialized || string.IsNullOrWhiteSpace(NameBox.Text))
        {
            return;
        }

        _state.Name = NameBox.Text.Trim();
        Preview();
    }

    private void PaletteBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized)
        {
            return;
        }

        _state.ChartPalette = PaletteBox.SelectedIndex == 1 ? ChartPaletteVariant.Dark : ChartPaletteVariant.Light;
        Preview();
    }

    private void EditSelectedRole()
    {
        if (RolesList.SelectedItem is not RoleRow row)
        {
            return;
        }

        var current = _state[row.Role];
        var picker = new ColorPickerWindow(current.R, current.G, current.B) { Owner = this };
        if (picker.ShowDialog() == true)
        {
            _state.Set(row.Role, new ThemeColor(picker.SelectedR, picker.SelectedG, picker.SelectedB));
            RefreshRoles();
            RefreshWarnings();
            Preview();
        }
    }

    private void RolesList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => EditSelectedRole();

    private void EditButton_Click(object sender, RoutedEventArgs e) => EditSelectedRole();

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (RolesList.SelectedItem is not RoleRow row)
        {
            return;
        }

        _state.ResetToSeed(row.Role);
        RefreshRoles();
        RefreshWarnings();
        Preview();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e) => Save();

    /// <summary>Validates, confirms overwrite if needed, and saves - exposed for tests, like <see cref="EditSelectedRole"/>'s callers.</summary>
    internal void Save()
    {
        var name = NameBox.Text.Trim();
        if (!ProfileName.IsValid(name))
        {
            ReportValidationError("Enter a valid theme name.");
            return;
        }

        if (BuiltInThemes.IsReservedName(name))
        {
            ReportValidationError($"'{name}' is reserved for a built-in theme; pick another name.");
            return;
        }

        _state.Name = name;
        var path = Path.Combine(_directory, $"{name}.json");
        if (File.Exists(path) && !ConfirmOverwrite(name))
        {
            return;
        }

        if (_state.Save(path) is { } error)
        {
            ReportValidationError(error);
            return;
        }

        ActiveTheme.UseCatalog(ThemeCatalog.Load(_directory));
        ActiveTheme.Select(name);
        try
        {
            // Throws if this window wasn't shown via ShowDialog() - true in tests that call Save()
            // directly without ever showing the window. See DeviceProfilesWindow's CloseRequested
            // handler for the same pattern.
            DialogResult = true;
        }
        catch (InvalidOperationException)
        {
        }
    }

    /// <summary>
    /// Runs the whole "Build/Edit Theme..." flow: the seed picker, then this window, with live
    /// preview (<see cref="ActiveTheme.Preview"/>) during the edit and <see cref="ActiveTheme.CancelPreview"/>
    /// afterward regardless of outcome (a no-op after Save, which already re-selected the saved theme).
    /// </summary>
    public static void Run(Window? owner, string? themesDirectory = null)
    {
        var seedPicker = new ThemeSeedPickerWindow { Owner = owner };
        if (seedPicker.ShowDialog() != true || seedPicker.ChosenTheme is not { } seed || seedPicker.ChosenName is not { } name)
        {
            return;
        }

        var state = new ThemeBuilderState(seed, name);
        ActiveTheme.Preview(state.Build());
        try
        {
            new ThemeBuilderWindow(state, themesDirectory) { Owner = owner }.ShowDialog();
        }
        finally
        {
            ActiveTheme.CancelPreview();
        }
    }

    private sealed record RoleRow(ThemeRole Role, string Row);
}
