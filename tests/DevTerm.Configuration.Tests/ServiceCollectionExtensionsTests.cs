using DevTerm.Core.Presenters;
using DevTerm.Core.Transports;
using DevTerm.Presenters.Text;
using DevTerm.Test.Utilities;
using DevTerm.Transports.Hid;
using DevTerm.Transports.Loopback;
using DevTerm.Transports.Mqtt;
using DevTerm.Transports.Rfc2217;
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
[TestCategory(TestCategories.Unit)]
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
    public void AddDevTermFrontEnd_MqttTransport_ResolvesMqttTransportConfiguredFromCliOptions()
    {
        var cliOptions = new CliOptions { Transport = "mqtt", Host = "broker", Port = "1883", Subscribe = "a/#, b/+", Publish = "cmd", Username = "u" };
        var provider = new ServiceCollection().AddDevTermFrontEnd(cliOptions).BuildServiceProvider();

        var transport = provider.GetRequiredService<ITransport>();
        var options = provider.GetRequiredService<IOptions<MqttTransportOptions>>().Value;

        Assert.IsInstanceOfType<MqttTransport>(transport);
        Assert.AreEqual("broker", options.Host);
        Assert.AreEqual(1883, options.Port);
        CollectionAssert.AreEqual(new[] { "a/#", "b/+" }, options.SubscribeTopics);
        Assert.AreEqual("cmd", options.PublishTopic);
        Assert.AreEqual("u", options.Username);
    }

    [TestMethod]
    public void AddDevTermFrontEnd_Rfc2217Transport_ResolvesRfc2217TransportConfiguredFromCliOptions()
    {
        var cliOptions = new CliOptions { Transport = "rfc2217", Host = "device.local", Port = "2217", Baud = 4800 };
        var provider = new ServiceCollection().AddDevTermFrontEnd(cliOptions).BuildServiceProvider();

        var transport = provider.GetRequiredService<ITransport>();
        var options = provider.GetRequiredService<IOptions<Rfc2217TransportOptions>>().Value;

        Assert.IsInstanceOfType<Rfc2217Transport>(transport);
        Assert.AreEqual("device.local", options.Host);
        Assert.AreEqual(2217, options.Port);
        Assert.AreEqual(4800, options.BaudRate);
    }

    [TestMethod]
    public void AddDevTermFrontEnd_LoopbackTransport_ConfiguresSampleIntervalFromCliOptions()
    {
        var cliOptions = new CliOptions { Transport = "loopback", LoopbackSampleIntervalMs = 250 };
        var provider = new ServiceCollection().AddDevTermFrontEnd(cliOptions).BuildServiceProvider();

        var transport = provider.GetRequiredService<ITransport>();
        var options = provider.GetRequiredService<IOptions<LoopbackTransportOptions>>().Value;

        Assert.IsInstanceOfType<LoopbackTransport>(transport);
        Assert.AreEqual(250, options.SampleIntervalMs);
    }

    [TestMethod]
    public void AddDevTermFrontEnd_SerialTransport_ConfiguresWriteByteDelayFromCliOptions()
    {
        var cliOptions = new CliOptions { Transport = "serial", Port = "COM3", WriteByteDelayMs = 20 };
        var provider = new ServiceCollection().AddDevTermFrontEnd(cliOptions).BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<SerialTransportOptions>>().Value;

        Assert.AreEqual(20, options.WriteByteDelayMs);
    }

    [TestMethod]
    public void AddDevTermFrontEnd_TcpTransport_ConfiguresWriteByteDelayFromCliOptions()
    {
        var cliOptions = new CliOptions { Transport = "tcp", Host = "device.local", Port = "502", WriteByteDelayMs = 20 };
        var provider = new ServiceCollection().AddDevTermFrontEnd(cliOptions).BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<TcpTransportOptions>>().Value;

        Assert.AreEqual(20, options.WriteByteDelayMs);
    }

    [TestMethod]
    public void AddDevTermFrontEnd_Rfc2217Transport_ConfiguresWriteByteDelayFromCliOptions()
    {
        var cliOptions = new CliOptions { Transport = "rfc2217", Host = "device.local", Port = "2217", WriteByteDelayMs = 20 };
        var provider = new ServiceCollection().AddDevTermFrontEnd(cliOptions).BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<Rfc2217TransportOptions>>().Value;

        Assert.AreEqual(20, options.WriteByteDelayMs);
    }

    [TestMethod]
    public void AddDevTermFrontEnd_HidTransport_DoesNotExposeWriteByteDelay()
    {
        var cliOptions = new CliOptions { Transport = "hid", VendorId = 0x1915, ProductId = 0xAFDA, WriteByteDelayMs = 20 };
        var provider = new ServiceCollection().AddDevTermFrontEnd(cliOptions).BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<HidTransportOptions>>().Value;

        Assert.IsFalse(options.GetType().GetProperties().Any(p => p.Name == "WriteByteDelayMs"), "HID writes one atomic report per call; write pacing doesn't apply.");
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

        Assert.Contains("ascii", catalog.Names.ToList());
        Assert.Contains("hex", catalog.Names.ToList());
    }

    [TestMethod]
    public void AddDevTermFrontEnd_EachCatalogGetsItsOwnStatefulPresenterInstances()
    {
        // Two sessions in one process must never share a buffering presenter (the ASCII line
        // accumulator, the SCPI pending-query queue) - they'd interleave each other's partial frames.
        var cliOptions = new CliOptions { Transport = "serial", Port = "COM3" };
        var provider = new ServiceCollection().AddDevTermFrontEnd(cliOptions).BuildServiceProvider();

        var first = provider.GetRequiredService<PresenterCatalog>();
        var second = provider.GetRequiredService<PresenterCatalog>();

        Assert.AreNotSame(first.Get("ascii"), second.Get("ascii"));
        Assert.AreSame(first.Get("ascii"), first.Get("ascii"));
    }
}
