using System.IO;
using System.Text;
using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;
using DevTerm.Presenters.Text;
using DevTerm.Test.Utilities;

namespace DevTerm.Wpf.Tests;

/// <summary>
/// The GUI never crashes or closes itself over an error: a failed connect leaves the window open
/// and disconnected with the error shown; invalid typed input is rejected with the connection left
/// alone; a lost connection (send or read failure) is reported and the window flips to
/// "disconnected", ready for File > Connect. Never shows or closes the window (see
/// <see cref="MainWindowTests"/>' remarks for why), so no real modal can block a test.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class MainWindowErrorHandlingTests
{
    private static (MainWindow Window, FakeTransport Transport) CreateWindow(string parser = "ascii")
    {
        var transport = new FakeTransport();
        var ascii = new AsciiPresenter(Microsoft.Extensions.Options.Options.Create(new AsciiPresenterOptions()));
        var session = new Session(transport, new Pipeline([ascii]));
        var window = new MainWindow(
            session,
            new PresenterCatalog([ascii, new HexPresenter()]),
            new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "23", Presenter = ["ascii"], Parser = parser },
            IsolatedProfiles.Empty())
        {
            ShowInTaskbar = false,
        };
        return (window, transport);
    }

    private static string Output(MainWindow window) => string.Join("\n", window.OutputList.Items.Cast<object>());

    [TestMethod]
    public void ConnectFails_WindowStaysDisconnectedWithTheErrorShown_ThenConnectRetries()
    {
        StaTestRunner.Run(async () =>
        {
            var (window, transport) = CreateWindow();
            transport.FailNextOpen(new IOException("connection refused"));

            await window.ConnectAsync();

            StringAssert.Contains(Output(window), "connection refused");
            Assert.IsFalse(window.SendBox.IsEnabled);
            Assert.AreEqual("_Connect", window.ConnectMenuItem.Header);

            await window.ToggleConnectionAsync();

            Assert.IsTrue(window.SendBox.IsEnabled);
            Assert.AreEqual("_Disconnect", window.ConnectMenuItem.Header);
        });
    }

    [TestMethod]
    public void InvalidHexInput_IsRejected_NothingSent_StillConnected()
    {
        StaTestRunner.Run(async () =>
        {
            var (window, transport) = CreateWindow(parser: "hex");
            await window.ConnectAsync();

            window.SendBox.Text = "OUTPut?";
            await window.SendCurrentInputAsync();

            StringAssert.Contains(Output(window), "isn't valid hex input");
            Assert.IsEmpty(transport.WrittenPayloads);
            Assert.IsTrue(window.SendBox.IsEnabled, "An invalid line must not disconnect.");
        });
    }

    [TestMethod]
    public void SendFails_ReportedOnce_WindowFlipsToDisconnected_AndReconnects()
    {
        StaTestRunner.Run(async () =>
        {
            var (window, transport) = CreateWindow();
            await window.ConnectAsync();

            transport.FailNextWrite(new IOException("device unplugged"));
            window.SendBox.Text = "hello";
            await window.SendCurrentInputAsync();

            Assert.IsTrue(StaTestRunner.PumpUntil(() => !window.SendBox.IsEnabled, TimeSpan.FromSeconds(5)), "The window should flip to disconnected.");
            var output = Output(window);
            Assert.AreEqual(1, output.Split("device unplugged").Length - 1, "Reported exactly once.");
            Assert.AreEqual("_Connect", window.ConnectMenuItem.Header);

            await window.ToggleConnectionAsync();
            window.SendBox.Text = "again";
            await window.SendCurrentInputAsync();

            Assert.AreEqual("again", Encoding.ASCII.GetString(transport.WrittenPayloads.Single()));
        });
    }

    [TestMethod]
    public void ReadFails_ReportedAndWindowFlipsToDisconnected()
    {
        StaTestRunner.Run(async () =>
        {
            var (window, transport) = CreateWindow();
            await window.ConnectAsync();

            await transport.FailReadAsync(new IOException("socket reset"));

            Assert.IsTrue(StaTestRunner.PumpUntil(() => !window.SendBox.IsEnabled, TimeSpan.FromSeconds(5)), "The window should flip to disconnected.");
            StringAssert.Contains(Output(window), "socket reset");
            Assert.AreEqual(ConnectionState.Closed, transport.State);
        });
    }
}
