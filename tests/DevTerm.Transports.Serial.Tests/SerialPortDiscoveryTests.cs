namespace DevTerm.Transports.Serial.Tests;

[TestCategory("UNIT")]
[TestClass]
public sealed class SerialPortDiscoveryTests
{
    [TestMethod]
    [DataRow("USB Serial Device (COM3)", "COM3", "USB Serial Device")]
    [DataRow("Prolific USB-to-Serial Comm Port (COM12)", "COM12", "Prolific USB-to-Serial Comm Port")]
    [DataRow("  USB Serial Device (com3)  ", "COM3", "USB Serial Device")]
    [DataRow("Communications Port (COM1) ", "COM1", "Communications Port")]
    public void StripPortSuffix_DropsTheTrailingPortInParentheses(string friendlyName, string portName, string expected) =>
        Assert.AreEqual(expected, SystemSerialPortDiscovery.StripPortSuffix(friendlyName, portName));

    [TestMethod]
    [DataRow("USB Serial Device", "COM3")]
    [DataRow("USB Serial Device (COM4)", "COM3")]
    [DataRow("(COM3) USB Serial Device", "COM3")]
    public void StripPortSuffix_LeavesANameWithoutThatSuffixAlone(string friendlyName, string portName) =>
        Assert.AreEqual(friendlyName, SystemSerialPortDiscovery.StripPortSuffix(friendlyName, portName));

    [TestMethod]
    public void StripPortSuffix_WhenTheNameIsOnlyThePort_ReturnsEmpty() =>
        Assert.AreEqual(string.Empty, SystemSerialPortDiscovery.StripPortSuffix("(COM3)", "COM3"));

    [TestMethod]
    public void DefaultInterfaceImplementation_ReportsNoDescriptions()
    {
        ISerialPortDiscovery discovery = new NamesOnlyDiscovery();

        Assert.IsEmpty(discovery.GetPortDescriptions());
    }

    [TestMethod]
    public void SystemDiscovery_GetPortDescriptions_DoesNotThrow_AndReturnsSaneEntries()
    {
        // Reads whatever this machine's real Plug-and-Play registry holds (nothing on a machine
        // without serial devices, or a non-Windows one) - so this can only assert shape, not content.
        var descriptions = new SystemSerialPortDiscovery().GetPortDescriptions();

        foreach (var (port, description) in descriptions)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(port));
            Assert.IsFalse(string.IsNullOrWhiteSpace(description), $"{port} has a blank description.");
            Assert.IsFalse(
                description.EndsWith($"({port})", StringComparison.OrdinalIgnoreCase),
                $"'{description}' still carries the redundant ({port}) suffix.");
        }
    }

    private sealed class NamesOnlyDiscovery : ISerialPortDiscovery
    {
        public IReadOnlyList<string> GetPortNames() => ["COM1"];
    }
}
