using DevTerm.Test.Utilities;
using DevTerm.Transports.Ble;

namespace DevTerm.Configuration.Tests;

[TestClass]
[TestCategory(TestCategories.Unit)]
public sealed class BleCharacteristicOptionTests
{
    [TestMethod]
    public void FromServices_FlattensOneRowPerCharacteristicAcrossEveryService()
    {
        var services = new[]
        {
            new BleGattServiceDescriptor(
                "service-a",
                null,
                [
                    new BleGattCharacteristicDescriptor("char-a1", null, true, false, false, false, false),
                    new BleGattCharacteristicDescriptor("char-a2", null, false, true, false, false, false),
                ]),
            new BleGattServiceDescriptor(
                "service-b",
                null,
                [new BleGattCharacteristicDescriptor("char-b1", null, false, false, true, true, false)]),
        };

        var options = BleCharacteristicOption.FromServices(services).ToList();

        Assert.AreEqual(3, options.Count);
        Assert.IsTrue(options.All(o => o.ServiceUuid is "service-a" or "service-b"));
    }

    [TestMethod]
    public void FromServices_DisplayIncludesUuidCapabilityFlagsNameAndServiceUuid()
    {
        var services = new[]
        {
            new BleGattServiceDescriptor(
                "0000ffe0-0000-1000-8000-00805f9b34fb",
                null,
                [new BleGattCharacteristicDescriptor("0000ffe1-0000-1000-8000-00805f9b34fb", "Serial Port", CanRead: true, CanWrite: false, CanWriteWithoutResponse: true, CanNotify: true, CanIndicate: false)]),
        };

        var option = BleCharacteristicOption.FromServices(services).Single();

        StringAssert.Contains(option.Display, "0000ffe1-0000-1000-8000-00805f9b34fb");
        StringAssert.Contains(option.Display, "[Read,WriteWithoutResponse,Notify]");
        StringAssert.Contains(option.Display, "Serial Port");
        StringAssert.Contains(option.Display, "0000ffe0-0000-1000-8000-00805f9b34fb");
    }

    [TestMethod]
    public void FromServices_WithNoCharacteristicName_OmitsTheNamePortion()
    {
        var services = new[]
        {
            new BleGattServiceDescriptor(
                "service",
                null,
                [new BleGattCharacteristicDescriptor("char", null, true, false, false, false, false)]),
        };

        var option = BleCharacteristicOption.FromServices(services).Single();

        Assert.AreEqual("char  [Read]  (service service)", option.Display);
    }
}
