using DevTerm.Test.Utilities;

namespace DevTerm.Transports.Serial.Tests;

[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Serial)]
[TestClass]
public sealed class SerialPortDescriptionFormatTests
{
    [TestMethod]
    [DataRow("FTDI", "FT232R USB UART", "A50285BI", "0403:6001", "FTDI FT232R USB UART (0403:6001, serial A50285BI)")]
    [DataRow("Acme", "Acme Widget", null, "1234:5678", "Acme Widget (1234:5678)")]
    [DataRow("acme", "Acme Widget", null, null, "Acme Widget")]
    [DataRow(null, "Widget", null, null, "Widget")]
    [DataRow("Acme", null, null, null, "Acme")]
    [DataRow("  FTDI \n", " FT232R USB UART\n", " ", "0403:6001", "FTDI FT232R USB UART (0403:6001)")]
    [DataRow(null, null, "A50285BI", "0403:6001", "USB device 0403:6001, serial A50285BI")]
    [DataRow(null, null, null, "1a86:7523", "USB device 1a86:7523")]
    public void Format_CombinesWhatIsKnown(string? manufacturer, string? product, string? serial, string? ids, string expected) =>
        Assert.AreEqual(expected, SerialPortDescriptionFormat.Format(manufacturer, product, serial, ids));

    [TestMethod]
    [DataRow(null, null, null, null)]
    [DataRow(null, null, "A50285BI", null)]
    [DataRow(" ", "", null, null)]
    public void Format_WithNoNameAndNoIds_ReturnsNull(string? manufacturer, string? product, string? serial, string? ids) =>
        Assert.IsNull(SerialPortDescriptionFormat.Format(manufacturer, product, serial, ids));

    [TestMethod]
    [DataRow(1027L, 24577L, "0403:6001")]
    [DataRow(11914L, 10L, "2e8a:000a")]
    public void Ids_FormatsDecimalIdsAsFourDigitLowerCaseHex(long vendorId, long productId, string expected) =>
        Assert.AreEqual(expected, SerialPortDescriptionFormat.Ids(vendorId, productId));
}
