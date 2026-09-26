using DevTerm.Core.Transports;
using DevTerm.Test.Utilities;

namespace DevTerm.Configuration.Tests;

/// <summary>
/// The shared, front-end-agnostic pieces behind the main windows' connection-state UI: which
/// Device panels are available, the title's disconnected suffix, the status-bar text, and the SCPI
/// auto-detect timeout option.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class MainWindowStateTests
{
    private static CliOptions Hid(int vendorId, int productId) => new() { Transport = "hid", VendorId = vendorId, ProductId = productId };

    [TestMethod]
    [DataRow(0x5500, true)]
    [DataRow(0x5503, true)]
    [DataRow(0x5504, false)]
    public void K8055_OnlyForAK8055BoardAddress(int productId, bool expected) =>
        Assert.AreEqual(expected, DevicePanels.IsAvailable(DevicePanel.K8055, Hid(0x10CF, productId), connected: true));

    [TestMethod]
    public void Busylight_ForTheKnownKuandoIds_NotOtherHidDevices()
    {
        Assert.IsTrue(DevicePanels.IsAvailable(DevicePanel.Busylight, Hid(0x04D8, 0xF848), connected: true));
        Assert.IsTrue(DevicePanels.IsAvailable(DevicePanel.Busylight, Hid(0x27BB, 0x3BCD), connected: true));
        Assert.IsFalse(DevicePanels.IsAvailable(DevicePanel.Busylight, Hid(0x10CF, 0x5500), connected: true));
    }

    [TestMethod]
    public void Scpi_ForAnyTextTransport_NotHid()
    {
        Assert.IsTrue(DevicePanels.IsAvailable(DevicePanel.Scpi, new CliOptions { Transport = "tcp" }, connected: true));
        Assert.IsTrue(DevicePanels.IsAvailable(DevicePanel.Scpi, new CliOptions { Transport = "usbtmc" }, connected: true));
        Assert.IsFalse(DevicePanels.IsAvailable(DevicePanel.Scpi, Hid(0x10CF, 0x5500), connected: true));
    }

    [TestMethod]
    public void Manifest_ForAnyConnection()
    {
        Assert.IsTrue(DevicePanels.IsAvailable(DevicePanel.Manifest, new CliOptions { Transport = "loopback" }, connected: true));
        Assert.IsTrue(DevicePanels.IsAvailable(DevicePanel.Manifest, Hid(0x10CF, 0x5500), connected: true));
    }

    [TestMethod]
    public void NoPanelWhileDisconnected()
    {
        Assert.IsFalse(DevicePanels.IsAvailable(DevicePanel.K8055, Hid(0x10CF, 0x5500), connected: false));
        Assert.IsFalse(DevicePanels.IsAvailable(DevicePanel.Scpi, new CliOptions { Transport = "tcp" }, connected: false));
        Assert.IsFalse(DevicePanels.IsAvailable(DevicePanel.Manifest, new CliOptions { Transport = "loopback" }, connected: false));
    }

    [TestMethod]
    public void WindowTitle_SaysDisconnectedWhenItIs()
    {
        var options = new CliOptions { Transport = "tcp", Host = "192.168.0.110", Port = "23", Presenter = ["ascii"] };
        var store = new ConnectionProfileStore(Path.Combine(Path.GetTempPath(), "devterm-title-tests", Path.GetRandomFileName()));

        Assert.DoesNotContain("disconnected", ConnectionDescription.WindowTitle(options, "ascii", store, connected: true));
        Assert.EndsWith(" — disconnected", ConnectionDescription.WindowTitle(options, "ascii", store, connected: false));
    }

    [TestMethod]
    [DataRow(ConnectionState.Open, "Connected — tcp://192.168.0.110:23")]
    [DataRow(ConnectionState.Opening, "Connecting — tcp://192.168.0.110:23")]
    [DataRow(ConnectionState.Closed, "Disconnected — tcp://192.168.0.110:23")]
    [DataRow(ConnectionState.Faulted, "Disconnected — tcp://192.168.0.110:23")]
    public void StatusText_NamesTheStateAndTheConnection(ConnectionState state, string expected) =>
        Assert.AreEqual(expected, ConnectionDescription.StatusText(new CliOptions { Transport = "tcp", Host = "192.168.0.110", Port = "23" }, state));

    [TestMethod]
    [DataRow(99, false)]
    [DataRow(100, true)]
    [DataRow(60000, true)]
    [DataRow(60001, false)]
    public void ScpiAutoDetectTimeout_IsValidated(int timeoutMs, bool valid)
    {
        var options = new CliOptions { Transport = "loopback", ScpiAutoDetectTimeoutMs = timeoutMs };

        Assert.AreEqual(valid, new CliOptionsValidator().Validate(null, options).Succeeded);
    }

    [TestMethod]
    public void ScpiAutoDetectTimeout_SavedInAProfileOnlyWhenNotTheDefault()
    {
        Assert.DoesNotContain(nameof(CliOptions.ScpiAutoDetectTimeoutMs), DevTermConfiguration.ToProfileJson(new CliOptions { Transport = "loopback" }));
        Assert.Contains("\"ScpiAutoDetectTimeoutMs\": 8000", DevTermConfiguration.ToProfileJson(new CliOptions { Transport = "loopback", ScpiAutoDetectTimeoutMs = 8000 }));
    }
}
