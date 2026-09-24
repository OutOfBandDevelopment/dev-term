using DevTerm.Core.Presenters;
using DevTerm.Core.Transports;
using DevTerm.Presenters.Text;
using DevTerm.Transports.Hid;
using DevTerm.Transports.Serial;
using DevTerm.Transports.Tcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DevTerm.Configuration.Tests;

/// <summary>
/// Verifies <see cref="ServiceCollectionExtensions.AddDevTermFrontEnd"/> wires up the same way
/// every front end (console CLI/TUI, WPF) relies on: the right <see cref="ITransport"/> for the
/// selected transport, configured from <see cref="CliOptions"/>, plus the shared presenter set.
/// </summary>
[TestCategory("UNIT")]
[TestClass]
public sealed class ServiceCollectionExtensionsTests
{
    [TestMethod]
    public void AddDevTermFrontEnd_SerialTransport_ResolvesSerialTransportConfiguredFromCliOptions()
    {
        var cliOptions = new CliOptions { Transport = "serial", Port = "COM3", Baud = 4800 };
        var provider = new ServiceCollection().AddDevTermFrontEnd(cliOptions).BuildServiceProvider();

        var transport = provider.GetRequiredService<ITransport>();
        var options = provider.GetRequiredService<IOptions<SerialTransportOptions>>().Value;

        Assert.IsInstanceOfType<SerialTransport>(transport);
        Assert.AreEqual("COM3", options.PortName);
        Assert.AreEqual(4800, options.BaudRate);
    }

    [TestMethod]
    public void AddDevTermFrontEnd_TcpTransport_ResolvesTcpTransportConfiguredFromCliOptions()
    {
        var cliOptions = new CliOptions { Transport = "tcp", Host = "device.local", Port = "502" };
        var provider = new ServiceCollection().AddDevTermFrontEnd(cliOptions).BuildServiceProvider();

        var transport = provider.GetRequiredService<ITransport>();
        var options = provider.GetRequiredService<IOptions<TcpTransportOptions>>().Value;

        Assert.IsInstanceOfType<TcpTransport>(transport);
        Assert.AreEqual(TcpTransportMode.Client, options.Mode);
        Assert.AreEqual("device.local", options.Host);
        Assert.AreEqual(502, options.Port);
    }

    [TestMethod]
    public void AddDevTermFrontEnd_TcpListener_ConfiguresListenerMode()
    {
        var cliOptions = new CliOptions { Transport = "tcp", Port = "9000", Listen = true };
        var provider = new ServiceCollection().AddDevTermFrontEnd(cliOptions).BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<TcpTransportOptions>>().Value;

        Assert.AreEqual(TcpTransportMode.Listener, options.Mode);
    }

    [TestMethod]
    public void AddDevTermFrontEnd_HidTransport_ResolvesHidTransportConfiguredFromCliOptions()
    {
        var cliOptions = new CliOptions { Transport = "hid", VendorId = 0x1915, ProductId = 0xAFDA, SerialNumber = "12345" };
        var provider = new ServiceCollection().AddDevTermFrontEnd(cliOptions).BuildServiceProvider();

        var transport = provider.GetRequiredService<ITransport>();
        var options = provider.GetRequiredService<IOptions<HidTransportOptions>>().Value;

        Assert.IsInstanceOfType<HidTransport>(transport);
        Assert.AreEqual(0x1915, options.VendorId);
        Assert.AreEqual(0xAFDA, options.ProductId);
        Assert.AreEqual("12345", options.SerialNumber);
    }

    [TestMethod]
    public void AddDevTermFrontEnd_ConfiguresAsciiPresenterMaxLineLengthFromCliOptions()
    {
        var cliOptions = new CliOptions { Transport = "serial", Port = "COM3", AsciiMaxLineLength = 512 };
        var provider = new ServiceCollection().AddDevTermFrontEnd(cliOptions).BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<AsciiPresenterOptions>>().Value;

        Assert.AreEqual(512, options.MaxLineLength);
    }

    [TestMethod]
    public void AddDevTermFrontEnd_RegistersTextPresentersInTheCatalog()
    {
        var cliOptions = new CliOptions { Transport = "serial", Port = "COM3" };
        var provider = new ServiceCollection().AddDevTermFrontEnd(cliOptions).BuildServiceProvider();

        var catalog = provider.GetRequiredService<PresenterCatalog>();

        CollectionAssert.Contains(catalog.Names.ToList(), "ascii");
        CollectionAssert.Contains(catalog.Names.ToList(), "hex");
    }
}
