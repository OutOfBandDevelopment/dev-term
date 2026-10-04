using System.Windows;
using System.Windows.Controls;
using DevTerm.Core.Control;

namespace DevTerm.Wpf;

/// <summary>
/// Device-menu entries for control panels contributed by plugins (<see cref="IDevicePanelContribution"/>,
/// docs/design/proposals/plugin-contributed-panels.md): one item each, at the top of the Device menu, enabled by
/// <see cref="RefreshPluginPanelItems"/> the same way the built-in panels are.
/// </summary>
public partial class MainWindow
{
    private readonly List<(IDevicePanelContribution Panel, MenuItem Item)> _pluginPanelItems = [];

    /// <summary>Set by <c>App</c> from the container; builds one Device-menu item per contribution.</summary>
    public IReadOnlyList<IDevicePanelContribution>? PluginPanels
    {
        get => [.. _pluginPanelItems.Select(p => p.Panel)];
        set
        {
            foreach (var (_, item) in _pluginPanelItems)
            {
                ((MenuItem)item.Parent).Items.Remove(item);
            }

            _pluginPanelItems.Clear();
            var menu = (MenuItem)PluginsMenuItem.Parent;
            var position = 0;
            foreach (var contribution in value ?? [])
            {
                var captured = contribution;
                var item = new MenuItem { Header = captured.MenuTitle, IsEnabled = false };
                item.Click += (_, _) => OpenPluginPanel(captured);
                menu.Items.Insert(position++, item);
                _pluginPanelItems.Add((captured, item));
            }
        }
    }

    internal MenuItem? K8055MenuItem => PluginPanelItem("k8055");

    internal MenuItem? BusylightMenuItem => PluginPanelItem("busylight");

    private MenuItem? PluginPanelItem(string id) => _pluginPanelItems.FirstOrDefault(p => p.Panel.Id == id).Item;

    internal ControlPanelWindow OpenK8055ControlPanel() => OpenPluginPanel(_pluginPanelItems.First(p => p.Panel.Id == "k8055").Panel);

    /// <summary>Split from the click handler so tests can drive it without simulating a menu click.</summary>
    internal ControlPanelWindow OpenPluginPanel(IDevicePanelContribution contribution)
    {
        var tab = ActiveWindowTab;
        var structuredSource = contribution.PresenterName is { } name && tab.Tab.Catalog.TryGet(name, out var presenter) ? presenter : null;
        var window = new ControlPanelWindow(contribution.BuildDefinition(), contribution.CreateSurface(tab.Tab.Session), structuredSource)
        {
            Owner = this,
        };
        TrackControlPanel(window, tab);
        window.Show();
        return window;
    }

    private void RefreshPluginPanelItems(WindowTab? tab, bool connected)
    {
        foreach (var (panel, item) in _pluginPanelItems)
        {
            item.IsEnabled = tab is not null && connected
                && panel.IsAvailable(tab.Tab.CliOptions.Transport, tab.Tab.CliOptions.VendorId, tab.Tab.CliOptions.ProductId);
        }
    }
}
