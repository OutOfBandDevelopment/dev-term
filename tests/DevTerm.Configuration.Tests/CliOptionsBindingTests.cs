using System.IO.Ports;
using Microsoft.Extensions.Configuration;

namespace DevTerm.Configuration.Tests;

[TestCategory("UNIT")]
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
        Assert.AreEqual(8, options.DataBits);
        Assert.AreEqual(Parity.None, options.Parity);
        Assert.AreEqual(StopBits.One, options.StopBits);
        Assert.AreEqual(Handshake.None, options.Handshake);
        Assert.AreEqual(5000, options.WriteTimeoutMs);
        Assert.AreEqual(1000, options.ReadTimeoutMs);
        Assert.IsTrue(options.Dtr);
        Assert.IsTrue(options.Rts);
        Assert.AreEqual(LineEnding.None, options.LineEnding);
        Assert.AreEqual(DevTerm.Presenters.Text.AsciiPresenter.DefaultMaxLineLength, options.AsciiMaxLineLength);
        Assert.IsNull(options.Port);
        Assert.IsNull(options.Host);
        Assert.AreEqual(0, options.TcpPort);
        Assert.IsFalse(options.Listen);
        Assert.IsTrue(options.Tui, "The TUI is the console app's default mode.");
        Assert.IsFalse(options.Cli);
        Assert.IsNull(options.ManifestName);
    }

    [TestMethod]
    public void Bind_CliFlag_ForcesCliModeWithoutChangingTuiDefault()
    {
        var options = Bind("--cli", "true");

        Assert.IsTrue(options.Cli);
        Assert.IsTrue(options.Tui, "Cli is a separate override, not the inverse of Tui's stored value.");
    }

    [TestMethod]
    public void Bind_TuiFalse_AlsoAvailableToForceCliMode()
    {
        var options = Bind("--tui", "false");

        Assert.IsFalse(options.Tui);
    }

    [TestMethod]
    public void Bind_ManifestName_IsBoundAsAName()
    {
        var options = Bind("--manifestname", "radex-one");

        Assert.AreEqual("radex-one", options.ManifestName);
    }

    [TestMethod]
    public void Bind_SerialFraming_BindsEnumsByName()
    {
        var options = Bind(
            "--port", "COM3", "--baud", "4800",
            "--databits", "7", "--parity", "Even", "--stopbits", "Two", "--handshake", "RequestToSend");

        Assert.AreEqual(7, options.DataBits);
        Assert.AreEqual(Parity.Even, options.Parity);
        Assert.AreEqual(StopBits.Two, options.StopBits);
        Assert.AreEqual(Handshake.RequestToSend, options.Handshake);
    }

    [TestMethod]
    public void Bind_DtrRtsAndTimeouts_OverrideDefaults()
    {
        var options = Bind(
            "--port", "COM3", "--dtr", "false", "--rts", "false",
            "--writetimeoutms", "2000", "--readtimeoutms", "250", "--lineending", "Cr");

        Assert.IsFalse(options.Dtr);
        Assert.IsFalse(options.Rts);
        Assert.AreEqual(2000, options.WriteTimeoutMs);
        Assert.AreEqual(250, options.ReadTimeoutMs);
        Assert.AreEqual(LineEnding.Cr, options.LineEnding);
    }

    [TestMethod]
    public void Bind_AsciiMaxLineLength_OverridesDefault()
    {
        var options = Bind("--port", "COM3", "--asciimaxlinelength", "512");

        Assert.AreEqual(512, options.AsciiMaxLineLength);
    }

    [TestMethod]
    public void Bind_AsciiMaxLineLengthZero_IsAllowed()
    {
        var options = Bind("--port", "COM3", "--asciimaxlinelength", "0");

        Assert.AreEqual(0, options.AsciiMaxLineLength);
    }

    [TestMethod]
    public void Bind_ListPorts_DefaultsToFalse()
    {
        var options = Bind();

        Assert.IsFalse(options.ListPorts);
    }

    [TestMethod]
    public void Bind_ListPortsFlag_RequiresAnExplicitValue()
    {
        var options = Bind("--listports", "true");

        Assert.IsTrue(options.ListPorts);
    }

    [TestMethod]
    public void Bind_HidArguments_PopulatesOptions()
    {
        var options = Bind("--transport", "hid", "--hidvendorid", "6421", "--hidproductid", "45018", "--hidserialnumber", "12345");

        Assert.AreEqual("hid", options.Transport);
        Assert.AreEqual(6421, options.HidVendorId);
        Assert.AreEqual(45018, options.HidProductId);
        Assert.AreEqual("12345", options.HidSerialNumber);
    }

    [TestMethod]
    public void Bind_ListHidDevices_DefaultsToFalse()
    {
        var options = Bind();

        Assert.IsFalse(options.ListHidDevices);
    }

    [TestMethod]
    public void Bind_ListHidDevicesFlag_RequiresAnExplicitValue()
    {
        var options = Bind("--listhiddevices", "true");

        Assert.IsTrue(options.ListHidDevices);
    }
}
