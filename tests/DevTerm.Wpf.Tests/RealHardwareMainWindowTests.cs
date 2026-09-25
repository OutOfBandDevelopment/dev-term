using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Presenters.Text;
using DevTerm.Test.Utilities;
using DevTerm.Transports.Tcp;
using Microsoft.Extensions.Options;

namespace DevTerm.Wpf.Tests;

/// <summary>
/// Opt-in <see cref="MainWindow"/> automation against real hardware — same device and same
/// <c>.runsettings</c>-based opt-in as <c>DevTerm.Console.Tests.RealHardwareCliTests</c>, using a
/// real <see cref="TcpTransport"/> instead of <see cref="FakeTransport"/>. Reports
/// <see cref="Assert.Inconclusive(string)"/> (not a failure) when run without a settings file.
/// </summary>
[TestCategory(TestCategories.DevLocal)]
[TestClass]
[DoNotParallelize]
public sealed class RealHardwareMainWindowTests
{
    public TestContext TestContext { get; set; } = null!;

    private static readonly TimeSpan _pumpTimeout = TimeSpan.FromSeconds(10);

    [TestMethod]
    [DataRow("RealTcpDeviceHost3")]
    [DataRow("RealTcpDeviceHost2")]
    public async Task MainWindow_AgainstRealDevice_ReceivesDecodedIdReply(string hostParameterName)
    {
        var host = TestContext.Properties.ContainsKey(hostParameterName) ? TestContext.Properties[hostParameterName] as string : null;
        var portText = TestContext.Properties.ContainsKey("RealTcpDevicePort") ? TestContext.Properties["RealTcpDevicePort"] as string : null;
        if (string.IsNullOrEmpty(host) || !int.TryParse(portText, out var port))
        {
            Assert.Inconclusive($"No '{hostParameterName}'/'RealTcpDevicePort' — run with a settings file (see devterm.runsettings) to exercise this against real hardware.");
            return;
        }

        if (!await RealDeviceReachability.IsTcpReachableAsync(host, port, TestContext.CancellationToken))
        {
            Assert.Inconclusive($"Real device at {host}:{port} is not reachable (TCP connect attempt timed out/refused within {RealDeviceReachability.DefaultTimeout.TotalSeconds}s) — is it powered on and networked?");
            return;
        }

        StaTestRunner.Run(async () =>
        {
            var transport = new TcpTransport(
                new SystemTcpConnectionSource(),
                Microsoft.Extensions.Options.Options.Create(new TcpTransportOptions { Mode = TcpTransportMode.Client, Host = host, Port = port }));
            var presenter = new AsciiPresenter(Microsoft.Extensions.Options.Options.Create(new AsciiPresenterOptions()));
            var session = new Session(transport, new Pipeline([presenter]));
            var window = new MainWindow(session, new PresenterCatalog([presenter]), new CliOptions { Transport = "tcp", Host = host, Port = port.ToString(), LineEnding = LineEnding.Cr, Parser = "ascii" })
            {
                ShowInTaskbar = false,
            };

            await window.ConnectAsync();
            window.SendBox.Text = "ID?";
            await window.SendCurrentInputAsync();

            var appeared = StaTestRunner.PumpUntil(() => window.OutputList.Items.Count > 0, _pumpTimeout);

            Assert.IsTrue(appeared, $"Expected a decoded reply from the real device at {host}:{port}.");
            Assert.Contains("TEK/2230", (string)window.OutputList.Items[0]!);

            await session.CloseAsync(TestContext.CancellationToken);
            await session.DisposeAsync();
        });
    }
}
