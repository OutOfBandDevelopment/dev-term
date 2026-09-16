using System.Text;
using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Presenters.Text;
using Microsoft.Extensions.Options;
using Terminal.Gui.App;

namespace DevTerm.Console.Tests;

/// <summary>
/// Generates the real screenshots embedded in <c>docs/user-guide/</c> — a real, headlessly-rendered
/// <see cref="ConfigureMode"/>/<see cref="TuiMode"/> window rendered to PNG via
/// <see cref="TuiScreenshot"/> (each cell's actual foreground/background color, not a plain-text
/// dump), the same "real captured output from the actual built app" convention
/// <c>docs/user-guide/README.md</c> documents. A plain-text <see cref="TuiTestRunner.DumpBuffer"/>
/// capture is kept alongside each PNG purely as a cheap, diffable regression signal — the PNG is
/// what actually gets embedded in docs. These are real automated tests (each asserts the text dump
/// contains the fields/labels expected for that state) that double as doc generation. Re-run this
/// class (<c>dotnet test --filter ClassName~ScreenshotTests</c>) and re-embed the refreshed PNGs on
/// the relevant doc page whenever a screen's layout changes.
/// </summary>
[TestCategory("UNIT")]
[TestClass]
[DoNotParallelize]
public sealed class ScreenshotTests
{
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

    private static readonly string ImagesDirectory = Path.Combine(FindRepoRoot(), "docs", "user-guide", "images");
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    private static string CreateTempProfilesDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "devterm-configuremode-screenshots-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string CaptureConfigureMode(CliOptions initial, ConnectionProfileStore store, string baseName)
    {
        Application.Init("dotnet");
        string dump;
        try
        {
            var parts = ConfigureMode.BuildWindow(initial, validationError: null, store);
            var token = Application.Begin(parts.Window);
            Application.LayoutAndDraw(true);

            try
            {
                dump = TuiTestRunner.DumpBuffer();
                Directory.CreateDirectory(ImagesDirectory);
                TuiScreenshot.Save(Path.Combine(ImagesDirectory, baseName + ".png"));
            }
            finally
            {
                Application.End(token);
            }
        }
        finally
        {
            Application.Shutdown();
        }

        File.WriteAllText(Path.Combine(ImagesDirectory, baseName + ".txt"), dump);
        return dump;
    }

    [TestMethod]
    public void ConfigureMode_SerialTransport_IsCaptured()
    {
        var directory = CreateTempProfilesDirectory();
        try
        {
            var initial = new CliOptions { Transport = "serial", Port = "COM3", Baud = 9600, Presenter = "ascii", Description = "Tektronix 2230 bench scope" };
            var dump = CaptureConfigureMode(initial, new ConnectionProfileStore(directory), "tui-configure-serial");

            StringAssert.Contains(dump, "Transport:");
            StringAssert.Contains(dump, "Serial port:");
            StringAssert.Contains(dump, "COM3");
            StringAssert.Contains(dump, "Baud:");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ConfigureMode_TcpTransport_IsCaptured()
    {
        var directory = CreateTempProfilesDirectory();
        try
        {
            var initial = new CliOptions { Transport = "tcp", Host = "192.168.0.107", TcpPort = 23, Presenter = "ascii", Description = "Tektronix 2230 bench scope" };
            var dump = CaptureConfigureMode(initial, new ConnectionProfileStore(directory), "tui-configure-tcp");

            StringAssert.Contains(dump, "TCP host:");
            StringAssert.Contains(dump, "192.168.0.107");
            StringAssert.Contains(dump, "Port:");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ConfigureMode_HidTransport_IsCaptured()
    {
        var directory = CreateTempProfilesDirectory();
        try
        {
            var initial = new CliOptions { Transport = "hid", HidVendorId = 4216, HidProductId = 63560, Presenter = "hex" };
            var dump = CaptureConfigureMode(initial, new ConnectionProfileStore(directory), "tui-configure-hid");

            StringAssert.Contains(dump, "HID vendor ID");
            StringAssert.Contains(dump, "4216");
            StringAssert.Contains(dump, "Product ID:");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static (Session Session, FakeTransport Transport, IPresenter Presenter) CreateSession()
    {
        var transport = new FakeTransport();
        var presenter = new AsciiPresenter(Options.Create(new AsciiPresenterOptions()));
        var session = new Session(transport, new Pipeline([presenter]));
        return (session, transport, presenter);
    }

    [TestMethod]
    public async Task TuiMode_ConnectedEmpty_IsCaptured()
    {
        var (session, _, presenter) = CreateSession();
        await session.OpenAsync();
        var cliOptions = new CliOptions { Transport = "tcp", Host = "192.168.0.107", TcpPort = 23, Presenter = "ascii" };

        string dump = "";
        TuiTestRunner.RunHeadless(session, presenter, cliOptions, _ =>
        {
            dump = TuiTestRunner.DumpBuffer();
            Directory.CreateDirectory(ImagesDirectory);
            TuiScreenshot.Save(Path.Combine(ImagesDirectory, "tui-main-connected.png"));
        });

        await session.CloseAsync();

        StringAssert.Contains(dump, "TCP 192.168.0.107:23");
        StringAssert.Contains(dump, "Send:");
    }

    [TestMethod]
    public async Task TuiMode_TypingACommand_IsCaptured()
    {
        var (session, _, presenter) = CreateSession();
        await session.OpenAsync();
        var cliOptions = new CliOptions { Transport = "tcp", Host = "192.168.0.107", TcpPort = 23, Presenter = "ascii" };

        string dump = "";
        TuiTestRunner.RunHeadless(session, presenter, cliOptions, parts =>
        {
            // Sets the field directly rather than going through TuiTestRunner.TypeText's key
            // injector: that injector is known to degrade once several Init/Shutdown cycles have
            // already run earlier in the same test process (see TuiModeTests.CtrlQ_RequestsStop's
            // doc comment for the same class of fragility) — confirmed the hard way here, where
            // this test's own use of TypeText left the injector unable to reach TuiModeTests'
            // SendField in a later test when both ran in the same process. A screenshot doesn't
            // need real key routing, just the visible end state.
            parts.SendField.Text = "ID?";
            Terminal.Gui.App.Application.LayoutAndDraw(true);
            dump = TuiTestRunner.DumpBuffer();
            TuiScreenshot.Save(Path.Combine(ImagesDirectory, "tui-main-typing.png"));
        });

        await session.CloseAsync();

        StringAssert.Contains(dump, "Send: ID?");
    }

    [TestMethod]
    public async Task TuiMode_Disconnected_IsCaptured()
    {
        var (session, _, presenter) = CreateSession();
        await session.OpenAsync();
        var cliOptions = new CliOptions { Transport = "tcp", Host = "192.168.0.107", TcpPort = 23, Presenter = "ascii" };

        TuiTestRunner.RunWithLoop(session, presenter, cliOptions, parts =>
        {
            TuiMode.ToggleConnectionAsync(session, cliOptions, parts.ConnectMenuItem, parts.SendField, _ => { }).GetAwaiter().GetResult();

            var disconnected = TuiTestRunner.WaitUntilOnLoop(() => parts.ConnectMenuItem.Title == "_Connect", WaitTimeout);
            Assert.IsTrue(disconnected, "Expected the menu item's title to flip to _Connect after disconnecting.");

            TuiTestRunner.InvokeOnLoop(() =>
            {
                TuiScreenshot.Save(Path.Combine(ImagesDirectory, "tui-main-disconnected.png"));
                return true;
            });
        });
    }

    [TestMethod]
    public async Task TuiMode_AfterReplyArrives_IsCaptured()
    {
        var (session, transport, presenter) = CreateSession();
        await session.OpenAsync();
        var cliOptions = new CliOptions { Transport = "tcp", Host = "192.168.0.107", TcpPort = 23, Presenter = "ascii" };

        string dump = "";
        TuiTestRunner.RunWithLoop(session, presenter, cliOptions, parts =>
        {
            transport.PushIncomingAsync(Encoding.ASCII.GetBytes("ID TEK/2230,V81.1,VERS:14\r")).GetAwaiter().GetResult();

            var appeared = TuiTestRunner.WaitUntilOnLoop(() => parts.Output.Text.Length > 0, WaitTimeout);
            Assert.IsTrue(appeared, "Expected the decoded reply to appear via the real Session pull loop.");

            dump = TuiTestRunner.InvokeOnLoop(TuiTestRunner.DumpBuffer);
            TuiTestRunner.InvokeOnLoop(() =>
            {
                TuiScreenshot.Save(Path.Combine(ImagesDirectory, "tui-main-after-reply.png"));
                return true;
            });
        });

        await session.CloseAsync();

        StringAssert.Contains(dump, "[ascii] ID TEK/2230,V81.1,VERS:14");
    }
}
