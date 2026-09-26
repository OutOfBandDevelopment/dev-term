using System.Net;
using System.Net.Sockets;
using System.Text;
using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;
using DevTerm.Presenters.Text;
using DevTerm.Test.Utilities;
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
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class TuiModeSwitchProfileTests
{
    private static readonly TimeSpan _waitTimeout = TimeSpan.FromSeconds(15);

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
        await session.OpenAsync(TestContext.CancellationToken);
        var cliOptions = new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "1", Presenter = ["ascii"] };

        TuiTestRunner.RunWithLoop(session, presenter, cliOptions, parts =>
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var acceptTask = listener.AcceptTcpClientAsync(TestContext.CancellationToken);

            var switched = parts.SwitchProfileAsync(new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = port.ToString(), Presenter = ["hex"] })
                .GetAwaiter().GetResult();

            using var client = acceptTask.GetAwaiter().GetResult();
            using var stream = client.GetStream();

            Assert.IsTrue(switched);
            var titleUpdated = TuiTestRunner.WaitUntilOnLoop(() => parts.Window.Title.Contains($"tcp://127.0.0.1:{port}"), _waitTimeout);
            Assert.IsTrue(titleUpdated, "Expected the window title to reflect the newly-switched-to connection.");
            Assert.Contains("hex", TuiTestRunner.InvokeOnLoop(() => parts.Window.Title));
            Assert.AreEqual("_Disconnect", TuiTestRunner.InvokeOnLoop(() => parts.ConnectMenuItem.Title));
            Assert.IsTrue(TuiTestRunner.InvokeOnLoop(() => parts.SendField.Enabled));
            Assert.Contains("Switched to", TuiTestRunner.InvokeOnLoop(() => parts.Output.Text));

            stream.Write(Encoding.ASCII.GetBytes("AB"));
            var appeared = TuiTestRunner.WaitUntilOnLoop(() => parts.Output.Text.Contains("[hex]"), _waitTimeout);
            Assert.IsTrue(appeared, "Expected the new (real TCP) session's incoming bytes to reach the output.");
        });
    }

    [TestMethod]
    public async Task SwitchProfileAsync_SupersededByAnotherSwitchBeforeItResolves_DoesNotStompTheNewerOne()
    {
        // Reproduces "I tried connecting to 192.168.0.108 and it failed, so I tried 192.168.0.107
        // and it won't even try to connect now": a first attempt that's still pending (here, a TCP
        // listener mode that never gets a client, standing in for a host that never actively
        // refuses - the real case was an unreachable LAN IP sitting on the OS connect timeout) must
        // not have its eventual failure/cancellation reset connectMenuItem.Title/sendField.Enabled/
        // output after a second, newer switch has already established its own, real connection.
        var (session, _, presenter) = CreateSession();
        await session.OpenAsync(TestContext.CancellationToken);
        var cliOptions = new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "1", Presenter = ["ascii"] };

        TuiTestRunner.RunWithLoop(session, presenter, cliOptions, parts =>
        {
            using var neverConnectedTo = new TcpListener(IPAddress.Loopback, 0);
            neverConnectedTo.Start();
            var pendingPort = ((IPEndPoint)neverConnectedTo.LocalEndpoint).Port;

            // TcpTransportMode.Listener: AcceptAsync blocks until a client connects - nobody ever
            // does, so this stays "Opening" indefinitely until cancelled, a deterministic stand-in
            // for a slow-to-fail connect.
            var staleTask = parts.SwitchProfileAsync(new CliOptions { Transport = "tcp", Listen = true, Port = pendingPort.ToString(), Presenter = ["ascii"] });

            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var acceptTask = listener.AcceptTcpClientAsync(TestContext.CancellationToken);

            var switched = parts.SwitchProfileAsync(new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = port.ToString(), Presenter = ["hex"] })
                .GetAwaiter().GetResult();
            using var client = acceptTask.GetAwaiter().GetResult();

            Assert.IsTrue(switched, "The second (newer) switch should have connected for real.");
            Assert.AreEqual("_Disconnect", TuiTestRunner.InvokeOnLoop(() => parts.ConnectMenuItem.Title));
            Assert.IsTrue(TuiTestRunner.InvokeOnLoop(() => parts.SendField.Enabled));
            Assert.Contains($"tcp://127.0.0.1:{port}", TuiTestRunner.InvokeOnLoop(() => parts.Window.Title));

            // The stale (superseded) attempt should resolve false - cancelled, not left hanging -
            // without ever having touched the UI state the newer attempt already set.
            var staleResult = staleTask.GetAwaiter().GetResult();
            Assert.IsFalse(staleResult);
            Assert.AreEqual("_Disconnect", TuiTestRunner.InvokeOnLoop(() => parts.ConnectMenuItem.Title), "The stale attempt's resolution must not have reverted the menu title.");
            Assert.IsTrue(TuiTestRunner.InvokeOnLoop(() => parts.SendField.Enabled), "The stale attempt's resolution must not have disabled the send field.");
            Assert.Contains($"tcp://127.0.0.1:{port}", TuiTestRunner.InvokeOnLoop(() => parts.Window.Title));
        });
    }

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public async Task CurrentSession_AfterASwitch_IsTheSwitchedToSessionNotTheOriginal()
    {
        // Regression test for bug 018: TuiMode.RunAsync closes/disposes its own `session` parameter
        // when the loop ends, which SwitchProfileAsync already closed/disposed as the *old* session
        // during the switch - leaving whatever session is actually current (the switched-to one)
        // never closed or disposed. TuiWindowParts.CurrentSession is what RunAsync must use instead.
        var (session, _, presenter) = CreateSession();
        await session.OpenAsync(TestContext.CancellationToken);
        var cliOptions = new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "1", Presenter = ["ascii"] };

        Session? currentSession = null;
        TcpClient? client = null;
        TuiTestRunner.RunWithLoop(session, presenter, cliOptions, parts =>
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var acceptTask = listener.AcceptTcpClientAsync(TestContext.CancellationToken);

            var switched = parts.SwitchProfileAsync(new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = port.ToString(), Presenter = ["ascii"] })
                .GetAwaiter().GetResult();
            Assert.IsTrue(switched);
            client = acceptTask.GetAwaiter().GetResult();

            currentSession = parts.CurrentSession();
        });

        Assert.IsFalse(ReferenceEquals(currentSession, session), "The current session after a switch must be the switched-to one, not the original.");
        Assert.AreEqual(ConnectionState.Open, currentSession!.State, "The switched-to session should still be open - RunAsync (not this test) is what closes it.");

        // What RunAsync now does once the loop ends: close/dispose whichever session is CURRENT.
        await currentSession.CloseAsync(TestContext.CancellationToken);
        await currentSession.DisposeAsync();
        Assert.AreEqual(ConnectionState.Closed, currentSession.State);
        client?.Dispose();
    }

    [TestMethod]
    public async Task SwitchProfileAsync_WithAnUnknownPresenter_ReportsAndKeepsTheOldSessionUnaffected()
    {
        var (session, _, presenter) = CreateSession();
        await session.OpenAsync(TestContext.CancellationToken);
        var cliOptions = new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "23", Presenter = ["ascii"] };

        TuiTestRunner.RunWithLoop(session, presenter, cliOptions, parts =>
        {
            var switched = parts.SwitchProfileAsync(new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "23", Presenter = ["not-a-real-presenter"] })
                .GetAwaiter().GetResult();

            Assert.IsFalse(switched);
            Assert.AreEqual(ConnectionState.Open, session.State, "The original session should be untouched when the new profile fails to resolve a presenter.");
            var reported = TuiTestRunner.WaitUntilOnLoop(() => parts.Output.Text.Contains("Could not switch profile"), _waitTimeout);
            Assert.IsTrue(reported);
        });

        await session.CloseAsync(TestContext.CancellationToken);
    }


    [TestMethod]
    public async Task SwitchProfileAsync_ToASavedProfile_RetitlesTheWindowWithItsName()
    {
        var (session, _, presenter) = CreateSession();
        await session.OpenAsync(TestContext.CancellationToken);
        var directory = Path.Combine(Path.GetTempPath(), $"devterm-tests-{Guid.NewGuid():N}");
        try
        {
            var cliOptions = new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "1", Presenter = ["ascii"] };

            TuiTestRunner.RunWithLoop(session, presenter, cliOptions, parts =>
            {
                Assert.Contains("tcp://127.0.0.1:1", TuiTestRunner.InvokeOnLoop(() => parts.Window.Title));

                using var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                var acceptTask = listener.AcceptTcpClientAsync(TestContext.CancellationToken);
                var target = new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = port.ToString(), Presenter = ["hex"] };
                new ConnectionProfileStore(directory).Save("bench-scope", target);

                var switched = parts.SwitchProfileAsync(target).GetAwaiter().GetResult();
                using var client = acceptTask.GetAwaiter().GetResult();

                Assert.IsTrue(switched);
                var retitled = TuiTestRunner.WaitUntilOnLoop(() => parts.Window.Title.Contains("bench-scope"), _waitTimeout);
                Assert.IsTrue(retitled, "Expected the title to name the saved profile just switched to.");
                Assert.IsFalse(TuiTestRunner.InvokeOnLoop(() => parts.Window.Title.Contains("tcp://")));
            }, new ConnectionProfileStore(directory));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    public required TestContext TestContext { get; set; }
}
