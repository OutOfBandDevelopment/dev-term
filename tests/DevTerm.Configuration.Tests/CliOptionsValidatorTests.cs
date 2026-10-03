using DevTerm.Test.Utilities;

namespace DevTerm.Configuration.Tests;

[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class CliOptionsValidatorTests
{
    private readonly CliOptionsValidator _validator = new();

    [TestMethod]
    [DataRow(null)]
    [DataRow("true")]
    [DataRow("false")]
    [DataRow("http://localhost:4317")]
    public void Validate_OtlpOffOrAValidEndpoint_Succeeds(string? otlp)
    {
        var result = _validator.Validate(null, new CliOptions { Transport = "serial", Port = "COM3", Otlp = otlp });

        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    public void Validate_OtlpThatIsNotAnHttpUrl_Fails()
    {
        var result = _validator.Validate(null, new CliOptions { Transport = "serial", Port = "COM3", Otlp = "localhost:4317" });

        Assert.IsTrue(result.Failed);
    }

    [TestMethod]
    public void Validate_SerialWithPort_Succeeds()
    {
        var result = _validator.Validate(null, new CliOptions { Transport = "serial", Port = "COM3" });

        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    public void Validate_SerialWithoutPort_Fails()
    {
        var result = _validator.Validate(null, new CliOptions { Transport = "serial", Port = null });

        Assert.IsTrue(result.Failed);
    }

    [TestMethod]
    public void Validate_TcpClientWithHostAndPort_Succeeds()
    {
        var result = _validator.Validate(null, new CliOptions { Transport = "tcp", Host = "device.local", Port = "502" });

        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    public void Validate_TcpClientWithoutHost_Fails()
    {
        var result = _validator.Validate(null, new CliOptions { Transport = "tcp", Host = null, Port = "502", Listen = false });

        Assert.IsTrue(result.Failed);
    }

    [TestMethod]
    public void Validate_TcpListenerWithoutHost_Succeeds()
    {
        var result = _validator.Validate(null, new CliOptions { Transport = "tcp", Host = null, Port = "9000", Listen = true });

        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    public void Validate_TcpWithoutPort_Fails()
    {
        var result = _validator.Validate(null, new CliOptions { Transport = "tcp", Host = "device.local", Port = "0" });

        Assert.IsTrue(result.Failed);
    }

    [TestMethod]
    public void Validate_MqttWithHostPortAndTopic_Succeeds()
    {
        var result = _validator.Validate(null, new CliOptions { Transport = "mqtt", Host = "broker", Port = "1883", Subscribe = "a/#" });

        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    public void Validate_MqttWithoutAnyTopic_Fails()
    {
        var result = _validator.Validate(null, new CliOptions { Transport = "mqtt", Host = "broker", Port = "1883" });

        Assert.IsTrue(result.Failed);
    }

    [TestMethod]
    public void Validate_MqttWithoutHost_Fails()
    {
        var result = _validator.Validate(null, new CliOptions { Transport = "mqtt", Host = null, Port = "1883", Publish = "x" });

        Assert.IsTrue(result.Failed);
    }

    [TestMethod]
    [DataRow("amqp")]
    [DataRow("stomp")]
    public void Validate_BrokerTransportWithHostPortAndAddress_Succeeds(string transport)
    {
        var result = _validator.Validate(null, new CliOptions { Transport = transport, Host = "broker", Port = "5672", Subscribe = "a.#" });

        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    [DataRow("amqp")]
    [DataRow("stomp")]
    public void Validate_BrokerTransportWithoutAnyAddress_FailsNamingTheTransport(string transport)
    {
        var result = _validator.Validate(null, new CliOptions { Transport = transport, Host = "broker", Port = "5672" });

        Assert.IsTrue(result.Failed);
        StringAssert.Contains(result.FailureMessage, transport.ToUpperInvariant(), StringComparison.Ordinal);
    }

    [TestMethod]
    public void Validate_Rfc2217WithHostAndPort_Succeeds()
    {
        var result = _validator.Validate(null, new CliOptions { Transport = "rfc2217", Host = "device.local", Port = "2217" });

        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    public void Validate_Rfc2217WithoutHost_Fails()
    {
        var result = _validator.Validate(null, new CliOptions { Transport = "rfc2217", Host = null, Port = "2217" });

        Assert.IsTrue(result.Failed);
    }

    [TestMethod]
    public void Validate_Rfc2217WithoutPort_Fails()
    {
        var result = _validator.Validate(null, new CliOptions { Transport = "rfc2217", Host = "device.local", Port = "0" });

        Assert.IsTrue(result.Failed);
    }

    [TestMethod]
    public void Validate_Rfc2217WithInvalidBaud_Fails()
    {
        var result = _validator.Validate(null, new CliOptions { Transport = "rfc2217", Host = "device.local", Port = "2217", Baud = 0 });

        Assert.IsTrue(result.Failed);
    }

    [TestMethod]
    public void Validate_Rfc2217WithInvalidDataBits_Fails()
    {
        var result = _validator.Validate(null, new CliOptions { Transport = "rfc2217", Host = "device.local", Port = "2217", DataBits = 9 });

        Assert.IsTrue(result.Failed);
    }

    [TestMethod]
    public void Validate_HidWithVendorAndProductId_Succeeds()
    {
        var result = _validator.Validate(null, new CliOptions { Transport = "hid", VendorId = 0x1915, ProductId = 0xAFDA });

        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    public void Validate_HidWithoutVendorId_Fails()
    {
        var result = _validator.Validate(null, new CliOptions { Transport = "hid", VendorId = 0, ProductId = 0xAFDA });

        Assert.IsTrue(result.Failed);
    }

    [TestMethod]
    public void Validate_HidWithoutProductId_Fails()
    {
        var result = _validator.Validate(null, new CliOptions { Transport = "hid", VendorId = 0x1915, ProductId = 0 });

        Assert.IsTrue(result.Failed);
    }

    [TestMethod]
    public void Validate_WithLoopbackTransport_Succeeds()
    {
        var result = _validator.Validate(null, new CliOptions { Transport = "loopback" });

        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    public void Validate_WithPositiveLoopbackSampleIntervalMs_Succeeds()
    {
        var result = _validator.Validate(null, new CliOptions { Transport = "loopback", LoopbackSampleIntervalMs = 100 });

        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    public void Validate_WithNegativeLoopbackSampleIntervalMs_Fails()
    {
        var result = _validator.Validate(null, new CliOptions { Transport = "loopback", LoopbackSampleIntervalMs = -1 });

        Assert.IsTrue(result.Failed);
    }

    [TestMethod]
    public void Validate_UnknownTransport_Fails()
    {
        var result = _validator.Validate(null, new CliOptions { Transport = "carrier-pigeon" });

        Assert.IsTrue(result.Failed);
    }

    [TestMethod]
    public void Validate_TransportIsCaseInsensitive()
    {
        var result = _validator.Validate(null, new CliOptions { Transport = "TCP", Host = "device.local", Port = "502" });

        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    public void Validate_NegativeAsciiMaxLineLength_Fails()
    {
        var result = _validator.Validate(null, new CliOptions { Transport = "serial", Port = "COM3", AsciiMaxLineLength = -1 });

        Assert.IsTrue(result.Failed);
    }

    [TestMethod]
    public void Validate_AsciiMaxLineLengthZero_Succeeds()
    {
        var result = _validator.Validate(null, new CliOptions { Transport = "serial", Port = "COM3", AsciiMaxLineLength = 0 });

        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public void Validate_SerialWithNonPositiveBaud_Fails()
    {
        // Regression test for bug 015: a saved/loaded profile's Baud (e.g. 0, or a negative value)
        // passed validation and only failed once SerialPort actually opened, with an
        // ArgumentOutOfRangeException. See docs/bugs/resolved/015-mistyped-numbers-silently-default.md.
        var result = _validator.Validate(null, new CliOptions { Transport = "serial", Port = "COM3", Baud = 0 });

        Assert.IsTrue(result.Failed);
    }

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public void Validate_SerialWithDataBitsOutOfRange_Fails()
    {
        // Regression test for bug 015: DataBits: 9 in a profile passed validation and only failed
        // once SerialPort actually opened. See docs/bugs/resolved/015-mistyped-numbers-silently-default.md.
        var result = _validator.Validate(null, new CliOptions { Transport = "serial", Port = "COM3", DataBits = 9 });

        Assert.IsTrue(result.Failed);
    }

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public void Validate_ReadTimeoutBelowNegativeOne_Fails()
    {
        // Regression test for bug 015: ReadTimeoutMs: -5 in a profile passed validation.
        // See docs/bugs/resolved/015-mistyped-numbers-silently-default.md.
        var result = _validator.Validate(null, new CliOptions { Transport = "serial", Port = "COM3", ReadTimeoutMs = -5 });

        Assert.IsTrue(result.Failed);
    }

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public void Validate_WriteTimeoutBelowNegativeOne_Fails()
    {
        // Regression test for bug 015. See docs/bugs/resolved/015-mistyped-numbers-silently-default.md.
        var result = _validator.Validate(null, new CliOptions { Transport = "serial", Port = "COM3", WriteTimeoutMs = -5 });

        Assert.IsTrue(result.Failed);
    }

    [TestMethod]
    public void Validate_WriteByteDelayBelowNegativeOne_Fails()
    {
        var result = _validator.Validate(null, new CliOptions { Transport = "serial", Port = "COM3", WriteByteDelayMs = -5 });

        Assert.IsTrue(result.Failed);
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(0)]
    [DataRow(5)]
    public void Validate_WriteByteDelayNegativeOneOrGreater_Succeeds(int writeByteDelayMs)
    {
        var result = _validator.Validate(null, new CliOptions { Transport = "serial", Port = "COM3", WriteByteDelayMs = writeByteDelayMs });

        Assert.IsFalse(result.Failed);
    }

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public void Validate_NegativePlaybackSpeed_Fails()
    {
        // Regression test for bug 015. See docs/bugs/resolved/015-mistyped-numbers-silently-default.md.
        var result = _validator.Validate(null, new CliOptions { Transport = "serial", Port = "COM3", PlaybackSpeed = -1 });

        Assert.IsTrue(result.Failed);
    }

    private static CliOptions WithTools(string mode, params string[] names) => new()
    {
        Transport = "serial",
        Port = "COM3",
        StreamConvertMode = mode,
        StreamConvertTools = [.. names.Select(n => new StreamConvertToolOptions { Name = n, Path = "gs" })],
    };

    [TestMethod]
    public void Validate_StreamConvertModeAuto_Succeeds() =>
        Assert.IsTrue(_validator.Validate(null, WithTools("auto")).Succeeded);

    [TestMethod]
    public void Validate_StreamConvertModeToolNamingARegisteredTool_Succeeds() =>
        Assert.IsTrue(_validator.Validate(null, WithTools("tool:GS", "gs")).Succeeded);

    [TestMethod]
    public void Validate_StreamConvertModeToolNamingAToolNotInTheProfile_Succeeds() =>
        Assert.IsTrue(_validator.Validate(null, WithTools("tool:nope", "gs")).Succeeded);

    [TestMethod]
    public void Validate_StreamConvertModeToolWithoutAName_Fails() =>
        Assert.IsTrue(_validator.Validate(null, WithTools("tool:", "gs")).Failed);

    [TestMethod]
    public void Validate_DuplicateToolNames_Fail() =>
        Assert.IsTrue(_validator.Validate(null, WithTools("none", "gs", "GS")).Failed);

    [TestMethod]
    public void Validate_ToolWithoutAPath_Fails()
    {
        var options = WithTools("none", "gs");
        options.StreamConvertTools[0].Path = string.Empty;

        Assert.IsTrue(_validator.Validate(null, options).Failed);
    }
}
