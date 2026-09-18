using System.Net;
using System.Net.Sockets;
using System.Text;
using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;
using DevTerm.Presenters.Text;
using Microsoft.Extensions.Options;

namespace DevTerm.Console.Tests;

/// <summary>
/// <c>TuiWindowParts.SwitchProfileAsync</c> always composes its new session through
/// <see cref="DevTermSessionBuilder"/> — a real transport, never a <see cref="FakeTransport"/> (no
/// seam to inject one, by design: it's the same composition path <c>AddDevTermFrontEnd</c> uses for
/// the app's real startup). Exercising a real switch needs a real local TCP loopback socket, same
/// reasoning as <c>ConsoleAppCliTests</c> and <c>DevTerm.Wpf.Tests.MainWindowSwitchProfileTests</c>
/// — no real hardware, but a real transport, hence <c>INTEGRATION</c> rather than <c>UNIT</c> like
/// <see cref="TuiModeTests"/>. Uses <see cref="TuiTestRunner.RunWithLoop"/>, not
/// <see cref="TuiTestRunner.RunHeadless"/>: the switch calls <c>Application.Invoke</c> internally
/// (same as <c>ToggleConnectionAsync</c>), which never flushes without a real, actively-pumping
/// <c>Application.Run()</c> loop — see <c>CLAUDE.md</c>.
/// </summary>
[TestCategory("INTEGRATION")]
[TestClass]
[DoNotParallelize]
public sealed class TuiModeSwitchProfileTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(15);

    private static (Session Session, FakeTransport Transport, IPresenter Presenter) CreateSession()
    {
        var transport = new FakeTransport();
        var presenter = new AsciiPresenter(Options.Create(new AsciiPresenterOptions()));
        var session = new Session(transport, new Pipeline([presenter]));
        return (session, transport, presenter);
    }

    [TestMethod]
    public async Task SwitchProfileAsync_ToAWorkingProfile_ClosesOldSessionAndOpensNew()
    {
        var (session, _, presenter) = CreateSession();
        await session.OpenAsync();
        var cliOptions = new CliOptions { Transport = "tcp", Host = "127.0.0.1", TcpPort = 1, Presenter = ["ascii"] };

        TuiTestRunner.RunWithLoop(session, presenter, cliOptions, parts =>
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var acceptTask = listener.AcceptTcpClientAsync();

            var switched = parts.SwitchProfileAsync(new CliOptions { Transport = "tcp", Host = "127.0.0.1", TcpPort = port, Presenter = ["hex"] })
                .GetAwaiter().GetResult();

            using var client = acceptTask.GetAwaiter().GetResult();
            using var stream = client.GetStream();

            Assert.IsTrue(switched);
            var titleUpdated = TuiTestRunner.WaitUntilOnLoop(() => parts.Window.Title.Contains($"TCP 127.0.0.1:{port}"), WaitTimeout);
            Assert.IsTrue(titleUpdated, "Expected the window title to reflect the newly-switched-to connection.");
            StringAssert.Contains(TuiTestRunner.InvokeOnLoop(() => parts.Window.Title), "hex");
            Assert.AreEqual("_Disconnect", TuiTestRunner.InvokeOnLoop(() => parts.ConnectMenuItem.Title));
            Assert.IsTrue(TuiTestRunner.InvokeOnLoop(() => parts.SendField.Enabled));
            StringAssert.Contains(TuiTestRunner.InvokeOnLoop(() => parts.Output.Text), "Switched to");

            stream.Write(Encoding.ASCII.GetBytes("AB"));
            var appeared = TuiTestRunner.WaitUntilOnLoop(() => parts.Output.Text.Contains("[hex]"), WaitTimeout);
            Assert.IsTrue(appeared, "Expected the new (real TCP) session's incoming bytes to reach the output.");
        });
    }

    [TestMethod]
    public async Task SwitchProfileAsync_WithAnUnknownPresenter_ReportsAndKeepsTheOldSessionUnaffected()
    {
        var (session, _, presenter) = CreateSession();
        await session.OpenAsync();
        var cliOptions = new CliOptions { Transport = "tcp", Host = "127.0.0.1", TcpPort = 23, Presenter = ["ascii"] };

        TuiTestRunner.RunWithLoop(session, presenter, cliOptions, parts =>
        {
            var switched = parts.SwitchProfileAsync(new CliOptions { Transport = "tcp", Host = "127.0.0.1", TcpPort = 23, Presenter = ["not-a-real-presenter"] })
                .GetAwaiter().GetResult();

            Assert.IsFalse(switched);
            Assert.AreEqual(ConnectionState.Open, session.State, "The original session should be untouched when the new profile fails to resolve a presenter.");
            var reported = TuiTestRunner.WaitUntilOnLoop(() => parts.Output.Text.Contains("Could not switch profile"), WaitTimeout);
            Assert.IsTrue(reported);
        });

        await session.CloseAsync();
    }
}
