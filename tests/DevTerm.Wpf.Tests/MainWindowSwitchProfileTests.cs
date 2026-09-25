using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Presenters.Text;

namespace DevTerm.Wpf.Tests;

/// <summary>
/// <see cref="MainWindow.SwitchProfileAsync"/> always composes its new session through
/// <see cref="DevTermSessionBuilder"/> — a real transport, never a <see cref="FakeTransport"/> (that
/// builder has no seam to inject one, by design: it's the same composition path
/// <c>AddDevTermFrontEnd</c> uses for the app's real startup). So exercising a real switch needs a
/// real local TCP loopback socket, same reasoning as <c>DevTerm.Console.Tests.ConsoleAppCliTests</c>
/// — no real hardware, but a real transport and a real process-external listener, hence
/// <c>INTEGRATION</c> rather than <c>UNIT</c> like <see cref="MainWindowTests"/>.
///
/// Doesn't test the failure path (switching to an unreachable profile): that path calls the real
/// <c>MessageBox.Show</c>, a blocking modal with no automated way to dismiss it — the same
/// untested-by-necessity gap <c>ConnectAsync</c>/<c>ToggleConnectionAsync</c>'s own failure paths
/// already have.
/// </summary>
[TestCategory("INTEGRATION")]
[TestClass]
[DoNotParallelize]
public sealed class MainWindowSwitchProfileTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(15);

    [TestMethod]
    public void SwitchProfileAsync_ToAWorkingProfile_ClosesOldSessionAndOpensNew()
    {
        StaTestRunner.Run(async () =>
        {
            var initialTransport = new FakeTransport();
            var initialPresenter = new AsciiPresenter(Microsoft.Extensions.Options.Options.Create(new AsciiPresenterOptions()));
            var initialSession = new Session(initialTransport, new Pipeline([initialPresenter]));
            var window = new MainWindow(initialSession, new PresenterCatalog([initialPresenter]), new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "1", Parser = "ascii" },
                IsolatedProfiles.Empty())
            {
                ShowInTaskbar = false,
            };
            await window.ConnectAsync();
            Assert.IsTrue(window.SendBox.IsEnabled);

            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var acceptTask = listener.AcceptTcpClientAsync(TestContext.CancellationToken);

            var switched = await window.SwitchProfileAsync(new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = port.ToString(), Presenter = ["hex"] });

            using var client = await acceptTask.AsTask().WaitAsync(_timeout, TestContext.CancellationToken);
            using var stream = client.GetStream();

            Assert.IsTrue(switched);
            Assert.Contains($"tcp://127.0.0.1:{port}", window.Title);
            Assert.Contains("hex", window.Title);
            Assert.AreEqual("_Disconnect", window.ConnectMenuItem.Header);
            Assert.AreEqual(1, window.OutputList.Items.Count, "Old output should be cleared; only the 'Switched to ...' line should remain.");
            Assert.Contains("Switched to", (string)window.OutputList.Items[0]!);

            await stream.WriteAsync(Encoding.ASCII.GetBytes("AB"), TestContext.CancellationToken);
            var appeared = StaTestRunner.PumpUntil(() => window.OutputList.Items.Count > 1, _timeout);
            Assert.IsTrue(appeared, "Expected the new (real TCP) session's incoming bytes to reach the output list.");
            Assert.Contains("[hex]", (string)window.OutputList.Items[1]!);
        });
    }


    [TestMethod]
    public void SwitchProfileAsync_ToASavedProfile_RetitlesTheWindowWithItsName()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"devterm-tests-{Guid.NewGuid():N}");
        try
        {
            StaTestRunner.Run(async () =>
            {
                var initialTransport = new FakeTransport();
                var initialPresenter = new AsciiPresenter(Microsoft.Extensions.Options.Options.Create(new AsciiPresenterOptions()));
                var initialSession = new Session(initialTransport, new Pipeline([initialPresenter]));
                var window = new MainWindow(
                    initialSession,
                    new PresenterCatalog([initialPresenter]),
                    new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "1", Parser = "ascii" },
                    new ConnectionProfileStore(directory))
                {
                    ShowInTaskbar = false,
                };
                await window.ConnectAsync();
                Assert.Contains("tcp://127.0.0.1:1", window.Title);

                using var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                var acceptTask = listener.AcceptTcpClientAsync(TestContext.CancellationToken);
                var target = new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = port.ToString(), Presenter = ["hex"] };
                new ConnectionProfileStore(directory).Save("bench-scope", target);

                var switched = await window.SwitchProfileAsync(target);
                using var client = await acceptTask.AsTask().WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationToken);

                Assert.IsTrue(switched);
                Assert.Contains("bench-scope", window.Title);
                Assert.DoesNotContain("tcp://", window.Title, "A saved profile is titled by name, not by its connection string.");
            });
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    public TestContext TestContext { get; set; }
}
