using System.Windows.Controls;
using DevTerm.Configuration;
using DevTerm.Core.Control;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Devices.K8055;
using DevTerm.Presenters.Text;
using DevTerm.Test.Utilities;
using DevTerm.UiDefinitions;

namespace DevTerm.Wpf.Tests;

/// <summary>Device-menu entries for plugin-contributed panels (docs/design/proposals/plugin-contributed-panels.md).</summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class MainWindowPluginPanelsTests
{
    private sealed class SampleContribution : IDevicePanelContribution
    {
        public string Id => "sample";

        public string MenuTitle => "_Sample Panel...";

        public string? PresenterName => null;

        public bool IsAvailable(string transport, int vendorId, int productId) => transport == "hid" && vendorId == 0x10CF;

        public UiDefinition BuildDefinition() => K8055UiDefinition.Build();

        public IControlSurface CreateSurface(Session session) => new K8055ControlSurface(session);
    }

    private static MainWindow CreateWindow(CliOptions options)
    {
        var presenter = new AsciiPresenter(Microsoft.Extensions.Options.Options.Create(new AsciiPresenterOptions()));
        options.Parser ??= presenter.Name;
        return new MainWindow(new Session(new FakeTransport(), new Pipeline([presenter])), new PresenterCatalog([presenter]), options, IsolatedProfiles.Empty())
        {
            ShowInTaskbar = false,
            PluginPanels = [new SampleContribution()],
        };
    }

    private static MenuItem? SampleItem(MainWindow window) => ((MenuItem)window.PluginsMenuItem.Parent).Items.OfType<MenuItem>().FirstOrDefault(i => (string)i.Header == "_Sample Panel...");

    [TestMethod]
    public void ContributedPanel_IsAddedToTheDeviceMenu_AheadOfPlugins()
    {
        StaTestRunner.Run(() =>
        {
            var window = CreateWindow(new CliOptions { Transport = "hid", VendorId = 0x10CF, ProductId = 0x5500 });
            var menu = (MenuItem)window.PluginsMenuItem.Parent;

            Assert.IsNotNull(SampleItem(window));
            Assert.IsLessThan(menu.Items.IndexOf(window.PluginsMenuItem), menu.Items.IndexOf(SampleItem(window)!));
            Assert.IsFalse(SampleItem(window)!.IsEnabled, "Disabled until connected.");
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void ContributedPanel_OpensAControlPanelWindowOverTheActiveSession()
    {
        StaTestRunner.Run(async () =>
        {
            var window = CreateWindow(new CliOptions { Transport = "hid", VendorId = 0x10CF, ProductId = 0x5500 });
            window.Show();
            await window.ConnectAsync();

            Assert.IsTrue(SampleItem(window)!.IsEnabled);
            var panel = window.OpenPluginPanel(window.PluginPanels![0]);

            Assert.AreEqual(1, window.OpenControlPanels.Count);
            Assert.IsTrue(panel.IsVisible);
        });
    }

    [TestMethod]
    public void ContributedPanel_StaysDisabled_WhenTheConnectionDoesNotSuitIt()
    {
        StaTestRunner.Run(async () =>
        {
            var window = CreateWindow(new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "23" });
            window.Show();
            await window.ConnectAsync();

            Assert.IsFalse(SampleItem(window)!.IsEnabled);
        });
    }
}
