using DevTerm.Configuration;
using Terminal.Gui.Views;

namespace DevTerm.Console;

/// <summary>
/// The TUI's View &gt; Theme menu: one item per <see cref="ThemeCatalog.SelectionNames"/> entry
/// (light, dark, system, then the user's theme files), the current selection marked with "●".
/// Picking one calls <see cref="ActiveTheme.Select"/>, which persists it and raises
/// <see cref="ActiveTheme.Changed"/>; the main window's handler re-applies the theme live.
/// </summary>
internal sealed class TuiThemeMenu
{
    private const string _currentMarker = "● ";
    private const string _otherMarker = "  ";

    private readonly Dictionary<string, MenuItem> _items = new(StringComparer.OrdinalIgnoreCase);

    public TuiThemeMenu(Action<string> reportProblem)
    {
        ArgumentNullException.ThrowIfNull(reportProblem);
        foreach (var name in ActiveTheme.Catalog.SelectionNames)
        {
            _items[name] = new MenuItem(Title(name), string.Empty, () =>
            {
                if (ActiveTheme.Select(name) is { } problem)
                {
                    reportProblem(problem);
                }
            });
        }

        MenuBarItem = new MenuBarItem("_View", [new MenuItem("_Theme", string.Empty, new Menu([.. _items.Values]))]);
    }

    /// <summary>The top-level "_View" entry for the menu bar.</summary>
    public MenuBarItem MenuBarItem { get; }

    /// <summary>Each theme's menu item by selection name - for tests to invoke, and to read which is marked current.</summary>
    public IReadOnlyDictionary<string, MenuItem> Items => _items;

    /// <summary>Re-marks the current selection (after <see cref="ActiveTheme.Changed"/>).</summary>
    public void Refresh()
    {
        foreach (var (name, item) in _items)
        {
            item.Title = Title(name);
        }
    }

    private static string Title(string name) =>
        (string.Equals(name, ActiveTheme.Selection, StringComparison.OrdinalIgnoreCase) ? _currentMarker : _otherMarker) + Label(name);

    private static string Label(string name) => name switch
    {
        BuiltInThemes.LightName => "_Light",
        BuiltInThemes.DarkName => "_Dark",
        BuiltInThemes.SystemName => "_System (follow the OS)",
        _ => name,
    };
}
