using System.IO.Pipes;
using System.Text;
using DevTerm.Core.Sessions;
using DevTerm.Test.Utilities;

namespace DevTerm.Core.Tests.Sessions;

/// <summary>A second client tails a session's traffic over a real (local) named pipe.</summary>
[TestCategory(TestCategories.Integration)]
[TestClass]
public sealed class SessionPipeServerTests
{
    public required TestContext TestContext { get; set; }

    [TestMethod]
    public async Task Client_SeesOpenAndTraffic_AsLines()
    {
        var name = "test-" + Guid.NewGuid().ToString("N");
        await using var server = new SessionPipeServer(name);
        await using var client = new NamedPipeClientStream(".", SessionPipeServer.PipeNameFor(name), PipeDirection.In);
        await client.ConnectAsync(5000, TestContext.CancellationToken);
        using var reader = new StreamReader(client, Encoding.UTF8);

        // The accept loop registers the client just after the connect completes.
        await Task.Delay(200, TestContext.CancellationToken);
        server.OnOpened();
        server.OnSent(new byte[] { 0x41, 0x0A });
        server.OnReceived(new System.Buffers.ReadOnlySequence<byte>(new byte[] { 0x42 }));
        server.OnClosed(true, null);

        Assert.AreEqual("open", await reader.ReadLineAsync(TestContext.CancellationToken));
        Assert.AreEqual("tx 410A", await reader.ReadLineAsync(TestContext.CancellationToken));
        Assert.AreEqual("rx 42", await reader.ReadLineAsync(TestContext.CancellationToken));
        Assert.AreEqual("closed requested", await reader.ReadLineAsync(TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task NoClient_BroadcastIsHarmless()
    {
        await using var server = new SessionPipeServer("test-" + Guid.NewGuid().ToString("N"));

        server.OnOpened();
        server.OnSent(new byte[] { 1 });

        Assert.IsNotNull(server.PipeName);
    }

    [TestMethod]
    public async Task SessionPipeClient_TailsTheServer_UntilItIsDisposed()
    {
        var name = "test-" + Guid.NewGuid().ToString("N");
        var server = new SessionPipeServer(name);
        var lines = new List<string>();
        var reading = Task.Run(async () =>
        {
            await foreach (var line in SessionPipeClient.ReadLinesAsync(name, 5000, TestContext.CancellationToken))
            {
                lines.Add(line);
            }
        }, TestContext.CancellationToken);

        await Task.Delay(500, TestContext.CancellationToken);
        server.OnReceived(new System.Buffers.ReadOnlySequence<byte>(new byte[] { 0x48, 0x69 }));
        await Task.Delay(200, TestContext.CancellationToken);
        await server.DisposeAsync();
        await reading.WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationToken);

        CollectionAssert.AreEqual(new[] { "rx 4869" }, lines);
    }

    [TestMethod]
    public async Task SessionPipeClient_NoServer_TimesOut() =>
        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await foreach (var unused in SessionPipeClient.ReadLinesAsync("absent-" + Guid.NewGuid().ToString("N"), 300, TestContext.CancellationToken))
            {
            }
        });

    [TestMethod]
    public void Describe_AddsAsciiToTrafficLines_AndLeavesOthersAlone()
    {
        Assert.AreEqual("rx 48690A  |Hi.|", SessionPipeClient.Describe("rx 48690A"));
        Assert.AreEqual("open", SessionPipeClient.Describe("open"));
        Assert.AreEqual("rx zz", SessionPipeClient.Describe("rx zz"));
    }
}
