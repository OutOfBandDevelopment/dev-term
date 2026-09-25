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
