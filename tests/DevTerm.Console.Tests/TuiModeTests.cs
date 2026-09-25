using System.Text;
using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;
using DevTerm.Presenters.Text;
using Microsoft.Extensions.Options;

namespace DevTerm.Console.Tests;

/// <summary>
/// Drives a real <see cref="TuiMode"/> window — real Terminal.Gui controls, a real <c>Session</c> —
/// via <see cref="TuiTestRunner"/> rather than OS-level UI Automation, mirroring how
/// <c>DevTerm.Wpf.Tests.MainWindowTests</c> drives a real WPF <c>MainWindow</c>. See
/// <see cref="TuiTestRunner"/>'s doc comment for the two run modes this needed and why.
///
/// Runs sequentially, not in parallel with other tests in this assembly: Terminal.Gui's
/// <c>Application</c> state is static/process-global (same class of concern that made
/// <c>DevTerm.Wpf.Tests.MainWindowTests</c> need <c>[DoNotParallelize]</c> for its own, WPF-specific
/// reason).
/// </summary>
[TestCategory("UNIT")]
[TestClass]
[DoNotParallelize]
public sealed class TuiModeTests
{
    private static readonly TimeSpan _waitTimeout = TimeSpan.FromSeconds(5);

    private static (Session Session, FakeTransport Transport, IPresenter Presenter) CreateSession()
    {
        var transport = new FakeTransport();
        var presenter = new AsciiPresenter(Options.Create(new AsciiPresenterOptions()));
        var session = new Session(transport, new Pipeline([presenter]));
        return (session, transport, presenter);
    }

    [TestMethod]
    public async Task BuildWindow_WithAManifestNameThatDoesNotResolve_ShowsAWarningInOutput()
    {
        var (session, _, presenter) = CreateSession();
        await session.OpenAsync(TestContext.CancellationToken);
        var cliOptions = new CliOptions
        {
            Transport = "tcp",
            Host = "127.0.0.1",
            Port = "23",
            ManifestName = "definitely-does-not-exist-" + Guid.NewGuid().ToString("N"),
        };

        TuiTestRunner.RunHeadless(session, presenter, cliOptions, parts =>
        {
            Assert.Contains("Warning:", parts.Output.Text);
        });

        await session.CloseAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task BuildWindow_RendersTitleAndSendPrompt()
    {
        var (session, _, presenter) = CreateSession();
        await session.OpenAsync(TestContext.CancellationToken);
        var cliOptions = new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "23" };

        TuiTestRunner.RunHeadless(session, presenter, cliOptions, parts =>
        {
            Assert.Contains("tcp://127.0.0.1:23", parts.Window.Title);
            Assert.Contains("ascii", parts.Window.Title);

            var screen = TuiTestRunner.DumpBuffer();
            Assert.Contains("Send:", screen);
        });

        await session.CloseAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task TypingAndEnter_SendsLineWithLineEndingToTheTransport()
    {
        var (session, transport, presenter) = CreateSession();
        await session.OpenAsync(TestContext.CancellationToken);
        var cliOptions = new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "23", LineEnding = LineEnding.Cr };

        TuiTestRunner.RunHeadless(session, presenter, cliOptions, _ =>
        {
            TuiTestRunner.TypeText("ID?");
            TuiTestRunner.PressEnter();
        });

        await session.CloseAsync(TestContext.CancellationToken);

        Assert.HasCount(1, transport.WrittenPayloads);
        Assert.AreEqual("ID?\r", Encoding.ASCII.GetString(transport.WrittenPayloads[0]));
    }

    [TestMethod]
    public async Task EmptyInput_Enter_DoesNotWriteToTheTransport()
    {
        var (session, transport, presenter) = CreateSession();
        await session.OpenAsync(TestContext.CancellationToken);
        var cliOptions = new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "23", LineEnding = LineEnding.None };

        TuiTestRunner.RunHeadless(session, presenter, cliOptions, _ =>
        {
            TuiTestRunner.PressEnter();
        });

        await session.CloseAsync(TestContext.CancellationToken);

        Assert.IsEmpty(transport.WrittenPayloads);
    }

    [TestMethod]
    public async Task CtrlQ_RequestsStop()
    {
        var (session, _, presenter) = CreateSession();
        await session.OpenAsync(TestContext.CancellationToken);
        var cliOptions = new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "23" };

        TuiTestRunner.RunHeadless(session, presenter, cliOptions, parts =>
        {
            // The Quit MenuItem's own "Ctrl+Q" argument only labels the shortcut in the menu's
            // display text - checked directly, it doesn't register a live key binding by itself
            // (neither the Window's nor the MenuBar's own KeyBindings contained a Ctrl+Q entry
            // after building this exact menu). TuiMode.BuildWindow instead subscribes to the
            // global Application.KeyDown event: a per-view window.KeyDown handler doesn't
            // reliably see keys already routed to a focused child first (sendField normally has
            // focus) - checked directly, Ctrl+Q reached window.KeyDown when nothing else had focus
            // but not once sendField did, while Application.KeyDown fires ahead of focus routing.
            //
            // Application.RaiseKeyDownEvent (not TuiTestRunner.PressKey's IInputInjector-based
            // route) is used here specifically: the injector path proved unreliable once several
            // Init/Shutdown cycles had already run earlier in the same test process (this test
            // passed reliably alone, then failed once run after the others in this class) -
            // RaiseKeyDownEvent dispatches directly and didn't show the same degradation.
            var runnable = (Terminal.Gui.App.IRunnable)parts.Window;
            Assert.IsFalse(runnable.StopRequested);

            Terminal.Gui.App.Application.RaiseKeyDownEvent(Terminal.Gui.Input.Key.Q.WithCtrl);

            Assert.IsTrue(runnable.StopRequested, "Ctrl+Q should call Application.RequestStop(), setting the window's StopRequested.");
        });

        await session.CloseAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task TypingAndEnter_WhileDisconnected_DoesNotWriteToTheTransport()
    {
        var (session, transport, presenter) = CreateSession();
        await session.OpenAsync(TestContext.CancellationToken);
        var cliOptions = new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "23", LineEnding = LineEnding.Cr };
        await session.CloseAsync(TestContext.CancellationToken);

        TuiTestRunner.RunHeadless(session, presenter, cliOptions, _ =>
        {
            TuiTestRunner.TypeText("ID?");
            TuiTestRunner.PressEnter();
        });

        Assert.IsEmpty(transport.WrittenPayloads);
    }

    [TestMethod]
    public async Task ToggleConnectionAsync_DisconnectsThenReconnects()
    {
        var (session, _, presenter) = CreateSession();
        await session.OpenAsync(TestContext.CancellationToken);
        var cliOptions = new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "23" };

        TuiTestRunner.RunWithLoop(session, presenter, cliOptions, parts =>
        {
            Assert.AreEqual(ConnectionState.Open, session.State);
            Assert.AreEqual("_Disconnect", parts.ConnectMenuItem.Title);

            TuiMode.ToggleConnectionAsync(session, cliOptions, parts.ConnectMenuItem, parts.SendField, _ => { }).GetAwaiter().GetResult();

            var disconnected = TuiTestRunner.WaitUntilOnLoop(() => parts.ConnectMenuItem.Title == "_Connect", _waitTimeout);
            Assert.IsTrue(disconnected, "Expected the menu item's title to flip to _Connect after disconnecting.");
            Assert.AreEqual(ConnectionState.Closed, session.State);
            Assert.IsFalse(TuiTestRunner.InvokeOnLoop(() => parts.SendField.Enabled));

            TuiMode.ToggleConnectionAsync(session, cliOptions, parts.ConnectMenuItem, parts.SendField, _ => { }).GetAwaiter().GetResult();

            var reconnected = TuiTestRunner.WaitUntilOnLoop(() => parts.ConnectMenuItem.Title == "_Disconnect", _waitTimeout);
            Assert.IsTrue(reconnected, "Expected the menu item's title to flip back to _Disconnect after reconnecting.");
            Assert.AreEqual(ConnectionState.Open, session.State);
            Assert.IsTrue(TuiTestRunner.InvokeOnLoop(() => parts.SendField.Enabled));
        });

        await session.CloseAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task IncomingBytes_AppearInOutputThroughTheRealSessionPipeline()
    {
        var (session, transport, presenter) = CreateSession();
        await session.OpenAsync(TestContext.CancellationToken);
        var cliOptions = new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "23" };

        TuiTestRunner.RunWithLoop(session, presenter, cliOptions, parts =>
        {
            transport.PushIncomingAsync(Encoding.ASCII.GetBytes("ID TEK/2230\r")).GetAwaiter().GetResult();

            var appeared = TuiTestRunner.WaitUntilOnLoop(() => parts.Output.Text.Length > 0, _waitTimeout);
            Assert.IsTrue(appeared, "Expected the decoded line to arrive via the real Session pull loop + Application.Invoke.");

            var text = TuiTestRunner.InvokeOnLoop(() => parts.Output.Text);
            Assert.Contains("[ascii]", text);
            Assert.Contains("ID TEK/2230", text);
        });

        await session.CloseAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task BuildWindow_WithSeveralPresenters_ShowsThemAndTheSendFormatInTheTitle()
    {
        var transport = new FakeTransport();
        var ascii = new AsciiPresenter(Options.Create(new AsciiPresenterOptions()));
        var hex = new HexPresenter();
        var session = new Session(transport, new Pipeline([ascii, hex]));
        await session.OpenAsync(TestContext.CancellationToken);
        var cliOptions = new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "23", Presenter = ["ascii", "hex"] };

        TuiTestRunner.RunHeadless(session, new PresenterCatalog([ascii, hex]), cliOptions, parts =>
        {
            Assert.Contains("ascii, hex; send as ascii", parts.Window.Title);
        });

        await session.CloseAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task SetParser_SwitchesHowTheNextTypedLineIsEncoded_AndUpdatesTheTitle()
    {
        var transport = new FakeTransport();
        var ascii = new AsciiPresenter(Options.Create(new AsciiPresenterOptions()));
        var hex = new HexPresenter();
        var session = new Session(transport, new Pipeline([ascii]));
        await session.OpenAsync(TestContext.CancellationToken);
        var cliOptions = new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "23", Presenter = ["ascii"], Parser = "ascii" };

        // Raises the send field's own KeyDown directly instead of injecting a key through
        // IInputInjector: that injector degrades after enough Application.Init/Shutdown cycles in one
        // test process (see CLAUDE.md), and this test is about which parser encodes the line, not
        // about key routing (the tests above already cover that).
        TuiTestRunner.RunHeadless(session, new PresenterCatalog([ascii, hex]), cliOptions, parts =>
        {
            parts.SendField.Text = "ff";
            parts.SendField.NewKeyDownEvent(Terminal.Gui.Input.Key.Enter);

            parts.SetParser("hex");
            Assert.Contains("send as hex", parts.Window.Title);

            parts.SendField.Text = "ff";
            parts.SendField.NewKeyDownEvent(Terminal.Gui.Input.Key.Enter);
        });

        await session.CloseAsync(TestContext.CancellationToken);

        Assert.HasCount(2, transport.WrittenPayloads);
        Assert.AreSequenceEqual(new byte[] { 0x66, 0x66 }, transport.WrittenPayloads[0], "As ascii, 'ff' is two characters.");
        Assert.AreSequenceEqual(new byte[] { 0xFF }, transport.WrittenPayloads[1], "As hex, 'ff' is one byte.");
    }

    [TestMethod]
    public async Task CursorUp_AfterSendingALine_RecallsIt()
    {
        var (session, _, presenter) = CreateSession();
        await session.OpenAsync(TestContext.CancellationToken);
        var cliOptions = new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "23", LineEnding = LineEnding.None };

        // Raises the send field's own KeyDown directly (see SetParser_...'s comment above) rather
        // than injecting through IInputInjector - this is about history recall, not key routing.
        TuiTestRunner.RunHeadless(session, presenter, cliOptions, parts =>
        {
            parts.SendField.Text = "ID?";
            parts.SendField.NewKeyDownEvent(Terminal.Gui.Input.Key.Enter);

            parts.SendField.NewKeyDownEvent(Terminal.Gui.Input.Key.CursorUp);

            Assert.AreEqual("ID?", parts.SendField.Text);
        });

        await session.CloseAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task CursorUpTwiceThenCursorDown_RecallsTheNewerLine()
    {
        var (session, _, presenter) = CreateSession();
        await session.OpenAsync(TestContext.CancellationToken);
        var cliOptions = new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "23", LineEnding = LineEnding.None };

        TuiTestRunner.RunHeadless(session, presenter, cliOptions, parts =>
        {
            parts.SendField.Text = "first";
            parts.SendField.NewKeyDownEvent(Terminal.Gui.Input.Key.Enter);
            parts.SendField.Text = "second";
            parts.SendField.NewKeyDownEvent(Terminal.Gui.Input.Key.Enter);

            parts.SendField.NewKeyDownEvent(Terminal.Gui.Input.Key.CursorUp);
            parts.SendField.NewKeyDownEvent(Terminal.Gui.Input.Key.CursorUp);
            parts.SendField.NewKeyDownEvent(Terminal.Gui.Input.Key.CursorDown);

            Assert.AreEqual("second", parts.SendField.Text);
        });

        await session.CloseAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task SendAsMenu_ListsEveryPresenterThatCanEncodeInput()
    {
        var (session, _, presenter) = CreateSession();
        await session.OpenAsync(TestContext.CancellationToken);
        var cliOptions = new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "23" };
        var catalog = new PresenterCatalog([presenter, new HexPresenter()]);
        cliOptions.Parser = "ascii";

        TuiTestRunner.RunHeadless(session, catalog, cliOptions, _ =>
        {
            // The menu bar renders its top-level titles; the per-item entries only appear once opened.
            Assert.Contains("Send as", TuiTestRunner.DumpBuffer());
        });

        await session.CloseAsync(TestContext.CancellationToken);
    }


    [TestMethod]
    public async Task BuildWindow_WhenTheConnectionIsASavedProfile_TitlesTheWindowWithTheProfileName()
    {
        var (session, _, presenter) = CreateSession();
        await session.OpenAsync(TestContext.CancellationToken);
        var directory = Path.Combine(Path.GetTempPath(), $"devterm-tests-{Guid.NewGuid():N}");
        try
        {
            var cliOptions = new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "23", Presenter = ["ascii"] };
            var store = new ConnectionProfileStore(directory);
            store.Save("bench-scope", cliOptions);

            TuiTestRunner.RunHeadless(session, presenter, cliOptions, parts =>
            {
                Assert.Contains("bench-scope", parts.Window.Title);
                Assert.DoesNotContain("tcp://", parts.Window.Title, "A saved profile is titled by name, not by its connection string.");
            }, store);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public TestContext TestContext { get; set; }
}
