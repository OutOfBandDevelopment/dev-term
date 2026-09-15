using System.IO.Ports;

namespace DevTerm.Configuration.Tests;

[TestCategory("UNIT")]
[TestClass]
public sealed class ConnectionDescriptionTests
{
    [TestMethod]
    public void For_Serial_DescribesPortBaudAndFraming()
    {
        var options = new CliOptions
        {
            Transport = "serial",
            Port = "COM3",
            Baud = 4800,
            DataBits = 8,
            Parity = Parity.None,
            StopBits = StopBits.One,
        };

        Assert.AreEqual("COM3 at 4800 baud (8N1)", ConnectionDescription.For(options));
    }

    [TestMethod]
    public void For_Serial_FormatsOnePointFiveStopBits()
    {
        var options = new CliOptions
        {
            Transport = "serial",
            Port = "COM3",
            Baud = 9600,
            DataBits = 7,
            Parity = Parity.Even,
            StopBits = StopBits.OnePointFive,
        };

        Assert.AreEqual("COM3 at 9600 baud (7E1.5)", ConnectionDescription.For(options));
    }

    [TestMethod]
    public void For_TcpClient_DescribesHostAndPort()
    {
        var options = new CliOptions { Transport = "tcp", Host = "192.168.0.107", TcpPort = 23, Listen = false };

        Assert.AreEqual("TCP 192.168.0.107:23", ConnectionDescription.For(options));
    }

    [TestMethod]
    public void For_TcpListener_DescribesListeningPortWithoutHost()
    {
        var options = new CliOptions { Transport = "tcp", TcpPort = 9000, Listen = true };

        Assert.AreEqual("TCP listener on port 9000", ConnectionDescription.For(options));
    }

    [TestMethod]
    public void For_TransportIsCaseInsensitive()
    {
        var options = new CliOptions { Transport = "TCP", Host = "device.local", TcpPort = 502 };

        StringAssert.StartsWith(ConnectionDescription.For(options), "TCP ");
    }

    [TestMethod]
    public void For_Hid_DescribesVendorAndProductIdInHex()
    {
        var options = new CliOptions { Transport = "hid", HidVendorId = 0x1915, HidProductId = 0xAFDA };

        Assert.AreEqual("USB HID VID 0x1915 PID 0xAFDA", ConnectionDescription.For(options));
    }

    [TestMethod]
    public void For_HidWithSerialNumber_IncludesIt()
    {
        var options = new CliOptions { Transport = "hid", HidVendorId = 0x1915, HidProductId = 0xAFDA, HidSerialNumber = "12345" };

        Assert.AreEqual("USB HID VID 0x1915 PID 0xAFDA serial '12345'", ConnectionDescription.For(options));
    }
}
