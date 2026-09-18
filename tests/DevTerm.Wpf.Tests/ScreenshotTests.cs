using System.IO;
using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Presenters.Text;
using Microsoft.Extensions.Options;

namespace DevTerm.Wpf.Tests;

/// <summary>
/// Generates the real screenshots embedded across <c>docs/user-guide/</c>'s flow pages
/// (<c>connecting.md</c>, <c>managing-profiles.md</c>, <c>sending-and-receiving.md</c>,
/// <c>connect-disconnect.md</c>) — a real, laid-out <see cref="MainWindow"/>/
/// <see cref="DeviceProfilesWindow"/> rendered to PNG via <see cref="WpfScreenshot"/>. These are
/// real automated tests (each asserts the file exists and is a real, non-trivial image, not a
/// blank/near-empty one) that double as doc generation and as a quick visual regression check —
/// re-run this class (<c>dotnet test --filter ClassName~ScreenshotTests</c>) and diff the PNGs
/// under <c>docs/user-guide/images/</c> whenever a screen's layout changes, re-embedding the
/// refreshed images on whichever doc page references them.
///
/// <see cref="MainWindow"/>'s screenshots drive its real <c>Loaded</c>-triggered auto-connect via
/// <see cref="WpfScreenshot.ShowOffScreen"/> rather than calling <c>ConnectAsync</c> directly —
/// calling both opens the session twice concurrently (see <c>CLAUDE.md</c>'s constraint on this).
/// </summary>
[TestCategory("UNIT")]
[TestClass]
[DoNotParallelize]
public sealed class ScreenshotTests
{
    private static readonly string ImagesDirectory = Path.Combine(FindRepoRoot(), "docs", "user-guide", "images");
    private static readonly TimeSpan PumpTimeout = TimeSpan.FromSeconds(5);

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DevTerm.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException($"Could not find the repo root (DevTerm.slnx) above '{AppContext.BaseDirectory}'.");
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "devterm-screenshot-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        return path;
    }

    private static void AssertRealImage(string path)
    {
        Assert.IsTrue(File.Exists(path), $"Expected a screenshot at '{path}'.");
        Assert.IsGreaterThan(1000L, new FileInfo(path).Length, "Expected a real rendered image, not a blank/near-empty file.");
    }

    private static (MainWindow Window, FakeTransport Transport) CreateMainWindow()
    {
        var transport = new FakeTransport();
        var presenter = new AsciiPresenter(Options.Create(new AsciiPresenterOptions()));
        var session = new Session(transport, new Pipeline([presenter]));
        var window = new MainWindow(session, new PresenterCatalog([presenter]), new CliOptions { Transport = "tcp", Host = "192.168.0.107", TcpPort = 23, Parser = "ascii" });
        return (window, transport);
    }

    [TestMethod]
    public void MainWindow_Connected_IsCaptured()
    {
        StaTestRunner.Run(async () =>
        {
            var (window, transport) = CreateMainWindow();
            WpfScreenshot.ShowOffScreen(window);

            StaTestRunner.PumpUntil(() => window.Title.Contains("TCP"), PumpTimeout);
            await transport.PushIncomingAsync("ID TEK/2230,V81.1,VERS:14\r"u8.ToArray());
            StaTestRunner.PumpUntil(() => window.OutputList.Items.Count > 0, PumpTimeout);

            WpfScreenshot.Save(window, Path.Combine(ImagesDirectory, "wpf-main-window-connected.png"));

            AssertRealImage(Path.Combine(ImagesDirectory, "wpf-main-window-connected.png"));
        });
    }

    [TestMethod]
    public void MainWindow_Disconnected_IsCaptured()
    {
        StaTestRunner.Run(async () =>
        {
            var (window, _) = CreateMainWindow();
            WpfScreenshot.ShowOffScreen(window);

            StaTestRunner.PumpUntil(() => window.Title.Contains("TCP"), PumpTimeout);
            await window.ToggleConnectionAsync();
            StaTestRunner.PumpUntil(() => window.OutputList.Items.Count > 0, PumpTimeout);

            var path = Path.Combine(ImagesDirectory, "wpf-main-window.png");
            WpfScreenshot.Save(window, path);

            AssertRealImage(path);
        });
    }

    [TestMethod]
    public void DeviceProfilesWindow_SerialTransport_IsCaptured()
    {
        var directory = CreateTempDirectory();
        try
        {
            StaTestRunner.Run(async () =>
            {
                var initial = new CliOptions
                {
                    Transport = "serial",
                    Port = "COM3",
                    Baud = 9600,
                    DataBits = 8,
                    Presenter = ["ascii"],
                    Description = "Tektronix 2230 bench scope",
                };
                var window = new DeviceProfilesWindow(new ConnectionProfileStore(directory), initial);
                WpfScreenshot.ShowOffScreen(window);

                var path = Path.Combine(ImagesDirectory, "wpf-device-profiles-serial.png");
                WpfScreenshot.Save(window, path);

                AssertRealImage(path);
                await Task.CompletedTask;
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void DeviceProfilesWindow_TcpTransport_IsCaptured()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            store.Save("tek2230", new CliOptions { Transport = "tcp", Host = "192.168.0.107", TcpPort = 23, Presenter = ["ascii"] });
            store.Save("tds2024", new CliOptions { Transport = "tcp", Host = "192.168.0.110", TcpPort = 23, Presenter = ["ascii"] });

            StaTestRunner.Run(async () =>
            {
                var initial = new CliOptions { Transport = "tcp", Host = "192.168.0.107", TcpPort = 23, Description = "Tektronix 2230 bench scope" };
                var window = new DeviceProfilesWindow(store, initial);
                WpfScreenshot.ShowOffScreen(window);

                var path = Path.Combine(ImagesDirectory, "wpf-device-profiles-tcp.png");
                WpfScreenshot.Save(window, path);

                AssertRealImage(path);
                await Task.CompletedTask;
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void DeviceProfilesWindow_HidTransport_IsCaptured()
    {
        var directory = CreateTempDirectory();
        try
        {
            StaTestRunner.Run(async () =>
            {
                var initial = new CliOptions { Transport = "hid", HidVendorId = 4216, HidProductId = 63560, Presenter = ["hex"] };
                var window = new DeviceProfilesWindow(new ConnectionProfileStore(directory), initial);
                WpfScreenshot.ShowOffScreen(window);

                var path = Path.Combine(ImagesDirectory, "wpf-device-profiles-hid.png");
                WpfScreenshot.Save(window, path);

                AssertRealImage(path);
                await Task.CompletedTask;
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
