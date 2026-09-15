namespace DevTerm.Configuration.Tests;

[TestCategory("UNIT")]
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
        var result = _validator.Validate(null, new CliOptions { Transport = "tcp", Host = "device.local", TcpPort = 502 });

        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    public void Validate_TcpClientWithoutHost_Fails()
    {
        var result = _validator.Validate(null, new CliOptions { Transport = "tcp", Host = null, TcpPort = 502, Listen = false });

        Assert.IsTrue(result.Failed);
    }

    [TestMethod]
    public void Validate_TcpListenerWithoutHost_Succeeds()
    {
        var result = _validator.Validate(null, new CliOptions { Transport = "tcp", Host = null, TcpPort = 9000, Listen = true });

        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    public void Validate_TcpWithoutPort_Fails()
    {
        var result = _validator.Validate(null, new CliOptions { Transport = "tcp", Host = "device.local", TcpPort = 0 });

        Assert.IsTrue(result.Failed);
    }

    [TestMethod]
    public void Validate_HidWithVendorAndProductId_Succeeds()
    {
        var result = _validator.Validate(null, new CliOptions { Transport = "hid", HidVendorId = 0x1915, HidProductId = 0xAFDA });

        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    public void Validate_HidWithoutVendorId_Fails()
    {
        var result = _validator.Validate(null, new CliOptions { Transport = "hid", HidVendorId = 0, HidProductId = 0xAFDA });

        Assert.IsTrue(result.Failed);
    }

    [TestMethod]
    public void Validate_HidWithoutProductId_Fails()
    {
        var result = _validator.Validate(null, new CliOptions { Transport = "hid", HidVendorId = 0x1915, HidProductId = 0 });

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
        var result = _validator.Validate(null, new CliOptions { Transport = "TCP", Host = "device.local", TcpPort = 502 });

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
}
