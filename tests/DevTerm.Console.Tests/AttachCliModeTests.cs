using System.Text;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Test.Utilities;

namespace DevTerm.Console.Tests;

[TestClass]
public sealed class AttachCliModeTests
{
    public required TestContext TestContext { get; set; }

    [TestCategory(TestCategories.Unit)]
    [TestMethod]
    [DataRow("hello", "send hello", false)]
    [DataRow(":hex AA BB", "sendhex AA BB", false)]
    [DataRow(":ping", "ping", false)]
    [DataRow("::weird", "send :weird", false)]
    [DataRow(":nonsense", "send :nonsense", false)]
    [DataRow(":quit", null, true)]
    [DataRow("", null, false)]
    public void ToCommand_MapsTypedLines(string line, string? command, bool quit)
    {
        var (actual, actualQuit) = AttachCliMode.ToCommand(line);

        Assert.AreEqual(command, actual);
        Assert.AreEqual(quit, actualQuit);
    }

    [TestCategory(TestCategories.Integration)]
    [TestMethod]
    public async Task Repl_SendsTypedLinesToTheNamedSession_AndPrintsTraffic()
    {
        var transport = new FakeTransport();
        await using var session = new Session(transport, new Pipeline([]));
        await session.OpenAsync(TestContext.CancellationToken);
        var name = "attach-" + Guid.NewGuid().ToString("N");
        await using var server = new SessionControlPipeServer(session, name, text => (Encoding.ASCII.GetBytes(text + "\n"), null));
        using var registration = session.AddObserver(server);

        var stdout = new StringWriter();
        var exit = await AttachCliMode.RunAsync(name, new StringReader("*IDN?\n:ping\n:hex 41 42\n:quit\n"), stdout, new StringWriter(), TestContext.CancellationToken);

        Assert.AreEqual(0, exit);
        Assert.AreEqual("*IDN?\nAB", string.Concat(transport.WrittenPayloads.Select(Encoding.ASCII.GetString)));
        StringAssert.Contains(stdout.ToString(), "ok");
    }

    [TestCategory(TestCategories.Integration)]
    [TestMethod]
    public async Task Repl_WithNoSuchSession_ExitsOneWithAHint()
    {
        var stderr = new StringWriter();

        var exit = await AttachCliMode.RunAsync("nobody-" + Guid.NewGuid().ToString("N"), new StringReader("hi\n"), new StringWriter(), stderr, TestContext.CancellationToken);

        Assert.AreEqual(1, exit);
        StringAssert.Contains(stderr.ToString(), "--control");
    }
}
