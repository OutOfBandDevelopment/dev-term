using DevTerm.Test.Utilities;

namespace DevTerm.Devices.Busylight.Tests;

[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Kuando_Busylight)]
[TestClass]
public sealed class BusylightPanelContributionTests
{
    [TestMethod]
    [DataRow("hid", 0x04D8, 0xF848, true)]
    [DataRow("hid", 0x27BB, 0x3BCA, true)]
    [DataRow("hid", 0x04D8, 0x0001, false)]
    [DataRow("tcp", 0x27BB, 0x3BCA, false)]
    public void IsAvailable_MatchesTheBuiltInBusylightGate(string transport, int vendorId, int productId, bool expected) =>
        Assert.AreEqual(expected, new BusylightPanelContribution().IsAvailable(transport, vendorId, productId));
}
