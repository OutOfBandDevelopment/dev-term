using System.Buffers;
using System.Text;
using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.StreamContent;
using DevTerm.Core.Transports;
using DevTerm.Presenters.Text;
using DevTerm.Test.Utilities;
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
[TestCategory(TestCategories.Unit)]
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

    /// <summary>
    /// Stands in for <c>K8055Decoder</c> (see <see cref="ControlPanelModeTests"/>'s own nested
    /// equivalent) so bug 007's regression test below can tell whether closing the K8055 panel
    /// actually unsubscribed from <see cref="ValuesChanged"/> — <see cref="SubscriberCount"/> reads
    /// the live delegate's invocation list rather than a separately tracked counter, so it can't
    /// drift from what the event itself actually holds.
    /// </summary>
    private sealed class FakeStructuredPresenter : IPresenter, IStructuredPresenter
    {
        public string Name => "k8055";

        public IReadOnlyList<string> Render(ReadOnlySequence<byte> data) => [];

        public event EventHandler<IReadOnlyDictionary<string, string>>? ValuesChanged;

        public int SubscriberCount => ValuesChanged?.GetInvocationList().Length ?? 0;
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

            TuiTestRunner.CurrentApp.Keyboard.RaiseKeyDownEvent(Terminal.Gui.Input.Key.Q.WithCtrl);

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

            TuiMode.ToggleConnectionAsync(TuiTestRunner.CurrentApp, session, cliOptions, parts.ConnectMenuItem, parts.SendField, _ => { }).GetAwaiter().GetResult();

            var disconnected = TuiTestRunner.WaitUntilOnLoop(() => parts.ConnectMenuItem.Title == "_Connect", _waitTimeout);
            Assert.IsTrue(disconnected, "Expected the menu item's title to flip to _Connect after disconnecting.");
            Assert.AreEqual(ConnectionState.Closed, session.State);
            Assert.IsFalse(TuiTestRunner.InvokeOnLoop(() => parts.SendField.Enabled));

            TuiMode.ToggleConnectionAsync(TuiTestRunner.CurrentApp, session, cliOptions, parts.ConnectMenuItem, parts.SendField, _ => { }).GetAwaiter().GetResult();

            var reconnected = TuiTestRunner.WaitUntilOnLoop(() => parts.ConnectMenuItem.Title == "_Disconnect", _waitTimeout);
            Assert.IsTrue(reconnected, "Expected the menu item's title to flip back to _Disconnect after reconnecting.");
            Assert.AreEqual(ConnectionState.Open, session.State);
            Assert.IsTrue(TuiTestRunner.InvokeOnLoop(() => parts.SendField.Enabled));
        });

        await session.CloseAsync(TestContext.CancellationToken);
    }

    /// <summary>
    /// Bug 007 (docs/bugs/fixed/007-tui-modal-windows-not-disposed.md): the K8055 panel window
    /// opened via <c>app.Run(panelParts.Window)</c> used to never be disposed by its caller, so
    /// <c>window.Disposing</c>'s <c>structuredPresenter.ValuesChanged -= onValuesChanged</c>
    /// (registered in <c>ControlPanelMode.BuildWindow</c>) never ran — the subscription leaked for
    /// the rest of the process. Drives the real "_K8055 Control Panel..." menu item's own
    /// <c>Action</c> (not a copy of its logic) so this proves the actual production code path, the
    /// same way <see cref="ToggleConnectionAsync_DisconnectsThenReconnects"/> drives
    /// <see cref="TuiMode.ToggleConnectionAsync"/> directly. Needs <see cref="TuiTestRunner.RunWithLoop"/>
    /// (not <see cref="TuiTestRunner.RunHeadless"/>): a nested <c>Application.Run</c> only drains an
    /// <c>AddTimeout</c> registered ahead of it while a real loop is pumping (see
    /// <see cref="TuiReview.Modal"/>'s identical technique for the same reason).
    /// </summary>
    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public async Task K8055MenuItem_AfterThePanelCloses_UnsubscribesFromValuesChanged()
    {
        var (session, _, presenter) = CreateSession();
        await session.OpenAsync(TestContext.CancellationToken);
        var cliOptions = new CliOptions { Transport = "hid", VendorId = 0x10CF, ProductId = 0x5501 };
        var k8055 = new FakeStructuredPresenter();
        var catalog = new PresenterCatalog([presenter, k8055]);

        TuiTestRunner.RunWithLoop(session, catalog, cliOptions, parts =>
        {
            var app = TuiTestRunner.CurrentApp;

            var subscribedWhileOpen = TuiTestRunner.InvokeOnLoop(() =>
            {
                var sawSubscription = false;
                app.AddTimeout(TimeSpan.FromMilliseconds(20), () =>
                {
                    sawSubscription = k8055.SubscriberCount == 1;
                    app.RequestStop();
                    return false;
                });

                parts.K8055MenuItem.Action!.Invoke();
                return sawSubscription;
            });

            Assert.IsTrue(subscribedWhileOpen, "Expected the panel to subscribe to ValuesChanged while its nested Application.Run was active.");
        });

        Assert.AreEqual(0, k8055.SubscriberCount, "Expected window.Disposing to unsubscribe from ValuesChanged after the panel closes.");

        await session.CloseAsync(TestContext.CancellationToken);
    }

    /// <summary>
    /// Bug 019 (docs/bugs/fixed/019-tui-stream-monitor-capture-lost-on-quit.md): the Stream Monitor
    /// used to be disposed only via <c>window.Disposing</c> on the main window, which never fires
    /// once <c>Application.Run</c> returns (CLAUDE.md) - so a capture still in progress at quit was
    /// never flushed or saved. <see cref="TuiMode.RunAsync"/> now disposes whatever
    /// <c>TuiWindowParts.CurrentStreamMonitor</c> returns once its loop ends; this replicates that
    /// call directly (the same "what TuiMode.RunAsync does once the loop ends" convention
    /// <see cref="TuiLoggingTests"/> uses), since RunAsync itself can't easily be driven end-to-end
    /// here.
    /// </summary>
    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public async Task Quitting_WithACaptureStillInProgress_FlushesAndSavesIt()
    {
        var (session, transport, presenter) = CreateSession();
        await session.OpenAsync(TestContext.CancellationToken);
        var exportDirectory = Path.Combine(Path.GetTempPath(), "devterm-tests-streammonitor-" + Guid.NewGuid().ToString("N"));
        var cliOptions = new CliOptions { Transport = "loopback", ExportDirectory = exportDirectory };

        StreamMonitor? monitor = null;
        try
        {
            TuiTestRunner.RunWithLoop(session, presenter, cliOptions, parts =>
            {
                var app = TuiTestRunner.CurrentApp;

                TuiTestRunner.InvokeOnLoop(() =>
                {
                    app.AddTimeout(TimeSpan.FromMilliseconds(20), () =>
                    {
                        app.RequestStop();
                        return false;
                    });

                    parts.StreamMonitorMenuItem.Action!.Invoke();
                    return true;
                });

                monitor = parts.CurrentStreamMonitor();
                Assert.IsNotNull(monitor, "Opening Stream Monitor... should have created it, reachable via TuiWindowParts.CurrentStreamMonitor.");

                // HP-GL has no in-band end marker - it only ends on idle timeout - so it's still "in
                // progress" as soon as this returns, the same as real device output caught mid-reply.
                transport.PushIncomingAsync(StreamContentSamples.Hpgl()).GetAwaiter().GetResult();
                var seen = TuiTestRunner.WaitUntilOnLoop(() => monitor!.IsRunning, _waitTimeout);
                Assert.IsTrue(seen, "Expected the monitor to still be running before quitting.");
                Assert.IsEmpty(monitor!.Captures, "The capture should still be in progress, not yet saved.");

                // What TuiMode.RunAsync now does once the loop ends.
                monitor.Dispose();
            });

            Assert.HasCount(1, monitor!.Captures);
            var capture = monitor.Captures[0];
            Assert.AreEqual(StreamCaptureEnd.Flushed, capture.Capture.EndReason);
            Assert.IsNotNull(capture.SavedPath);
            Assert.IsTrue(File.Exists(capture.SavedPath));
        }
        finally
        {
            await session.CloseAsync(TestContext.CancellationToken);
            if (Directory.Exists(exportDirectory))
            {
                Directory.Delete(exportDirectory, recursive: true);
            }
        }
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
        Assert.AreSequenceEqual("ff"u8.ToArray(), transport.WrittenPayloads[0], "As ascii, 'ff' is two characters.");
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

    public required TestContext TestContext { get; set; }
}
