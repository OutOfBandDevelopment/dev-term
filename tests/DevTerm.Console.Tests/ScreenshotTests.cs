using System.Text;
using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Logging.Playback;
using DevTerm.Presenters.Text;
using DevTerm.Test.Utilities;
using Microsoft.Extensions.Options;

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
[TestCategory(TestCategories.Unit)]
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

    private static readonly string _imagesDirectory = Path.Combine(FindRepoRoot(), "docs", "user-guide", "images");
    private static readonly TimeSpan _waitTimeout = TimeSpan.FromSeconds(5);

    private static string CreateTempProfilesDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "devterm-configuremode-screenshots-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string CaptureConfigureMode(CliOptions initial, ConnectionProfileStore store, string baseName)
    {
        var dump = "";
        TuiTestRunner.RunHeadlessApp(app =>
        {
            var parts = ConfigureMode.BuildWindow(app, initial, validationError: null, store);
            var token = app.Begin(parts.Window) ?? throw new NotSupportedException(); ;
            app.LayoutAndDraw(true);

            try
            {
                dump = TuiTestRunner.DumpBuffer();
                Directory.CreateDirectory(_imagesDirectory);
                TuiScreenshot.Save(Path.Combine(_imagesDirectory, baseName + ".png"));
            }
            finally
            {
                app.End(token);
            }
        });

        File.WriteAllText(Path.Combine(_imagesDirectory, baseName + ".txt"), dump);
        return dump;
    }

    [TestMethod]
    public void ConfigureMode_SerialTransport_IsCaptured()
    {
        var directory = CreateTempProfilesDirectory();
        try
        {
            // A port name real hardware is vanishingly unlikely to occupy - this is a Unit test, and
            // SerialPortOptions reads real OS port enumeration (no fake to inject through
            // ConfigureMode.BuildWindow), so a real "COM3" here would make the "not found" hint below
            // depend on whatever happens to be plugged into the machine running the test.
            var initial = new CliOptions { Transport = "serial", Port = "COM99", Baud = 9600, Presenter = ["ascii"], Description = "Tektronix 2230 bench scope" };
            var dump = CaptureConfigureMode(initial, new ConnectionProfileStore(directory), "tui-configure-serial");

            Assert.Contains("Transport:", dump);
            Assert.Contains("── Serial ──", dump);
            Assert.Contains("Port:", dump);
            Assert.Contains("COM99", dump);
            Assert.Contains("Baud:", dump);
            Assert.Contains("not found", dump);
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
            var initial = new CliOptions { Transport = "tcp", Host = "192.168.0.107", Port = "23", Presenter = ["ascii"], Description = "Tektronix 2230 bench scope" };
            var dump = CaptureConfigureMode(initial, new ConnectionProfileStore(directory), "tui-configure-tcp");

            Assert.Contains("── TCP ──", dump);
            Assert.Contains("Host:", dump);
            Assert.Contains("192.168.0.107", dump);
            Assert.Contains("Port:", dump);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ConfigureMode_HidTransport_IsCaptured()
    {
        // The USB Device fields sit partly below the fold on an 80x24 window, so the capture is
        // scrolled to put that section's header at the top - the same scrolling PageDown drives, just
        // to an exact row so the whole section is in frame.
        var directory = CreateTempProfilesDirectory();
        try
        {
            var initial = new CliOptions { Transport = "hid", VendorId = 4216, ProductId = 63560, Presenter = ["hex"] };

            var dump = "";
            TuiTestRunner.RunHeadlessApp(app =>
            {
                var parts = ConfigureMode.BuildWindow(app, initial, validationError: null, new ConnectionProfileStore(directory));
                var token = app.Begin(parts.Window) ?? throw new NotSupportedException(); ;
                app.LayoutAndDraw(true);

                try
                {
                    parts.DescriptionField.SetFocus();
                    var header = parts.Form.SectionHeaderLabels["USB Device"];
                    parts.FormContent.Viewport = parts.FormContent.Viewport with { Y = parts.Form.Root.Frame.Y + header.Frame.Y };
                    app.LayoutAndDraw(true);

                    dump = TuiTestRunner.DumpBuffer();
                    Directory.CreateDirectory(_imagesDirectory);
                    TuiScreenshot.Save(Path.Combine(_imagesDirectory, "tui-configure-hid.png"));
                }
                finally
                {
                    app.End(token);
                }
            });

            File.WriteAllText(Path.Combine(_imagesDirectory, "tui-configure-hid.txt"), dump);

            Assert.Contains("Vendor ID:", dump);
            Assert.Contains("4216", dump);
            Assert.Contains("Product ID:", dump);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ConfigureMode_BleTransport_IsCaptured()
    {
        // Same below-the-fold reasoning as ConfigureMode_HidTransport_IsCaptured - the BLE field
        // group sits even further down, after Serial/TCP/USB, so a plain unscrolled capture would
        // miss it entirely.
        var directory = CreateTempProfilesDirectory();
        try
        {
            var initial = new CliOptions { Transport = "ble", BleDeviceId = "AB12CD34-1234-5678-9abc-def012345678", Presenter = ["ascii"] };

            var dump = "";
            TuiTestRunner.RunHeadlessApp(app =>
            {
                var parts = ConfigureMode.BuildWindow(app, initial, validationError: null, new ConnectionProfileStore(directory));
                var token = app.Begin(parts.Window) ?? throw new NotSupportedException();
                app.LayoutAndDraw(true);

                try
                {
                    parts.DescriptionField.SetFocus();
                    app.Keyboard.RaiseKeyDownEvent(Terminal.Gui.Input.Key.PageDown);
                    app.LayoutAndDraw(true);

                    dump = TuiTestRunner.DumpBuffer();
                    Directory.CreateDirectory(_imagesDirectory);
                    TuiScreenshot.Save(Path.Combine(_imagesDirectory, "tui-configure-ble.png"));
                }
                finally
                {
                    app.End(token);
                }
            });

            File.WriteAllText(Path.Combine(_imagesDirectory, "tui-configure-ble.txt"), dump);

            Assert.Contains("Device ID:", dump);

            // The field is narrower than the full UUID, so it scrolls to show the cursor (the end
            // of the typed value) rather than the start - assert on the visible tail, not the whole string.
            Assert.Contains("def012345678", dump);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ConfigureMode_LoopbackTransport_IsCaptured()
    {
        // Scrolled one page so the Loopback section and the Presentation fields under it are in
        // frame together, as ConfigureMode_ScrolledDown_RevealsControlsBelowTheFold does.
        var directory = CreateTempProfilesDirectory();
        try
        {
            var initial = new CliOptions { Transport = "loopback", Presenter = ["ascii"] };

            var dump = "";
            TuiTestRunner.RunHeadlessApp(app =>
            {
                var parts = ConfigureMode.BuildWindow(app, initial, validationError: null, new ConnectionProfileStore(directory));
                var token = app.Begin(parts.Window) ?? throw new NotSupportedException();
                app.LayoutAndDraw(true);

                try
                {
                    parts.DescriptionField.SetFocus();
                    app.Keyboard.RaiseKeyDownEvent(Terminal.Gui.Input.Key.PageDown);
                    app.LayoutAndDraw(true);

                    dump = TuiTestRunner.DumpBuffer();
                    Directory.CreateDirectory(_imagesDirectory);
                    TuiScreenshot.Save(Path.Combine(_imagesDirectory, "tui-configure-loopback.png"));
                }
                finally
                {
                    app.End(token);
                }
            });

            File.WriteAllText(Path.Combine(_imagesDirectory, "tui-configure-loopback.txt"), dump);

            Assert.Contains("No configuration needed", dump);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ConfigureMode_ScrolledDown_RevealsControlsBelowTheFold()
    {
        var directory = CreateTempProfilesDirectory();
        try
        {
            var initial = new CliOptions { Transport = "tcp", Host = "192.168.0.107", Port = "23", Presenter = ["ascii"], Description = "Tektronix 2230 bench scope" };

            var dump = "";
            TuiTestRunner.RunHeadlessApp(app =>
            {
                var parts = ConfigureMode.BuildWindow(app, initial, validationError: null, new ConnectionProfileStore(directory));
                var token = app.Begin(parts.Window) ?? throw new NotSupportedException();
                app.LayoutAndDraw(true);

                try
                {
                    // Same PageDown mechanism ConfigureModeTests.PageDown_ScrollsToRevealControlsBelowTheFold
                    // verifies works - this just also captures what it looks like.
                    // Focus off the saved-profiles list first: PageDown is the list's own while it
                    // has focus, which it now really does at startup.
                    parts.DescriptionField.SetFocus();
                    app.Keyboard.RaiseKeyDownEvent(Terminal.Gui.Input.Key.PageDown);
                    app.Keyboard.RaiseKeyDownEvent(Terminal.Gui.Input.Key.PageDown);
                    app.LayoutAndDraw(true);

                    dump = TuiTestRunner.DumpBuffer();
                    Directory.CreateDirectory(_imagesDirectory);
                    TuiScreenshot.Save(Path.Combine(_imagesDirectory, "tui-configure-scrolled.png"));
                }
                finally
                {
                    app.End(token);
                }
            });

            File.WriteAllText(Path.Combine(_imagesDirectory, "tui-configure-scrolled.txt"), dump);

            Assert.Contains("Connect", dump);
            Assert.Contains("Quit", dump);
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
        await session.OpenAsync(TestContext.CancellationToken);
        var cliOptions = new CliOptions { Transport = "tcp", Host = "192.168.0.107", Port = "23", Presenter = ["ascii"] };

        var dump = "";
        TuiTestRunner.RunHeadless(session, presenter, cliOptions, _ =>
        {
            dump = TuiTestRunner.DumpBuffer();
            Directory.CreateDirectory(_imagesDirectory);
            TuiScreenshot.Save(Path.Combine(_imagesDirectory, "tui-main-connected.png"));
        });

        await session.CloseAsync(TestContext.CancellationToken);

        Assert.Contains("tcp://192.168.0.107:23", dump);
        Assert.Contains("Send:", dump);
    }

    [TestMethod]
    public async Task TuiMode_TypingACommand_IsCaptured()
    {
        var (session, _, presenter) = CreateSession();
        await session.OpenAsync(TestContext.CancellationToken);
        var cliOptions = new CliOptions { Transport = "tcp", Host = "192.168.0.107", Port = "23", Presenter = ["ascii"] };

        var dump = "";
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
            TuiTestRunner.CurrentApp.LayoutAndDraw(true);
            dump = TuiTestRunner.DumpBuffer();
            TuiScreenshot.Save(Path.Combine(_imagesDirectory, "tui-main-typing.png"));
        });

        await session.CloseAsync(TestContext.CancellationToken);

        Assert.Contains("Send: ID?", dump);
    }

    [TestMethod]
    public async Task TuiMode_Disconnected_IsCaptured()
    {
        var (session, _, presenter) = CreateSession();
        await session.OpenAsync(TestContext.CancellationToken);
        var cliOptions = new CliOptions { Transport = "tcp", Host = "192.168.0.107", Port = "23", Presenter = ["ascii"] };

        TuiTestRunner.RunWithLoop(session, presenter, cliOptions, parts =>
        {
            // The menu action itself (not the bare static), so the status line and title refresh too.
            parts.ToggleConnectionAsync().GetAwaiter().GetResult();

            var disconnected = TuiTestRunner.WaitUntilOnLoop(() => parts.ConnectMenuItem.Title == "_Connect" && parts.StatusLabel.Text.Contains("Disconnected", StringComparison.Ordinal), _waitTimeout);
            Assert.IsTrue(disconnected, "Expected the menu item's title to flip to _Connect after disconnecting.");

            TuiTestRunner.InvokeOnLoop(() =>
            {
                TuiScreenshot.Save(Path.Combine(_imagesDirectory, "tui-main-disconnected.png"));
                return true;
            });
        });
    }

    [TestMethod]
    public async Task TuiMode_AfterReplyArrives_IsCaptured()
    {
        var (session, transport, presenter) = CreateSession();
        await session.OpenAsync(TestContext.CancellationToken);
        var cliOptions = new CliOptions { Transport = "tcp", Host = "192.168.0.107", Port = "23", Presenter = ["ascii"] };

        var dump = "";
        TuiTestRunner.RunWithLoop(session, presenter, cliOptions, parts =>
        {
            transport.PushIncomingAsync(Encoding.ASCII.GetBytes("ID TEK/2230,V81.1,VERS:14\r")).GetAwaiter().GetResult();

            var appeared = TuiTestRunner.WaitUntilOnLoop(() => parts.Output.Text.Length > 0, _waitTimeout);
            Assert.IsTrue(appeared, "Expected the decoded reply to appear via the real Session pull loop.");

            dump = TuiTestRunner.InvokeOnLoop(TuiTestRunner.DumpBuffer);
            TuiTestRunner.InvokeOnLoop(() =>
            {
                TuiScreenshot.Save(Path.Combine(_imagesDirectory, "tui-main-after-reply.png"));
                return true;
            });
        });

        await session.CloseAsync(TestContext.CancellationToken);

        Assert.Contains("[ascii] ID TEK/2230,V81.1,VERS:14", dump);
    }

    [TestMethod]
    public async Task TuiMode_Logging_IsCaptured()
    {
        var (session, _, presenter) = CreateSession();
        await session.OpenAsync(TestContext.CancellationToken);
        var cliOptions = new CliOptions { Transport = "tcp", Host = "192.168.0.107", Port = "23", Presenter = ["ascii"] };
        var directory = CreateTempProfilesDirectory();

        var dump = "";
        try
        {
            TuiTestRunner.RunHeadless(session, presenter, cliOptions, parts =>
            {
                Assert.IsTrue(parts.Logging.Start(Path.Combine(directory, "20260925-120000_tcp_192.168.0.107_23.jsonl")));
                TuiTestRunner.CurrentApp.LayoutAndDraw(true);
                dump = TuiTestRunner.DumpBuffer();
                TuiScreenshot.Save(Path.Combine(_imagesDirectory, "tui-main-logging.png"));
                parts.Logging.Stop();
            });
        }
        finally
        {
            await session.CloseAsync(TestContext.CancellationToken);
            Directory.Delete(directory, recursive: true);
        }

        Assert.Contains("● REC …tcp_192.168.0.107_23.jsonl", dump);
    }

    [TestMethod]
    public void PlaybackMode_PartWayThroughWithANote_IsCaptured()
    {
        var directory = CreateTempProfilesDirectory();
        try
        {
            var controller = new PlaybackPresenters().Open(PlaybackModeTests.WriteSampleLog(directory), new ManualTimeProvider());

            var dump = "";
            TuiTestRunner.RunHeadlessApp(app =>
            {
                var parts = PlaybackMode.BuildWindow(app, controller);
                var token = app.Begin(parts.Window) ?? throw new NotSupportedException();
                try
                {
                    parts.Do(() => controller.SetPresenters(["ascii", "hex"]));
                    parts.Do(() => controller.SeekTo(5));
                    parts.Do(() => controller.AddNote("IDN reply is correct"));
                    parts.Do(controller.Step);
                    controller.MarkIn();
                    parts.Do(() =>
                    {
                        controller.SetSpeed(PlaybackController.Speeds[3]);
                        return PlaybackBatch.Empty;
                    });
                    app.LayoutAndDraw(true);

                    dump = TuiTestRunner.DumpBuffer();
                    Directory.CreateDirectory(_imagesDirectory);
                    TuiScreenshot.Save(Path.Combine(_imagesDirectory, "tui-playback.png"));
                }
                finally
                {
                    app.End(token);
                }
            });

            File.WriteAllText(Path.Combine(_imagesDirectory, "tui-playback.txt"), dump);

            Assert.Contains("[note] IDN reply is correct", dump);
            Assert.Contains("[ascii] ID TEK/2230,V81.1,VERS:14", dump);
            Assert.Contains("Mark In", dump);
            Assert.Contains("2x", dump);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public required TestContext TestContext { get; set; }
}
