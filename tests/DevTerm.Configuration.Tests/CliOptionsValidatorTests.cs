using DevTerm.Test.Utilities;

namespace DevTerm.Configuration.Tests;

[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class CliOptionsValidatorTests
{
    private readonly CliOptionsValidator _validator = new();

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
        // ArgumentOutOfRangeException. See docs/bugs/015-mistyped-numbers-silently-default.md.
        var result = _validator.Validate(null, new CliOptions { Transport = "serial", Port = "COM3", Baud = 0 });

        Assert.IsTrue(result.Failed);
    }

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public void Validate_SerialWithDataBitsOutOfRange_Fails()
    {
        // Regression test for bug 015: DataBits: 9 in a profile passed validation and only failed
        // once SerialPort actually opened. See docs/bugs/015-mistyped-numbers-silently-default.md.
        var result = _validator.Validate(null, new CliOptions { Transport = "serial", Port = "COM3", DataBits = 9 });

        Assert.IsTrue(result.Failed);
    }

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public void Validate_ReadTimeoutBelowNegativeOne_Fails()
    {
        // Regression test for bug 015: ReadTimeoutMs: -5 in a profile passed validation.
        // See docs/bugs/015-mistyped-numbers-silently-default.md.
        var result = _validator.Validate(null, new CliOptions { Transport = "serial", Port = "COM3", ReadTimeoutMs = -5 });

        Assert.IsTrue(result.Failed);
    }

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public void Validate_WriteTimeoutBelowNegativeOne_Fails()
    {
        // Regression test for bug 015. See docs/bugs/015-mistyped-numbers-silently-default.md.
        var result = _validator.Validate(null, new CliOptions { Transport = "serial", Port = "COM3", WriteTimeoutMs = -5 });

        Assert.IsTrue(result.Failed);
    }

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public void Validate_NegativePlaybackSpeed_Fails()
    {
        // Regression test for bug 015. See docs/bugs/015-mistyped-numbers-silently-default.md.
        var result = _validator.Validate(null, new CliOptions { Transport = "serial", Port = "COM3", PlaybackSpeed = -1 });

        Assert.IsTrue(result.Failed);
    }
}
