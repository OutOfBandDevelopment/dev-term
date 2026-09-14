using Microsoft.Extensions.Configuration;

namespace DevTerm.Console.Tests;

[TestClass]
public sealed class CliOptionsBindingTests
{
    private static CliOptions Bind(params string[] args)
    {
        var configuration = new ConfigurationBuilder().AddCommandLine(args).Build();
        var options = new CliOptions();
        configuration.Bind(options);
        return options;
    }

    [TestMethod]
    public void Bind_SerialArguments_PopulatesOptionsByPropertyName()
    {
        var options = Bind("--port", "COM3", "--baud", "115200");

        Assert.AreEqual("serial", options.Transport, "Transport should keep its default.");
        Assert.AreEqual("COM3", options.Port);
        Assert.AreEqual(115200, options.Baud);
        Assert.AreEqual("hex", options.Presenter, "Presenter should keep its default.");
    }

    [TestMethod]
    public void Bind_TcpClientArguments_PopulatesOptions()
    {
        var options = Bind("--transport", "tcp", "--host", "device.local", "--tcpport", "502", "--presenter", "ascii");

        Assert.AreEqual("tcp", options.Transport);
        Assert.AreEqual("device.local", options.Host);
        Assert.AreEqual(502, options.TcpPort);
        Assert.AreEqual("ascii", options.Presenter);
        Assert.IsFalse(options.Listen);
    }

    [TestMethod]
    public void Bind_ListenFlag_RequiresAnExplicitValue()
    {
        // A known quirk of Microsoft.Extensions.Configuration.CommandLine: unlike a getopt-style
        // parser, a bare trailing switch has no value, so boolean flags must be passed explicitly
        // (`--listen true`), not as a bare `--listen`.
        var options = Bind("--transport", "tcp", "--listen", "true", "--tcpport", "9000");

        Assert.IsTrue(options.Listen);
    }

    [TestMethod]
    public void Bind_NoArguments_UsesAllDefaults()
    {
        var options = Bind();

        Assert.AreEqual("serial", options.Transport);
        Assert.AreEqual("hex", options.Presenter);
        Assert.AreEqual(9600, options.Baud);
        Assert.IsNull(options.Port);
        Assert.IsNull(options.Host);
        Assert.AreEqual(0, options.TcpPort);
        Assert.IsFalse(options.Listen);
    }
}
