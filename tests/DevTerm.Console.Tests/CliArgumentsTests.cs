using DevTerm.Console;

namespace DevTerm.Console.Tests;

[TestClass]
public sealed class CliArgumentsTests
{
    [TestMethod]
    public void Parse_DefaultsToSerialTransport()
    {
        var result = CliArguments.Parse(["--port", "COM3"]);

        Assert.AreEqual(TransportKind.Serial, result.Transport);
        Assert.AreEqual("COM3", result.SerialPortName);
        Assert.AreEqual(CliArguments.DefaultBaudRate, result.SerialBaudRate);
        Assert.AreEqual(CliArguments.DefaultPresenterName, result.PresenterName);
    }

    [TestMethod]
    public void Parse_SerialWithAllArguments_UsesProvidedValues()
    {
        var result = CliArguments.Parse(["--port", "COM5", "--baud", "115200", "--presenter", "ascii"]);

        Assert.AreEqual("COM5", result.SerialPortName);
        Assert.AreEqual(115200, result.SerialBaudRate);
        Assert.AreEqual("ascii", result.PresenterName);
    }

    [TestMethod]
    public void Parse_ArgumentOrderDoesNotMatter()
    {
        var result = CliArguments.Parse(["--presenter", "hex", "--baud", "9600", "--port", "COM1"]);

        Assert.AreEqual("COM1", result.SerialPortName);
        Assert.AreEqual(9600, result.SerialBaudRate);
        Assert.AreEqual("hex", result.PresenterName);
    }

    [TestMethod]
    public void Parse_SerialMissingPort_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => CliArguments.Parse(["--baud", "9600"]));
    }

    [TestMethod]
    public void Parse_UnknownArgument_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => CliArguments.Parse(["--port", "COM1", "--nope", "x"]));
    }

    [TestMethod]
    public void Parse_NonIntegerBaud_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => CliArguments.Parse(["--port", "COM1", "--baud", "fast"]));
    }

    [TestMethod]
    public void Parse_OptionMissingValue_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => CliArguments.Parse(["--port"]));
    }

    [TestMethod]
    public void Parse_TcpClientMode_UsesProvidedValues()
    {
        var result = CliArguments.Parse(["--transport", "tcp", "--host", "device.local", "--tcp-port", "502"]);

        Assert.AreEqual(TransportKind.Tcp, result.Transport);
        Assert.AreEqual("device.local", result.TcpHost);
        Assert.AreEqual(502, result.TcpPort);
        Assert.IsFalse(result.TcpListen);
    }

    [TestMethod]
    public void Parse_TcpListenerMode_DoesNotRequireHost()
    {
        var result = CliArguments.Parse(["--transport", "tcp", "--listen", "--tcp-port", "9000"]);

        Assert.AreEqual(TransportKind.Tcp, result.Transport);
        Assert.IsTrue(result.TcpListen);
        Assert.AreEqual(9000, result.TcpPort);
        Assert.IsNull(result.TcpHost);
    }

    [TestMethod]
    public void Parse_TcpClientModeWithoutHost_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => CliArguments.Parse(["--transport", "tcp", "--tcp-port", "502"]));
    }

    [TestMethod]
    public void Parse_TcpMissingPort_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => CliArguments.Parse(["--transport", "tcp", "--host", "device.local"]));
    }

    [TestMethod]
    public void Parse_UnknownTransport_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => CliArguments.Parse(["--transport", "carrier-pigeon", "--port", "COM1"]));
    }

    [TestMethod]
    public void Parse_TransportNameIsCaseInsensitive()
    {
        var result = CliArguments.Parse(["--transport", "TCP", "--listen", "--tcp-port", "9000"]);

        Assert.AreEqual(TransportKind.Tcp, result.Transport);
    }
}
