using System.Buffers;
using System.Text;
using DevTerm.Core.Routing;
using DevTerm.Test.Utilities;

namespace DevTerm.Core.Tests.Routing;

/// <summary>Rule validation and sample testing, plus the router's counters and the confirm gate on broker-to-device sends.</summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class RoutingRuleTests
{
    private sealed class NullSink : IMessageSink
    {
        public Task PublishAsync(string topic, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static RoutingRule Cmd(bool confirm) => new() { Direction = RoutingDirection.BrokerToDevice, Topic = "cmd", Match = "^(?<c>[A-Z]+)$", Send = "${c}?", Confirm = confirm };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
    }

    [TestMethod]
    public void Validate_AcceptsAGoodRule_AndNamesTheProblemOtherwise()
    {
        Assert.IsNull(Cmd(false).Validate());
        Assert.Contains("valid regex", new RoutingRule { Topic = "t", Match = "(" }.Validate()!);
        Assert.Contains("Topic is required", new RoutingRule { Match = "x" }.Validate()!);
        Assert.Contains("Send is required", new RoutingRule { Direction = RoutingDirection.BrokerToDevice, Topic = "t" }.Validate()!);
        Assert.Contains("${nope}", new RoutingRule { Topic = "t", Match = "^(?<a>x)", Payload = "${nope}" }.Validate()!);
    }

    [TestMethod]
    public void Test_ShowsWhatARuleWouldDo_WithoutSideEffects()
    {
        var rule = new RoutingRule { Direction = RoutingDirection.DeviceToBroker, Match = "^A=(?<a>[0-9.]+)", Topic = "s/${a}", Payload = "a=${a}" };

        Assert.AreEqual(new RuleTestResult("s/7.25", "a=7.25"), rule.Test("A=7.25"));
        Assert.IsNull(rule.Test("nothing"));
        Assert.AreEqual(new RuleTestResult("cmd", "PING?"), Cmd(false).Test("PING"));
    }

    [TestMethod]
    public void Router_CountsHitsAndUnmatchedLines()
    {
        var hit = new RoutingRule { Direction = RoutingDirection.DeviceToBroker, Match = "^A=", Topic = "s" };
        var router = new MessageRouter(new RoutingRuleSet { Rules = [hit] }, new NullSink());

        router.Render(new ReadOnlySequence<byte>(Encoding.ASCII.GetBytes("A=1\r\nA=2\r\nzzz\r\n")));

        Assert.AreEqual(2, router.HitCount(hit));
        Assert.AreEqual(1, router.Unmatched);
    }

    [TestMethod]
    public void ConfirmRule_WithNoConfirmer_IsDroppedNotSent()
    {
        var router = new MessageRouter(new RoutingRuleSet { Rules = [Cmd(true)] }, new NullSink());
        var sent = 0;
        router.Originated += (_, _) => sent++;

        router.OnBrokerMessage("cmd", "PING");

        Assert.AreEqual(0, sent);
        Assert.AreEqual(1, router.Dropped);
    }

    [TestMethod]
    public async Task ConfirmRule_SendOnceAsksEachTime_AlwaysAsksOnce_DropSendsNothing()
    {
        var rule = Cmd(true);
        var router = new MessageRouter(new RoutingRuleSet { Rules = [rule] }, new NullSink());
        var asked = 0;
        var answer = RoutingConfirmChoice.SendOnce;
        router.Confirm = (_, _) =>
        {
            asked++;
            return Task.FromResult(answer);
        };
        var sent = 0;
        router.Originated += (_, _) => Interlocked.Increment(ref sent);

        router.OnBrokerMessage("cmd", "PING");
        router.OnBrokerMessage("cmd", "PING");
        await WaitUntilAsync(() => Volatile.Read(ref sent) == 2);
        Assert.AreEqual(2, asked);

        answer = RoutingConfirmChoice.Drop;
        router.OnBrokerMessage("cmd", "PING");
        await WaitUntilAsync(() => router.Dropped == 1);
        Assert.AreEqual(2, sent);

        answer = RoutingConfirmChoice.Always;
        router.OnBrokerMessage("cmd", "PING");
        await WaitUntilAsync(() => Volatile.Read(ref sent) == 3);
        router.OnBrokerMessage("cmd", "PING");
        Assert.AreEqual(4, sent);
        Assert.AreEqual(4, asked);
    }
}
