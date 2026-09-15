using DevTerm.Configuration;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Testing;
using Terminal.Gui.Views;

namespace DevTerm.Console.Tests;

/// <summary>
/// Drives a real <see cref="ConfigureMode"/> window headlessly, the same way
/// <c>TuiModeTests</c> drives <see cref="TuiMode"/> — see <see cref="TuiTestRunner"/>'s doc comment
/// for why headless (no <c>Application.Run()</c> loop) is the mode that supports key injection.
/// Uses a temp directory for <see cref="ConnectionProfileStore"/> rather than the real
/// <c>~/.dev-term/profiles</c>, same isolation as <c>ConnectionProfileStoreTests</c>.
/// </summary>
[TestCategory("UNIT")]
[TestClass]
[DoNotParallelize]
public sealed class ConfigureModeTests
{
    private static string CreateTempProfilesDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "devterm-configuremode-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void RunHeadless(CliOptions initial, string? validationError, ConnectionProfileStore profileStore, Action<ConfigureWindowParts> body)
    {
        Application.Init("dotnet");
        try
        {
            var parts = ConfigureMode.BuildWindow(initial, validationError, profileStore);
            var token = Application.Begin(parts.Window);
            Application.LayoutAndDraw(true);

            try
            {
                body(parts);
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
    }

    /// <summary>
    /// Simulates pressing a button via <c>View.InvokeCommand(Command.Accept)</c> — a direct,
    /// documented way to invoke a view's command, bypassing key-event routing entirely. Landed on
    /// this after two key-injection-based approaches each proved unreliable against a real running
    /// window: <c>View.SetFocus()</c> makes <c>HasFocus</c> report <see langword="true"/> without
    /// fully registering the view for command routing (a button focused this way then sent an
    /// injected Enter/Space silently did nothing), and Tab-navigating focus onto a button worked in
    /// isolation but not once several tests ran in the same process. This still exercises the real
    /// <c>Accepting</c> handler wired in <see cref="ConfigureMode.BuildWindow"/> — just not via the
    /// full input pipeline, which real end-to-end coverage (a human, or a future OS-level UI
    /// Automation harness) still exercises for the parts this doesn't.
    /// </summary>
    private static void Click(Button button) => button.InvokeCommand(Command.Accept);

    [TestMethod]
    public void Connect_WithValidFields_ReturnsOptionsAndStopsTheLoop()
    {
        var directory = CreateTempProfilesDirectory();
        try
        {
            var initial = new CliOptions { Transport = "serial" }; // invalid: no Port
            RunHeadless(initial, "Missing required '--port' for the serial transport.", new ConnectionProfileStore(directory), parts =>
            {
                StringAssert.Contains(parts.ErrorLabel.Text, "Missing required");

                parts.TransportField.Text = "tcp";
                parts.HostField.Text = "192.168.0.107";
                parts.TcpPortField.Text = "23";

                Click(parts.ConnectButton);

                Assert.IsNotNull(parts.Result);
                Assert.AreEqual("tcp", parts.Result.Transport);
                Assert.AreEqual("192.168.0.107", parts.Result.Host);
                Assert.AreEqual(23, parts.Result.TcpPort);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Connect_WithStillInvalidFields_ShowsErrorAndDoesNotSetResult()
    {
        var directory = CreateTempProfilesDirectory();
        try
        {
            var initial = new CliOptions { Transport = "tcp" }; // invalid: no Host/TcpPort
            RunHeadless(initial, "Missing or invalid '--tcpport'...", new ConnectionProfileStore(directory), parts =>
            {
                Click(parts.ConnectButton);

                Assert.IsNull(parts.Result);
                StringAssert.Contains(parts.ErrorLabel.Text, "tcpport");
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Quit_SetsResultToNull()
    {
        var directory = CreateTempProfilesDirectory();
        try
        {
            RunHeadless(new CliOptions(), null, new ConnectionProfileStore(directory), parts =>
            {
                parts.TransportField.Text = "tcp";
                parts.HostField.Text = "192.168.0.107";
                parts.TcpPortField.Text = "23";

                // A non-null sentinel first: Result already defaults to null, so clicking Quit and
                // then asserting null would pass even if the click did nothing at all — seeding a
                // non-null value first means the assertion only passes if Quit's handler actually ran.
                parts.Result = new CliOptions();

                Click(parts.QuitButton);

                Assert.IsNull(parts.Result);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Save_WritesAProfileTheStoreCanLoadBack()
    {
        var directory = CreateTempProfilesDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            var initial = new CliOptions { Transport = "tcp", Host = "192.168.0.108", TcpPort = 23, Presenter = "ascii" };

            RunHeadless(initial, null, store, parts =>
            {
                parts.SaveNameField.Text = "tek108";
                Click(parts.SaveButton);
                StringAssert.Contains(parts.ErrorLabel.Text, "Saved profile 'tek108'");
            });

            Assert.Contains("tek108", store.List());
            var saved = store.Load("tek108");
            Assert.AreEqual("tcp", saved.Transport);
            Assert.AreEqual("192.168.0.108", saved.Host);
            Assert.AreEqual(23, saved.TcpPort);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void LoadButton_PopulatesFieldsFromTheSelectedProfile()
    {
        var directory = CreateTempProfilesDirectory();
        try
        {
            // Arrange the saved profile directly through the store rather than by driving the UI a
            // second time in this method: two Application.Init/Shutdown cycles within one test
            // method (rather than one per [TestMethod], MSTest's normal granularity) turned out to
            // leave the second window's button clicks silently doing nothing — found the hard way,
            // not something this stub investigated further given a single-cycle-per-test workaround
            // was straightforward and every test here already needs its own cycle regardless.
            var store = new ConnectionProfileStore(directory);
            store.Save("tek108", new CliOptions { Transport = "tcp", Host = "192.168.0.108", TcpPort = 23, Presenter = "ascii" });

            RunHeadless(new CliOptions { Transport = "serial" }, "Missing required '--port'...", store, parts =>
            {
                parts.ProfilesList.SelectedItem = 0;
                Click(parts.LoadButton);

                StringAssert.Contains(parts.ErrorLabel.Text, "Loaded profile 'tek108'");
                Assert.AreEqual("tcp", parts.TransportField.Text);
                Assert.AreEqual("192.168.0.108", parts.HostField.Text);
                Assert.AreEqual("23", parts.TcpPortField.Text);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
