using DevTerm.Test.Utilities;

namespace DevTerm.Transports.Ble.Tests;

[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Ble)]
[TestClass]
public sealed class UnsupportedPlatformBleTests
{
    [TestMethod]
    public void Create_ThrowsPlatformNotSupported()
    {
        var factory = new UnsupportedPlatformBleAdapterFactory();

        Assert.ThrowsExactly<PlatformNotSupportedException>(() => factory.Create(new BleTransportOptions { DeviceId = "x" }));
    }

    [TestMethod]
    public void GetDevices_ReturnsEmpty()
    {
        var discovery = new UnsupportedPlatformBleDeviceDiscovery();

        Assert.IsEmpty(discovery.GetDevices());
    }
}
