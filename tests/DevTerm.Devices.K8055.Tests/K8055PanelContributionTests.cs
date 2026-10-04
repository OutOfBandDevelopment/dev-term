using DevTerm.Test.Utilities;

namespace DevTerm.Devices.K8055.Tests;

[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Velleman_K8055)]
[TestClass]
public sealed class K8055PanelContributionTests
{
    [TestMethod]
    [DataRow("hid", 0x10CF, 0x5500, true)]
    [DataRow("HID", 0x10CF, 0x5503, true)]
    [DataRow("hid", 0x10CF, 0x5504, false)]
    [DataRow("tcp", 0x10CF, 0x5500, false)]
    [DataRow("hid", 0x04D8, 0xF848, false)]
    public void IsAvailable_MatchesTheBuiltInK8055Gate(string transport, int vendorId, int productId, bool expected) =>
        Assert.AreEqual(expected, new K8055PanelContribution().IsAvailable(transport, vendorId, productId));

    [TestMethod]
    public void Definition_IsTheK8055Panel() => Assert.AreEqual("k8055", new K8055PanelContribution().Id);
}
