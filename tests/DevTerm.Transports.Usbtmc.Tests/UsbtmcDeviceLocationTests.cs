using DevTerm.Test.Utilities;
using DevTerm.Transports.Usbtmc;

namespace DevTerm.Transports.Usbtmc.Tests;

[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Usbtmc)]
[TestClass]
public sealed class UsbtmcDeviceLocationTests
{
    [TestMethod]
    public void Format_BusAndPortChain_LooksLikeASysfsName()
    {
        Assert.AreEqual("usb:1-4", UsbtmcDeviceLocation.Format(1, [4]));
        Assert.AreEqual("usb:2-1.3.2", UsbtmcDeviceLocation.Format(2, [1, 3, 2]));
    }

    [TestMethod]
    public void Format_NoPortChain_IsNull() => Assert.IsNull(UsbtmcDeviceLocation.Format(1, []));
}
