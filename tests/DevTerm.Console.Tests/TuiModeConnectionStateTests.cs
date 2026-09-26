using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Presenters.Text;
using DevTerm.Test.Utilities;
using Microsoft.Extensions.Options;

namespace DevTerm.Console.Tests;

/// <summary>
/// The TUI main window's connection-state UI: the colored status line, the title's disconnected
/// suffix, which Device menu items are enabled, and tagged status/error lines in the output pane.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class TuiModeConnectionStateTests
{
    public required TestContext TestContext { get; set; }

    private static CliOptions Tcp() => new() { Transport = "tcp", Host = "127.0.0.1", Port = "1", Presenter = ["ascii"] };

    private static (Session Session, FakeTransport Transport, AsciiPresenter Ascii) CreateSession()
    {
        var transport = new FakeTransport();
        var ascii = new AsciiPresenter(Options.Create(new AsciiPresenterOptions()));
        return (new Session(transport, new Pipeline([ascii])), transport, ascii);
    }

    [TestMethod]
    public async Task Connected_StatusLineTitleAndDeviceMenuReflectIt()
    {
        var (session, _, ascii) = CreateSession();
        await session.OpenAsync(TestContext.CancellationToken);

        TuiTestRunner.RunHeadless(session, ascii, Tcp(), parts =>
        {
            Assert.AreEqual(" ● Connected — tcp://127.0.0.1:1", parts.StatusLabel.Text);
            Assert.DoesNotContain("disconnected", parts.Window.Title);
            Assert.IsTrue(parts.ScpiMenuItem.Enabled, "SCPI makes sense over TCP.");
            Assert.IsFalse(parts.K8055MenuItem.Enabled, "The K8055 is a HID device.");
            Assert.IsFalse(parts.BusylightMenuItem.Enabled);
        });
    }

    [TestMethod]
    public void NotConnected_StatusLineTitleAndDeviceMenuReflectIt_AndTheStartupErrorIsTagged()
    {
        var (session, _, ascii) = CreateSession();

        TuiTestRunner.RunHeadlessApp(app =>
        {
            var parts = TuiMode.BuildWindow(app, session, TuiTestRunner.CatalogFor(ascii, Tcp()), Tcp(), TuiTestRunner.EmptyProfiles(), "Could not open the connection: refused.");

            Assert.AreEqual(" ● Disconnected — tcp://127.0.0.1:1", parts.StatusLabel.Text);
            Assert.EndsWith(" — disconnected", parts.Window.Title);
            Assert.IsFalse(parts.ScpiMenuItem.Enabled, "No panel makes sense while disconnected.");
            StringAssert.StartsWith(parts.Output.Text, "[error] Could not open the connection");
        });
    }

    [TestMethod]
    public async Task AK8055Connection_EnablesOnlyTheK8055Panel()
    {
        var (session, _, ascii) = CreateSession();
        await session.OpenAsync(TestContext.CancellationToken);
        var options = new CliOptions { Transport = "hid", VendorId = 0x10CF, ProductId = 0x5501, Presenter = ["ascii"] };

        TuiTestRunner.RunHeadless(session, ascii, options, parts =>
        {
            Assert.IsTrue(parts.K8055MenuItem.Enabled);
            Assert.IsFalse(parts.ScpiMenuItem.Enabled);
        });
    }

    [TestMethod]
    public async Task LostConnection_FlipsTheStatusLineAndTitle()
    {
        var (session, transport, ascii) = CreateSession();
        await session.OpenAsync(TestContext.CancellationToken);

        TuiTestRunner.RunWithLoop(session, ascii, Tcp(), parts =>
        {
            transport.FailReadAsync(new IOException("socket reset")).GetAwaiter().GetResult();

            (string Status, string Title, string Output) Snapshot()
            {
                (string, string, string) result = default;
                using var done = new ManualResetEventSlim(false);
                TuiTestRunner.CurrentApp.Invoke(() =>
                {
                    result = (parts.StatusLabel.Text, parts.Window.Title, parts.Output.Text);
                    done.Set();
                });
                done.Wait(TimeSpan.FromSeconds(5));
                return result;
            }

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            var state = Snapshot();
            while (!state.Status.Contains("Disconnected", StringComparison.Ordinal) && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(20);
                state = Snapshot();
            }

            StringAssert.Contains(state.Status, "Disconnected");
            Assert.EndsWith(" — disconnected", state.Title);
            StringAssert.Contains(state.Output, "[error] Connection lost: socket reset");
        });
    }

    [TestMethod]
    public void StatusAndErrorLines_AreTagged() =>
        Assert.AreEqual(("[dev-term] Connected.", "[error] Nope."), (TuiMode.StatusLine("Connected."), TuiMode.ErrorLine("Nope.")));
}
