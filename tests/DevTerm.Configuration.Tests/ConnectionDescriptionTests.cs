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
        var options = new CliOptions { Transport = "tcp", Host = "192.168.0.107", Port = "23", Listen = false };

        Assert.AreEqual("TCP 192.168.0.107:23", ConnectionDescription.For(options));
    }

    [TestMethod]
    public void For_TcpListener_DescribesListeningPortWithoutHost()
    {
        var options = new CliOptions { Transport = "tcp", Port = "9000", Listen = true };

        Assert.AreEqual("TCP listener on port 9000", ConnectionDescription.For(options));
    }

    [TestMethod]
    public void For_TransportIsCaseInsensitive()
    {
        var options = new CliOptions { Transport = "TCP", Host = "device.local", Port = "502" };

        StringAssert.StartsWith(ConnectionDescription.For(options), "TCP ");
    }

    [TestMethod]
    public void For_Hid_DescribesVendorAndProductIdInHex()
    {
        var options = new CliOptions { Transport = "hid", VendorId = 0x1915, ProductId = 0xAFDA };

        Assert.AreEqual("USB HID VID 0x1915 PID 0xAFDA", ConnectionDescription.For(options));
    }

    [TestMethod]
    public void For_HidWithSerialNumber_IncludesIt()
    {
        var options = new CliOptions { Transport = "hid", VendorId = 0x1915, ProductId = 0xAFDA, SerialNumber = "12345" };

        Assert.AreEqual("USB HID VID 0x1915 PID 0xAFDA serial '12345'", ConnectionDescription.For(options));
    }


    [TestMethod]
    public void Definition_Tcp_IsATcpUri()
    {
        Assert.AreEqual("tcp://192.168.0.110:23", ConnectionDescription.Definition(new CliOptions { Transport = "tcp", Host = "192.168.0.110", Port = "23" }));
    }

    [TestMethod]
    public void Definition_TcpListener_ShowsTheWildcardHost()
    {
        Assert.AreEqual("tcp://*:9000 (listening)", ConnectionDescription.Definition(new CliOptions { Transport = "tcp", Listen = true, Port = "9000" }));
    }

    [TestMethod]
    public void Definition_Serial_IsPortBaudDataBitsParityLetterStopBits()
    {
        var options = new CliOptions { Transport = "serial", Port = "COM3", Baud = 4800, DataBits = 8, Parity = Parity.None, StopBits = StopBits.One };

        Assert.AreEqual("serial://COM3:4800,8,n,1", ConnectionDescription.Definition(options));
    }

    [TestMethod]
    [DataRow(Parity.Even, StopBits.OnePointFive, "serial:///dev/ttyUSB0:9600,7,e,1.5")]
    [DataRow(Parity.Odd, StopBits.Two, "serial:///dev/ttyUSB0:9600,7,o,2")]
    public void Definition_Serial_UsesLowercaseParityLetterAndTheRealStopBitsText(Parity parity, StopBits stopBits, string expected)
    {
        var options = new CliOptions { Transport = "serial", Port = "/dev/ttyUSB0", Baud = 9600, DataBits = 7, Parity = parity, StopBits = stopBits };

        Assert.AreEqual(expected, ConnectionDescription.Definition(options));
    }

    [TestMethod]
    public void Definition_Hid_IsVendorAndProductInHex()
    {
        Assert.AreEqual("hid://1915.AFDA", ConnectionDescription.Definition(new CliOptions { Transport = "hid", VendorId = 0x1915, ProductId = 0xAFDA }));
    }

    [TestMethod]
    public void Definition_HidWithSerialNumber_AppendsItAsTheInstance()
    {
        var options = new CliOptions { Transport = "hid", VendorId = 0x1915, ProductId = 0xAFDA, SerialNumber = "12345" };

        Assert.AreEqual("hid://1915.AFDA.12345", ConnectionDescription.Definition(options));
    }

    [TestMethod]
    public void WindowTitle_ForANonSavedConnection_UsesTheDefinitionAndTheFormats()
    {
        var store = new ConnectionProfileStore(Path.Combine(Path.GetTempPath(), $"devterm-tests-{Guid.NewGuid():N}"));
        var options = new CliOptions { Transport = "tcp", Host = "192.168.0.110", Port = "23", Presenter = ["ascii", "hex"], Parser = "hex" };

        Assert.AreEqual("dev-term — tcp://192.168.0.110:23 (ascii, hex; send as hex)", ConnectionDescription.WindowTitle(options, "hex", store));
    }

    [TestMethod]
    public void WindowTitle_ForASavedProfile_UsesTheProfileName()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"devterm-tests-{Guid.NewGuid():N}");
        try
        {
            var store = new ConnectionProfileStore(directory);
            var options = new CliOptions { Transport = "tcp", Host = "192.168.0.110", Port = "23", Presenter = ["ascii"], Parser = "ascii" };
            store.Save("tek2230", options);

            Assert.AreEqual("dev-term — tek2230 (ascii; send as ascii)", ConnectionDescription.WindowTitle(options, "ascii", store));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
