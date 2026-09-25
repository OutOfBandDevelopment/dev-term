using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;
using DevTerm.Presenters.Text;
using DevTerm.Test.Utilities;
using Microsoft.Extensions.Options;

namespace DevTerm.Console.Tests;

/// <summary>
/// The TUI never crashes or exits over an error: a failed startup connect opens the TUI
/// disconnected with the error shown; invalid typed input is rejected (connection left alone); a
/// lost connection is reported and flips the window to "disconnected", ready for File > Connect.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class TuiModeErrorHandlingTests
{
    public required TestContext TestContext { get; set; }

    private static (Session Session, FakeTransport Transport, AsciiPresenter Ascii) CreateSession()
    {
        var transport = new FakeTransport();
        var ascii = new AsciiPresenter(Options.Create(new AsciiPresenterOptions()));
        return (new Session(transport, new Pipeline([ascii])), transport, ascii);
    }

    [TestMethod]
    public async Task SendAsync_InvalidHex_IsReportedAndNotSent()
    {
        var (session, transport, _) = CreateSession();
        await session.OpenAsync(TestContext.CancellationToken);
        var output = new List<string>();

        await TuiMode.SendAsync(session, new CliOptions { Presenter = ["hex"] }, new HexPresenter(), "OUTPut?", output.Add, "hex");

        Assert.HasCount(1, output);
        StringAssert.Contains(output[0], "isn't valid hex input");
        Assert.IsEmpty(transport.WrittenPayloads);
        Assert.AreEqual(ConnectionState.Open, session.State);
    }

    [TestMethod]
    public async Task SendAsync_DeviceFailure_DisconnectsAndLeavesReportingToTheDisconnectedHandler()
    {
        var (session, transport, ascii) = CreateSession();
        await session.OpenAsync(TestContext.CancellationToken);
        var reported = new List<string>();
        session.Disconnected += (_, e) => reported.Add(e.Message);
        var output = new List<string>();
        transport.FailNextWrite(new IOException("device unplugged"));

        await TuiMode.SendAsync(session, new CliOptions(), ascii, "hello", output.Add);

        Assert.IsEmpty(output, "Not reported a second time by the send path.");
        Assert.HasCount(1, reported);
        Assert.AreEqual(ConnectionState.Closed, session.State);
    }

    [TestMethod]
    public void BuildWindow_WithAStartupError_ShowsItDisconnected()
    {
        var (session, _, ascii) = CreateSession();
        var cliOptions = new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "1", Presenter = ["ascii"] };

        TuiTestRunner.RunHeadlessApp(app =>
        {
            var parts = TuiMode.BuildWindow(app, session, TuiTestRunner.CatalogFor(ascii, cliOptions), cliOptions, TuiTestRunner.EmptyProfiles(), "Could not open the connection: refused.");

            StringAssert.Contains(parts.Output.Text, "refused");
            Assert.IsFalse(parts.SendField.Enabled);
            Assert.AreEqual("_Connect", parts.ConnectMenuItem.Title);
        });
    }

    [TestMethod]
    public async Task ReadFailure_IsReportedAndTheWindowFlipsToDisconnected()
    {
        var (session, transport, ascii) = CreateSession();
        await session.OpenAsync(TestContext.CancellationToken);
        var cliOptions = new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "1", Presenter = ["ascii"] };

        TuiTestRunner.RunWithLoop(session, ascii, cliOptions, parts =>
        {
            Assert.IsTrue(parts.SendField.Enabled);

            transport.FailReadAsync(new IOException("socket reset")).GetAwaiter().GetResult();

            // The Editor only allows access from the UI thread - read everything through the loop.
            (string Text, bool Enabled, string Title) Snapshot()
            {
                (string, bool, string) result = default;
                using var done = new ManualResetEventSlim(false);
                TuiTestRunner.CurrentApp.Invoke(() =>
                {
                    result = (parts.Output.Text, parts.SendField.Enabled, parts.ConnectMenuItem.Title);
                    done.Set();
                });
                done.Wait(TimeSpan.FromSeconds(5));
                return result;
            }

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            var state = Snapshot();
            while ((state.Enabled || !state.Text.Contains("socket reset", StringComparison.Ordinal)) && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(20);
                state = Snapshot();
            }

            StringAssert.Contains(state.Text, "socket reset");
            Assert.IsFalse(state.Enabled);
            Assert.AreEqual("_Connect", state.Title);
        });
    }
}
