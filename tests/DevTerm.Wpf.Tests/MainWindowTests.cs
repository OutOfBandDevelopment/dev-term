using System.Text;
using System.Windows.Input;
using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Presenters.Text;

namespace DevTerm.Wpf.Tests;

/// <summary>
/// Drives a real <see cref="MainWindow"/> — real XAML, real controls, a real <c>Session</c> — via
/// its testable async entry points (<see cref="MainWindow.ConnectAsync"/>,
/// <see cref="MainWindow.SendCurrentInputAsync"/>) rather than OS-level UI Automation: this
/// exercises the actual UI logic and real control state (<c>OutputList.Items</c>,
/// <c>SendBox.Text</c>, <c>Title</c>) without needing a real display driver or an external
/// automation library. See <see cref="StaTestRunner"/> for why an STA thread and manual
/// dispatcher-pumping are both needed for this to work without <c>Application.Run()</c>.
///
/// Deliberately never calls <see cref="Window.Show"/> here: showing the window fires its real
/// <c>Loaded</c> event, which — exactly like the production app — calls <c>ConnectAsync</c> on
/// its own. Calling <c>ConnectAsync</c> a second time here as well would open the session twice
/// concurrently, and two concurrent readers on one <see cref="System.IO.Pipelines.PipeReader"/> is
/// explicitly unsupported (confirmed the hard way: it corrupts the pipe's internal state and
/// throws "Writing is not allowed after writer was completed" from a completely unrelated call).
/// Driving <c>ConnectAsync</c>/<c>SendCurrentInputAsync</c> directly, without ever showing the
/// window, tests the same logic deterministically.
///
/// Runs sequentially, not in parallel with other tests in this assembly: multiple concurrent WPF
/// dispatchers/STA threads have real, observed cross-test interference (a test that passes in
/// isolation intermittently failed when run alongside the others) — the same class of problem
/// <c>DevTermConfigurationTests</c> already avoids for its own (different) reason.
/// </summary>
[TestCategory("UNIT")]
[TestClass]
[DoNotParallelize]
public sealed class MainWindowTests
{
    private static readonly TimeSpan PumpTimeout = TimeSpan.FromSeconds(5);

    private static (MainWindow Window, FakeTransport Transport) CreateWindow(CliOptions? cliOptions = null)
    {
        var transport = new FakeTransport();
        var presenter = new AsciiPresenter(Microsoft.Extensions.Options.Options.Create(new AsciiPresenterOptions()));
        var session = new Session(transport, new Pipeline([presenter]));
        var options = cliOptions ?? new CliOptions { Transport = "tcp", Host = "127.0.0.1", TcpPort = 23 };
        options.Parser ??= presenter.Name; // the fake catalog below only holds this one presenter
        var window = new MainWindow(session, new PresenterCatalog([presenter]), options, IsolatedProfiles.Empty())
        {
            ShowInTaskbar = false,
        };
        return (window, transport);
    }

    [TestMethod]
    public void Constructor_WithAManifestNameThatDoesNotResolve_AddsAWarningToTheOutputList()
    {
        StaTestRunner.Run(async () =>
        {
            var (window, _) = CreateWindow(new CliOptions
            {
                Transport = "tcp",
                Host = "127.0.0.1",
                TcpPort = 23,
                ManifestName = "definitely-does-not-exist-" + Guid.NewGuid().ToString("N"),
            });

            Assert.HasCount(1, window.OutputList.Items);
            StringAssert.Contains((string)window.OutputList.Items[0]!, "Warning:");

            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void ConnectAsync_OpensSessionAndSetsTitleAndEnablesSendBox()
    {
        StaTestRunner.Run(async () =>
        {
            var (window, _) = CreateWindow();
            await window.ConnectAsync();

            StringAssert.Contains(window.Title, "tcp://127.0.0.1:23");
            StringAssert.Contains(window.Title, "ascii");
            Assert.IsTrue(window.SendBox.IsEnabled);
        });
    }

    [TestMethod]
    public void SwitchingTheSendAsBox_ChangesHowTheNextTypedLineIsEncoded_AndTheTitle()
    {
        StaTestRunner.Run(async () =>
        {
            var transport = new FakeTransport();
            var ascii = new AsciiPresenter(Microsoft.Extensions.Options.Options.Create(new AsciiPresenterOptions()));
            var session = new Session(transport, new Pipeline([ascii]));
            var window = new MainWindow(
                session,
                new PresenterCatalog([ascii, new HexPresenter()]),
                new CliOptions { Transport = "tcp", Host = "127.0.0.1", TcpPort = 23, Presenter = ["ascii"], Parser = "ascii" },
                IsolatedProfiles.Empty())
            {
                ShowInTaskbar = false,
            };
            await window.ConnectAsync();
            CollectionAssert.AreEquivalent(new[] { "ascii", "hex" }, window.ParserBox.Items.Cast<string>().ToArray());
            Assert.AreEqual("ascii", window.ParserBox.SelectedItem, "The box starts at the profile's parser.");

            window.SendBox.Text = "ff";
            await window.SendCurrentInputAsync();

            window.ParserBox.SelectedItem = "hex";
            StringAssert.Contains(window.Title, "send as hex");
            window.SendBox.Text = "ff";
            await window.SendCurrentInputAsync();

            Assert.HasCount(2, transport.WrittenPayloads);
            CollectionAssert.AreEqual(new byte[] { 0x66, 0x66 }, transport.WrittenPayloads[0]);
            CollectionAssert.AreEqual(new byte[] { 0xFF }, transport.WrittenPayloads[1]);
        });
    }

    [TestMethod]
    public void ConnectAsync_WithSeveralPresenters_ShowsThemAllInTheTitle()
    {
        StaTestRunner.Run(async () =>
        {
            var transport = new FakeTransport();
            var ascii = new AsciiPresenter(Microsoft.Extensions.Options.Options.Create(new AsciiPresenterOptions()));
            var hex = new HexPresenter();
            var window = new MainWindow(
                new Session(transport, new Pipeline([ascii, hex])),
                new PresenterCatalog([ascii, hex]),
                new CliOptions { Transport = "tcp", Host = "127.0.0.1", TcpPort = 23, Presenter = ["ascii", "hex"] },
                IsolatedProfiles.Empty())
            {
                ShowInTaskbar = false,
            };
            await window.ConnectAsync();

            StringAssert.Contains(window.Title, "ascii, hex; send as ascii");
        });
    }

    [TestMethod]
    public void IncomingBytes_AppearInOutputListThroughTheRealSessionPipeline()
    {
        StaTestRunner.Run(async () =>
        {
            var (window, transport) = CreateWindow();
            await window.ConnectAsync();

            await transport.PushIncomingAsync(Encoding.ASCII.GetBytes("ID TEK/2230\r"));

            var appeared = StaTestRunner.PumpUntil(() => window.OutputList.Items.Count > 0, PumpTimeout);

            Assert.IsTrue(appeared, "Expected the decoded line to arrive via the real Session pull loop + Dispatcher.Invoke.");
            StringAssert.Contains((string)window.OutputList.Items[0]!, "[ascii]");
            StringAssert.Contains((string)window.OutputList.Items[0]!, "ID TEK/2230");
        });
    }

    [TestMethod]
    public void SendCurrentInputAsync_WritesTypedLineWithLineEndingToTheTransport()
    {
        StaTestRunner.Run(async () =>
        {
            var (window, transport) = CreateWindow(new CliOptions { Transport = "tcp", Host = "127.0.0.1", TcpPort = 23, LineEnding = LineEnding.Cr });
            await window.ConnectAsync();
            window.SendBox.Text = "ID?";

            await window.SendCurrentInputAsync();

            Assert.HasCount(1, transport.WrittenPayloads);
            Assert.AreEqual("ID?\r", Encoding.ASCII.GetString(transport.WrittenPayloads[0]));
            Assert.AreEqual(string.Empty, window.SendBox.Text, "The send box should clear after sending.");
        });
    }

    [TestMethod]
    public void SendCurrentInputAsync_WithEmptyInput_DoesNotWriteToTheTransport()
    {
        StaTestRunner.Run(async () =>
        {
            var (window, transport) = CreateWindow(new CliOptions { Transport = "tcp", Host = "127.0.0.1", TcpPort = 23, LineEnding = LineEnding.None });
            await window.ConnectAsync();
            window.SendBox.Text = string.Empty;

            await window.SendCurrentInputAsync();

            Assert.IsEmpty(transport.WrittenPayloads);
        });
    }

    [TestMethod]
    public void HandleSendBoxKey_Up_RecallsMostRecentlySentLine()
    {
        StaTestRunner.Run(async () =>
        {
            var (window, _) = CreateWindow(new CliOptions { Transport = "tcp", Host = "127.0.0.1", TcpPort = 23, LineEnding = LineEnding.None });
            await window.ConnectAsync();
            window.SendBox.Text = "ID?";
            await window.SendCurrentInputAsync();

            window.HandleSendBoxKey(Key.Up);

            Assert.AreEqual("ID?", window.SendBox.Text);
        });
    }

    [TestMethod]
    public void HandleSendBoxKey_UpTwiceThenDown_RecallsTheNewerLine()
    {
        StaTestRunner.Run(async () =>
        {
            var (window, _) = CreateWindow(new CliOptions { Transport = "tcp", Host = "127.0.0.1", TcpPort = 23, LineEnding = LineEnding.None });
            await window.ConnectAsync();
            window.SendBox.Text = "first";
            await window.SendCurrentInputAsync();
            window.SendBox.Text = "second";
            await window.SendCurrentInputAsync();

            window.HandleSendBoxKey(Key.Up);
            window.HandleSendBoxKey(Key.Up);
            window.HandleSendBoxKey(Key.Down);

            Assert.AreEqual("second", window.SendBox.Text);
        });
    }

    [TestMethod]
    public void HandleSendBoxKey_UnrecognizedKey_ReturnsFalse()
    {
        StaTestRunner.Run(async () =>
        {
            var (window, _) = CreateWindow();

            Assert.IsFalse(window.HandleSendBoxKey(Key.A));
            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void ToggleConnectionAsync_DisconnectsThenReconnects()
    {
        StaTestRunner.Run(async () =>
        {
            var (window, _) = CreateWindow();
            await window.ConnectAsync();

            Assert.AreEqual("_Disconnect", window.ConnectMenuItem.Header);
            Assert.IsTrue(window.SendBox.IsEnabled);

            await window.ToggleConnectionAsync();

            Assert.AreEqual("_Connect", window.ConnectMenuItem.Header);
            Assert.IsFalse(window.SendBox.IsEnabled);

            await window.ToggleConnectionAsync();

            Assert.AreEqual("_Disconnect", window.ConnectMenuItem.Header);
            Assert.IsTrue(window.SendBox.IsEnabled);
        });
    }

    [TestMethod]
    public void SendCurrentInputAsync_WhileDisconnected_DoesNotWriteToTheTransport()
    {
        StaTestRunner.Run(async () =>
        {
            var (window, transport) = CreateWindow(new CliOptions { Transport = "tcp", Host = "127.0.0.1", TcpPort = 23, LineEnding = LineEnding.Cr });
            await window.ConnectAsync();
            await window.ToggleConnectionAsync();
            window.SendBox.Text = "ID?";

            await window.SendCurrentInputAsync();

            Assert.IsEmpty(transport.WrittenPayloads);
        });
    }

    // A real crash reported from the running app ("Cannot set Visibility to Visible or call Show,
    // ShowDialog, Close ... while a Window is closing", from OnClosing's second Close()): OnClosing
    // cancels the first close, awaits the session's cleanup, then calls Close() again - but when
    // the session is already closed (never opened, or disconnected via the menu) those awaits
    // complete synchronously, so the second Close() ran inside the first one's own Closing event and
    // WPF refused it. This once looked like a test-automation quirk (see CLAUDE.md's old note about
    // not calling Close() on a shown window) and was actually the app's own bug.
    [TestMethod]
    public void Close_WhenTheSessionIsAlreadyClosed_ClosesTheWindowWithoutThrowing()
    {
        StaTestRunner.Run(async () =>
        {
            var (window, _) = CreateWindow();
            var closed = false;
            window.Closed += (_, _) => closed = true;

            window.Close();

            Assert.IsTrue(StaTestRunner.PumpUntil(() => closed, PumpTimeout), "The window should finish closing once the async cleanup is done.");
            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void Close_AfterDisconnectingViaTheMenu_ClosesTheWindowWithoutThrowing()
    {
        StaTestRunner.Run(async () =>
        {
            var (window, _) = CreateWindow();
            await window.ConnectAsync();
            await window.ToggleConnectionAsync();
            var closed = false;
            window.Closed += (_, _) => closed = true;

            window.Close();

            Assert.IsTrue(StaTestRunner.PumpUntil(() => closed, PumpTimeout));
        });
    }

    [TestMethod]
    public void Close_WhileConnected_ClosesTheWindowAndTheSession()
    {
        StaTestRunner.Run(async () =>
        {
            var (window, _) = CreateWindow();
            await window.ConnectAsync();
            var closed = false;
            window.Closed += (_, _) => closed = true;

            window.Close();

            Assert.IsTrue(StaTestRunner.PumpUntil(() => closed, PumpTimeout));
        });
    }
}
