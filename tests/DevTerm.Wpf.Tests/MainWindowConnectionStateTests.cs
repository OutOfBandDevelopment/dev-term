using System.IO;
using System.Windows.Media;
using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Presenters.Text;
using DevTerm.Test.Utilities;

namespace DevTerm.Wpf.Tests;

/// <summary>
/// The main window's connection-state UI: the status bar, the title's disconnected suffix, which
/// Device menu items are enabled, styled error lines, and closing a really-shown window.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class MainWindowConnectionStateTests
{
    private static readonly TimeSpan _pumpTimeout = TimeSpan.FromSeconds(5);

    private static (MainWindow Window, FakeTransport Transport) CreateWindow(CliOptions options)
    {
        var transport = new FakeTransport();
        var ascii = new AsciiPresenter(Microsoft.Extensions.Options.Options.Create(new AsciiPresenterOptions()));
        var window = new MainWindow(new Session(transport, new Pipeline([ascii])), new PresenterCatalog([ascii]), options, IsolatedProfiles.Empty())
        {
            ShowInTaskbar = false,
        };
        return (window, transport);
    }

    private static CliOptions Tcp() => new() { Transport = "tcp", Host = "127.0.0.1", Port = "23", Presenter = ["ascii"] };

    [TestMethod]
    public void Connected_StatusBarSaysSo_TitleHasNoSuffix_OnlyTheMatchingPanelIsEnabled()
    {
        StaTestRunner.Run(async () =>
        {
            var (window, _) = CreateWindow(Tcp());
            await window.ConnectAsync();

            Assert.AreEqual("Connected — tcp://127.0.0.1:23", window.ConnectionStatusText.Text);
            Assert.AreEqual(WpfTheme.ToColor(ActiveTheme.Current[ThemeRole.StatusConnected]), ((SolidColorBrush)window.ConnectionStatusDot.Fill).Color, "The dot is the theme's statusConnected.");
            Assert.DoesNotContain("disconnected", window.Title);
            Assert.IsTrue(window.ScpiMenuItem.IsEnabled, "SCPI makes sense over TCP.");
            Assert.IsFalse(window.K8055MenuItem.IsEnabled, "The K8055 is a HID device.");
            Assert.IsFalse(window.BusylightMenuItem.IsEnabled);
        });
    }

    [TestMethod]
    public void Disconnected_StatusBarTitleAndDeviceMenuAllFollow()
    {
        StaTestRunner.Run(async () =>
        {
            var (window, _) = CreateWindow(Tcp());
            await window.ConnectAsync();

            await window.ToggleConnectionAsync();

            Assert.AreEqual("Disconnected — tcp://127.0.0.1:23", window.ConnectionStatusText.Text);
            Assert.AreEqual(WpfTheme.ToColor(ActiveTheme.Current[ThemeRole.StatusDisconnected]), ((SolidColorBrush)window.ConnectionStatusDot.Fill).Color, "The dot is the theme's statusDisconnected.");
            Assert.EndsWith(" — disconnected", window.Title);
            Assert.IsFalse(window.ScpiMenuItem.IsEnabled, "No panel makes sense while disconnected.");
        });
    }

    [TestMethod]
    public void LostConnection_FlipsTheStatusBarToo()
    {
        StaTestRunner.Run(async () =>
        {
            var (window, transport) = CreateWindow(Tcp());
            await window.ConnectAsync();

            await transport.FailReadAsync(new IOException("socket reset"));

            Assert.IsTrue(StaTestRunner.PumpUntil(() => window.ConnectionStatusText.Text.StartsWith("Disconnected", StringComparison.Ordinal), _pumpTimeout));
            Assert.EndsWith(" — disconnected", window.Title);
        });
    }

    [TestMethod]
    public void AK8055Connection_EnablesOnlyTheK8055Panel()
    {
        StaTestRunner.Run(async () =>
        {
            var (window, _) = CreateWindow(new CliOptions { Transport = "hid", VendorId = 0x10CF, ProductId = 0x5500, Presenter = ["ascii"] });
            await window.ConnectAsync();

            Assert.IsTrue(window.K8055MenuItem.IsEnabled);
            Assert.IsFalse(window.BusylightMenuItem.IsEnabled);
            Assert.IsFalse(window.ScpiMenuItem.IsEnabled, "SCPI is text; HID is fixed-size binary reports.");
        });
    }

    [TestMethod]
    public void ConnectFailure_IsAnErrorLine_StyledApartFromDeviceOutput()
    {
        StaTestRunner.Run(async () =>
        {
            var (window, transport) = CreateWindow(Tcp());
            transport.FailNextOpen(new IOException("connection refused"));

            await window.ConnectAsync();

            var line = window.OutputList.Items.Cast<OutputLine>().Single(l => l.Text.Contains("connection refused", StringComparison.Ordinal));
            Assert.AreEqual(OutputKind.Error, line.Kind);
        });
    }

    /// <summary>
    /// The spec's old open item said closing a really-shown MainWindow under test automation throws
    /// "Cannot ... Close ... while a Window is closing". That was the app's own OnClosing reentrancy
    /// bug, fixed with Dispatcher.Yield; this proves it for a window that's really shown (off-screen,
    /// auto-connected through Loaded), not just constructed.
    /// </summary>
    [TestMethod]
    public void AReallyShownWindow_ClosesCleanly()
    {
        StaTestRunner.Run(async () =>
        {
            var (window, transport) = CreateWindow(Tcp());
            var closed = false;
            window.Closed += (_, _) => closed = true;
            WpfScreenshot.ShowOffScreen(window);
            Assert.IsTrue(StaTestRunner.PumpUntil(() => transport.State == DevTerm.Core.Transports.ConnectionState.Open, _pumpTimeout), "Loaded should auto-connect.");

            window.Close();

            Assert.IsTrue(StaTestRunner.PumpUntil(() => closed, _pumpTimeout), "The window should finish closing.");
            Assert.AreEqual(DevTerm.Core.Transports.ConnectionState.Closed, transport.State);
            await Task.CompletedTask;
        });
    }
}
