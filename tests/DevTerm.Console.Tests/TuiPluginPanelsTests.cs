using DevTerm.Configuration;
using DevTerm.Core.Control;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Devices.K8055;
using DevTerm.Presenters.Text;
using DevTerm.Test.Utilities;
using DevTerm.UiDefinitions;
using Microsoft.Extensions.Options;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace DevTerm.Console.Tests;

/// <summary>Device-menu entries for plugin-contributed panels (docs/design/proposals/plugin-contributed-panels.md).</summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class TuiPluginPanelsTests
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

    private static MenuItem? FindItem(View window)
    {
        var bar = window.SubViews.OfType<MenuBar>().Single();
        var device = bar.SubViews.OfType<MenuBarItem>().Single(i => i.Title.Replace("_", string.Empty, StringComparison.Ordinal) == "Device");
        return device.PopoverMenu!.SubViews.OfType<Menu>().SelectMany(m => m.SubViews.OfType<MenuItem>()).FirstOrDefault(i => i.Title == "_Sample Panel...");
    }

    [TestMethod]
    public async Task ContributedPanel_AppearsInTheDeviceMenu_EnabledOnlyForAMatchingConnection()
    {
        foreach (var (options, expected) in new[]
        {
            (new CliOptions { Transport = "hid", VendorId = 0x10CF, ProductId = 0x5500 }, true),
            (new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "23" }, false),
        })
        {
            var presenter = new AsciiPresenter(Options.Create(new AsciiPresenterOptions()));
            options.Parser ??= presenter.Name;
            var session = new Session(new FakeTransport(), new Pipeline([presenter]));
            await session.OpenAsync(TestContext.CancellationToken);

            TuiTestRunner.RunHeadlessApp(app =>
            {
                var parts = TuiMode.BuildWindow(app, session, new PresenterCatalog([presenter]), options, TuiTestRunner.EmptyProfiles(), panels: [new SampleContribution()]);
                var item = FindItem(parts.Window);

                Assert.IsNotNull(item, "The contributed panel should be listed under Device.");
                Assert.AreEqual(expected, item.Enabled);
            });

            await session.CloseAsync(TestContext.CancellationToken);
        }
    }

    public TestContext TestContext { get; set; } = null!;
}
