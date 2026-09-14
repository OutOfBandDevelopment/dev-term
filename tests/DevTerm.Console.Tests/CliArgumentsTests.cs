using DevTerm.Console;

namespace DevTerm.Console.Tests;

[TestClass]
public sealed class CliArgumentsTests
{
    [TestMethod]
    public void Parse_WithOnlyPort_UsesDefaults()
    {
        var result = CliArguments.Parse(["--port", "COM3"]);

        Assert.AreEqual("COM3", result.PortName);
        Assert.AreEqual(CliArguments.DefaultBaudRate, result.BaudRate);
        Assert.AreEqual(CliArguments.DefaultPresenterName, result.PresenterName);
    }

    [TestMethod]
    public void Parse_WithAllArguments_UsesProvidedValues()
    {
        var result = CliArguments.Parse(["--port", "COM5", "--baud", "115200", "--presenter", "ascii"]);

        Assert.AreEqual("COM5", result.PortName);
        Assert.AreEqual(115200, result.BaudRate);
        Assert.AreEqual("ascii", result.PresenterName);
    }

    [TestMethod]
    public void Parse_ArgumentOrderDoesNotMatter()
    {
        var result = CliArguments.Parse(["--presenter", "hex", "--baud", "9600", "--port", "COM1"]);

        Assert.AreEqual("COM1", result.PortName);
        Assert.AreEqual(9600, result.BaudRate);
        Assert.AreEqual("hex", result.PresenterName);
    }

    [TestMethod]
    public void Parse_MissingPort_Throws()
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
}
