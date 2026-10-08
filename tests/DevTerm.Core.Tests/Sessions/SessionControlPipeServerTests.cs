using System.IO.Pipelines;
using System.IO.Pipes;
using System.Text;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;
using DevTerm.Test.Utilities;
using Moq;

namespace DevTerm.Core.Tests.Sessions;

/// <summary>A second process drives a session over the read-write control pipe.</summary>
[TestCategory(TestCategories.Integration)]
[TestClass]
public sealed class SessionControlPipeServerTests
{
    public required TestContext TestContext { get; set; }

    [TestMethod]
    public async Task SendCommands_ReachTheTransport_AndEventsComeBack()
    {
        var pipe = new Pipe();
        var written = new List<byte>();
        var transport = new Mock<ITransport>();
        transport.SetupGet(t => t.Input).Returns(pipe.Reader);
        transport.SetupGet(t => t.State).Returns(ConnectionState.Open);
        transport.Setup(t => t.WriteAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Callback<ReadOnlyMemory<byte>, CancellationToken>((data, _) => written.AddRange(data.ToArray()))
            .Returns(Task.CompletedTask);
        await using var session = new Session(transport.Object, new Pipeline([]));
        await session.OpenAsync(TestContext.CancellationToken);

        var name = "test-" + Guid.NewGuid().ToString("N");
        await using var server = new SessionControlPipeServer(session, name, text => text == "bad" ? (null, "nope") : (Encoding.ASCII.GetBytes(text + "\n"), null));
        using var registration = session.AddObserver(server);
        await using var client = new NamedPipeClientStream(".", SessionControlPipeServer.PipeNameFor(name), PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(5000, TestContext.CancellationToken);
        using var reader = new StreamReader(client, Encoding.UTF8);
        await using var writer = new StreamWriter(client, new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = true };

        await writer.WriteLineAsync("send *IDN?");
        Assert.AreEqual("ok", await ReadReplyAsync(reader));
        await writer.WriteLineAsync("sendhex 41 42");
        Assert.AreEqual("ok", await ReadReplyAsync(reader));
        await writer.WriteLineAsync("send bad");
        Assert.AreEqual("error nope", await ReadReplyAsync(reader));
        await writer.WriteLineAsync("sendhex zz");
        StringAssert.StartsWith(await ReadReplyAsync(reader), "error ");
        await writer.WriteLineAsync("frobnicate");
        StringAssert.StartsWith(await ReadReplyAsync(reader), "error unknown command");

        CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("*IDN?\nAB"), written.ToArray());
    }

    [TestMethod]
    public async Task ControlClient_SendsCommandsAndPrintsReplies()
    {
        var transport = new Mock<ITransport>();
        transport.SetupGet(t => t.Input).Returns(new Pipe().Reader);
        transport.SetupGet(t => t.State).Returns(ConnectionState.Open);
        transport.Setup(t => t.WriteAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        await using var session = new Session(transport.Object, new Pipeline([]));
        await session.OpenAsync(TestContext.CancellationToken);
        var name = "test-" + Guid.NewGuid().ToString("N");
        await using var server = new SessionControlPipeServer(session, name, text => (Encoding.ASCII.GetBytes(text), null));

        var lines = new List<string>();
        await SessionControlClient.RunAsync(name, Commands("ping", "send hi", "nonsense"), l => { lock (lines) { lines.Add(l); } }, cancellationToken: TestContext.CancellationToken);

        string[] seen;
        lock (lines)
        {
            seen = [.. lines];
        }

        Assert.AreEqual(2, seen.Count(l => l == "ok"));
        Assert.IsTrue(seen.Any(l => l.StartsWith("error unknown command", StringComparison.Ordinal)));
    }

    private static async IAsyncEnumerable<string> Commands(params string[] commands)
    {
        foreach (var command in commands)
        {
            yield return command;
            await Task.Yield();
        }
    }

    // Event lines (open/tx/rx/closed) share the stream with replies; skip them to reach the next reply.
    private async Task<string> ReadReplyAsync(StreamReader reader)
    {
        while (await reader.ReadLineAsync(TestContext.CancellationToken) is { } line)
        {
            if (line == "ok" || line.StartsWith("error", StringComparison.Ordinal))
            {
                return line;
            }
        }

        throw new EndOfStreamException();
    }
}
