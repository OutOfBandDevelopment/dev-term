using System.Windows.Controls;
using DevTerm.Configuration;

namespace DevTerm.Wpf;

/// <summary>
/// View &gt; Theme: one item per <see cref="ThemeCatalog.SelectionNames"/> entry (light, dark,
/// system, then the user's theme files), the current selection checked. Picking one calls
/// <see cref="ActiveTheme.Select"/>, which saves it as the app preference and raises
/// <see cref="ActiveTheme.Changed"/>; <see cref="WpfTheme"/> re-themes every open window live.
/// </summary>
public partial class MainWindow
{
    /// <summary>Each theme's menu item by selection name - for tests.</summary>
    internal IReadOnlyDictionary<string, MenuItem> ThemeMenuItems => _themeMenuItems;

    private readonly Dictionary<string, MenuItem> _themeMenuItems = new(StringComparer.OrdinalIgnoreCase);

    private void BuildThemeMenu()
    {
        foreach (var problem in ActiveTheme.StartupProblems)
        {
            AppendOutput(problem, OutputKind.Status);
        }

        foreach (var name in ActiveTheme.Catalog.SelectionNames)
        {
            var item = new MenuItem { Header = MenuLabel(name), IsCheckable = false };
            item.Click += (_, _) => SelectTheme(name);
            _themeMenuItems[name] = item;
            ThemeMenuItem.Items.Add(item);
        }

        RefreshThemeMenu();
        ActiveTheme.Changed += OnThemeChanged;
        Closed += (_, _) => ActiveTheme.Changed -= OnThemeChanged;
    }

    /// <summary>What a theme menu item's click does - exposed for tests, like <see cref="ConnectAsync"/>.</summary>
    internal void SelectTheme(string name)
    {
        if (ActiveTheme.Select(name) is { } problem)
        {
            AppendOutput(problem, OutputKind.Status);
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess())
        {
            RefreshThemeMenu();
        }
        else
        {
            Dispatcher.BeginInvoke(RefreshThemeMenu);
        }
    }

    private void RefreshThemeMenu()
    {
        foreach (var (name, item) in _themeMenuItems)
        {
            item.IsChecked = string.Equals(name, ActiveTheme.Selection, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string MenuLabel(string name) => name switch
    {
        BuiltInThemes.LightName => "_Light",
        BuiltInThemes.DarkName => "_Dark",
        BuiltInThemes.SystemName => "_System (follow Windows)",
        _ => name.Replace("_", "__", StringComparison.Ordinal),
    };
}
