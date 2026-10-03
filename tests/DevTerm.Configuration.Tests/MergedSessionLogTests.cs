using System.IO.Pipelines;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;
using DevTerm.Test.Utilities;
using Moq;

namespace DevTerm.Configuration.Tests;

[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class MergedSessionLogTests
{
    public TestContext TestContext { get; set; } = null!;

    private static (Session Session, Pipe Pipe) Live()
    {
        var pipe = new Pipe();
        var transport = new Mock<ITransport>();
        transport.SetupGet(t => t.Input).Returns(pipe.Reader);
        transport.SetupGet(t => t.State).Returns(ConnectionState.Open);
        return (new Session(transport.Object, new Pipeline([])), pipe);
    }

    [TestMethod]
    public void Escape_ShowsControlBytesVisibly()
    {
        Assert.AreEqual("*IDN?\\r\\n\\x01\\tx", MergedSessionLog.Escape("*IDN?\r\n\u0001\tx"u8));
    }

    [TestMethod]
    public async Task TwoSessions_AreInterleavedInArrivalOrder_TaggedWithTheirDevice()
    {
        var clock = new ManualTimeProvider();
        var log = new MergedSessionLog(clock);
        var (a, pipeA) = Live();
        var (b, _) = Live();
        await using var disposeA = a;
        await using var disposeB = b;
        log.Track("a", a, "scope");
        log.Track("b", b, "psu");
        await a.OpenAsync(TestContext.CancellationToken);
        await b.OpenAsync(TestContext.CancellationToken);

        await a.SendAsync("*IDN?\n"u8.ToArray(), TestContext.CancellationToken);
        clock.Advance(TimeSpan.FromMilliseconds(250));
        await b.SendAsync("VOUT1?\n"u8.ToArray(), TestContext.CancellationToken);
        clock.Advance(TimeSpan.FromMilliseconds(250));
        await pipeA.Writer.WriteAsync("TEK\n"u8.ToArray(), TestContext.CancellationToken);
        SpinWait.SpinUntil(() => log.Entries.Count >= 5, TimeSpan.FromSeconds(5));

        var lines = log.Entries.Where(e => !e.Text.StartsWith("--", StringComparison.Ordinal)).Select(e => (e.Device, e.Sent, e.Text)).ToList();
        Assert.AreSequenceEqual([("scope", true, "*IDN?\\n"), ("psu", true, "VOUT1?\\n"), ("scope", false, "TEK\\n")], lines);
        Assert.IsTrue(log.Entries.Zip(log.Entries.Skip(1)).All(p => p.First.Time <= p.Second.Time), "Entries are time-ordered.");

        log.Untrack("a");
        var count = log.Entries.Count;
        await a.SendAsync("X"u8.ToArray(), TestContext.CancellationToken);
        Assert.AreEqual(count, log.Entries.Count, "An untracked session stops contributing.");
    }
}
