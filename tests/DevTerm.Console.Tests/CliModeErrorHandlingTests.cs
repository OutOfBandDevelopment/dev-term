using System.Text;
using System.Threading.Channels;
using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Presenters.Text;
using DevTerm.Test.Utilities;

namespace DevTerm.Console.Tests;

/// <summary>
/// The CLI never exits or crashes on an error after it's connected: invalid input is rejected
/// with the connection left alone, and a lost connection (a send or read failure) is reported once
/// and reconnected by the next line typed.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class CliModeErrorHandlingTests
{
    public required TestContext TestContext { get; set; }

    /// <summary>A stdin whose lines the test feeds one at a time, when it's ready for the next.</summary>
    private sealed class ScriptedStdin : TextReader
    {
        private readonly Channel<string?> _lines = Channel.CreateUnbounded<string?>();

        public void Type(string line) => _lines.Writer.TryWrite(line);

        public void EndOfInput() => _lines.Writer.TryWrite(null);

        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) =>
            await _lines.Reader.ReadAsync(cancellationToken);
    }

    /// <summary>A thread-safe StringWriter the test can poll while the loop is still running.</summary>
    private sealed class SyncWriter : StringWriter
    {
        public override void WriteLine(string? value)
        {
            lock (this)
            {
                base.WriteLine(value);
            }
        }

        public string Snapshot()
        {
            lock (this)
            {
                return ToString();
            }
        }
    }

    private sealed record Harness(FakeTransport Transport, ScriptedStdin Stdin, SyncWriter Stdout, SyncWriter Stderr, Task<int> Run);

    private static Harness Start(string parser = "ascii")
    {
        var transport = new FakeTransport();
        var ascii = new AsciiPresenter(Microsoft.Extensions.Options.Options.Create(new AsciiPresenterOptions()));
        var catalog = new PresenterCatalog([ascii, new HexPresenter()]);
        var session = new Session(transport, new Pipeline([ascii]));
        var options = new CliOptions { Transport = "loopback", Presenter = ["ascii"], Parser = parser };
        var stdin = new ScriptedStdin();
        var stdout = new SyncWriter();
        var stderr = new SyncWriter();
        var run = CliMode.RunAsync(session, catalog, options, stdin, stdout, stderr);
        return new Harness(transport, stdin, stdout, stderr, run);
    }

    private async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"Timed out waiting for: {what}");
            }

            await Task.Delay(10, TestContext.CancellationToken);
        }
    }

    [TestMethod]
    public async Task InvalidHexLine_IsRejected_AndTheNextValidLineStillSends()
    {
        var h = Start(parser: "hex");

        h.Stdin.Type("OUTPut?");
        h.Stdin.Type("0A0B");
        await WaitUntilAsync(() => h.Transport.WrittenPayloads.Count == 1, "the valid line to be sent");
        h.Stdin.EndOfInput();

        Assert.AreEqual(0, await h.Run.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken));
        StringAssert.Contains(h.Stderr.Snapshot(), "isn't valid hex input");
        Assert.AreSequenceEqual(new byte[] { 0x0A, 0x0B }, h.Transport.WrittenPayloads[0]);
        Assert.AreEqual(1, h.Transport.OpenCount, "An invalid line must not disconnect.");
    }

    [TestMethod]
    public async Task SendFailure_IsReportedOnce_ThenTheNextLineReconnectsAndSends()
    {
        var h = Start();
        await WaitUntilAsync(() => h.Transport.OpenCount == 1, "the initial connect");

        h.Transport.FailNextWrite(new IOException("device unplugged"));
        h.Stdin.Type("first");
        await WaitUntilAsync(() => h.Stderr.Snapshot().Contains("device unplugged"), "the failure to be reported");

        h.Stdin.Type("second");
        await WaitUntilAsync(() => h.Transport.WrittenPayloads.Count == 1, "the retry to be sent");
        h.Stdin.EndOfInput();

        Assert.AreEqual(0, await h.Run.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken));
        var stderr = h.Stderr.Snapshot();
        Assert.AreEqual(1, CountOf(stderr, "device unplugged"), "Reported exactly once.");
        StringAssert.Contains(stderr, "Reconnecting");
        Assert.AreEqual(2, h.Transport.OpenCount);
        Assert.AreEqual("second", Encoding.ASCII.GetString(h.Transport.WrittenPayloads[0]));
    }

    [TestMethod]
    public async Task ReadFailure_IsReported_AndTheNextLineReconnects()
    {
        var h = Start();
        await WaitUntilAsync(() => h.Transport.OpenCount == 1, "the initial connect");

        await h.Transport.FailReadAsync(new IOException("socket reset"));
        await WaitUntilAsync(() => h.Stderr.Snapshot().Contains("socket reset"), "the read failure to be reported");

        h.Stdin.Type("hello");
        await WaitUntilAsync(() => h.Transport.WrittenPayloads.Count == 1, "the line to be sent after reconnecting");
        h.Stdin.EndOfInput();

        Assert.AreEqual(0, await h.Run.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken));
        Assert.AreEqual(2, h.Transport.OpenCount);
    }

    [TestMethod]
    public async Task ReconnectFails_IsReportedAndTheLoopKeepsGoing()
    {
        var h = Start();
        await WaitUntilAsync(() => h.Transport.OpenCount == 1, "the initial connect");

        h.Transport.FailNextWrite(new IOException("gone"));
        h.Stdin.Type("one");
        await WaitUntilAsync(() => h.Stderr.Snapshot().Contains("gone"), "the send failure");

        h.Transport.FailNextOpen(new IOException("port is in use"));
        h.Stdin.Type("two");
        await WaitUntilAsync(() => h.Stderr.Snapshot().Contains("port is in use"), "the reconnect failure");

        h.Stdin.Type("three");
        await WaitUntilAsync(() => h.Transport.WrittenPayloads.Count == 1, "the third line after a successful reconnect");
        h.Stdin.EndOfInput();

        Assert.AreEqual(0, await h.Run.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken));
        Assert.AreEqual("three", Encoding.ASCII.GetString(h.Transport.WrittenPayloads[0]));
    }

    private static int CountOf(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
