using System.Text;
using DevTerm.Core.Transports;
using DevTerm.Test.Utilities;
using Microsoft.Extensions.Options;

namespace DevTerm.Transports.Loopback.Tests;

/// <summary>
/// Exercises <see cref="LoopbackTransport"/> directly (no <c>Session</c>/presenter involved — that
/// composition is covered at the console-front-end level) to prove the scripted request/response
/// behavior works as designed. No real transport I/O anywhere, so this is <c>UNIT</c>.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Loopback)]
[TestClass]
public sealed class LoopbackTransportTests
{
    private static LoopbackTransport CreateTransport() => new(Options.Create(new LoopbackTransportOptions()));

    // A single StreamReader must be reused across reads on the same transport: it buffers ahead
    // from the underlying PipeReader, so a fresh StreamReader per call would silently drop
    // already-buffered-but-unconsumed response lines.
    private static StreamReader CreateReader(LoopbackTransport transport) => new(transport.Input.AsStream(), Encoding.ASCII);

    private static async Task<string> ReadLineAsync(StreamReader reader)
    {
        var line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        return line ?? throw new InvalidOperationException("Expected a response line but got none.");
    }

    [TestMethod]
    public async Task OpenAsync_SetsStateToOpen()
    {
        var transport = CreateTransport();

        await transport.OpenAsync(TestContext.CancellationToken);

        Assert.AreEqual(ConnectionState.Open, transport.State);
    }

    [TestMethod]
    public async Task CloseAsync_SetsStateToClosed()
    {
        var transport = CreateTransport();
        await transport.OpenAsync(TestContext.CancellationToken);

        await transport.CloseAsync(TestContext.CancellationToken);

        Assert.AreEqual(ConnectionState.Closed, transport.State);
    }

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public async Task OpenAsync_AfterClose_CanReconnectAndExchangeData()
    {
        // Regression test for bug 003: the transport used to keep one Pipe for its whole lifetime,
        // so CloseAsync completed its writer permanently - a later OpenAsync just flipped State back
        // to Open over an already-completed pipe, and the next WriteAsync threw "Writing is not
        // allowed after writer was completed". See docs/bugs/fixed/003-loopback-cannot-reconnect.md.
        var transport = CreateTransport();
        await transport.OpenAsync(TestContext.CancellationToken);
        await transport.CloseAsync(TestContext.CancellationToken);

        await transport.OpenAsync(TestContext.CancellationToken);
        await transport.WriteAsync(Encoding.ASCII.GetBytes("hello\r\n"), TestContext.CancellationToken);

        Assert.AreEqual("From Loopback test", await ReadLineAsync(CreateReader(transport)));
    }

    [TestMethod]
    public async Task WriteAsync_WithLiteralRule_PushesItsFixedResponseLine()
    {
        var transport = CreateTransport();
        await transport.OpenAsync(TestContext.CancellationToken);

        await transport.WriteAsync(Encoding.ASCII.GetBytes("hello\r\n"), TestContext.CancellationToken);

        Assert.AreEqual("From Loopback test", await ReadLineAsync(CreateReader(transport)));
    }

    [TestMethod]
    public async Task WriteAsync_WithSendStreamCommand_PushesADeterministicAsciiRun()
    {
        var transport = CreateTransport();
        await transport.OpenAsync(TestContext.CancellationToken);

        await transport.WriteAsync(Encoding.ASCII.GetBytes("Send Stream: 30, ascii\r\n"), TestContext.CancellationToken);

        Assert.AreEqual("ABCDEFGHIJKLMNOPQRSTUVWXYZABCD", await ReadLineAsync(CreateReader(transport)));
    }

    [TestMethod]
    public async Task WriteAsync_WithSendEventsCommand_PushesOneLinePerEvent()
    {
        var transport = CreateTransport();
        await transport.OpenAsync(TestContext.CancellationToken);

        await transport.WriteAsync(Encoding.ASCII.GetBytes("Send Events: 3\r\n"), TestContext.CancellationToken);

        var reader = CreateReader(transport);
        Assert.AreEqual("Event 1", await ReadLineAsync(reader));
        Assert.AreEqual("Event 2", await ReadLineAsync(reader));
        Assert.AreEqual("Event 3", await ReadLineAsync(reader));
    }

    [TestMethod]
    public async Task SimulatedSensor_MeasureThenSamples_ContinueOneDeterministicSequence()
    {
        var transport = CreateTransport();
        await transport.OpenAsync(TestContext.CancellationToken);
        var reader = CreateReader(transport);

        await transport.WriteAsync(Encoding.ASCII.GetBytes("MEAS?\n"), TestContext.CancellationToken);
        Assert.AreEqual("A=50.00 B=90.00 C=0.00 X=0.80 Y=0.00 Z=0.00 R=0.50 T=0.00 H=0.00", await ReadLineAsync(reader));

        await transport.WriteAsync(Encoding.ASCII.GetBytes("Samples: 3\n"), TestContext.CancellationToken);
        Assert.AreEqual(LoopbackGenerators.SensorSample(1), await ReadLineAsync(reader));
        Assert.AreEqual(LoopbackGenerators.SensorSample(2), await ReadLineAsync(reader));
        Assert.AreEqual(LoopbackGenerators.SensorSample(3), await ReadLineAsync(reader));
        Assert.StartsWith("A=", LoopbackGenerators.SensorSample(3));
        Assert.AreNotEqual(LoopbackGenerators.SensorSample(1), LoopbackGenerators.SensorSample(2));
    }

    [TestMethod]
    public async Task WriteAsync_WithHelpCommand_PushesTheCommandList()
    {
        var transport = CreateTransport();
        await transport.OpenAsync(TestContext.CancellationToken);

        await transport.WriteAsync(Encoding.ASCII.GetBytes("help\r\n"), TestContext.CancellationToken);

        var reader = CreateReader(transport);
        foreach (var expected in LoopbackScript.HelpLines)
        {
            Assert.AreEqual(expected, await ReadLineAsync(reader));
        }
    }

    [TestMethod]
    public async Task WriteAsync_WithQuestionMarkCommand_PushesTheCommandList()
    {
        var transport = CreateTransport();
        await transport.OpenAsync(TestContext.CancellationToken);

        await transport.WriteAsync(Encoding.ASCII.GetBytes("?\r\n"), TestContext.CancellationToken);

        Assert.AreEqual(LoopbackScript.HelpLines[0], await ReadLineAsync(CreateReader(transport)));
    }

    [TestMethod]
    public async Task WriteAsync_WithDifferentCasing_StillMatches()
    {
        var transport = CreateTransport();
        await transport.OpenAsync(TestContext.CancellationToken);

        await transport.WriteAsync(Encoding.ASCII.GetBytes("HELLO\r\n"), TestContext.CancellationToken);

        Assert.AreEqual("From Loopback test", await ReadLineAsync(CreateReader(transport)));
    }

    [TestMethod]
    public async Task WriteAsync_WithUnrecognizedCommand_PushesAVisibleMarker()
    {
        var transport = CreateTransport();
        await transport.OpenAsync(TestContext.CancellationToken);

        await transport.WriteAsync(Encoding.ASCII.GetBytes("not a real command\r\n"), TestContext.CancellationToken);

        Assert.Contains("not a real command", await ReadLineAsync(CreateReader(transport)));
    }

    public required TestContext TestContext { get; set; }
}
