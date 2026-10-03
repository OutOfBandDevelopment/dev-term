using System.Text;
using DevTerm.Core.Presenters;
using DevTerm.Core.Routing;
using DevTerm.Core.Sessions;
using DevTerm.Test.Utilities;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace DevTerm.Transports.Loopback.Tests;

/// <summary>
/// The routing-proxy proof of concept: a device message detected over the loopback transport is published to a
/// (fake, in-memory) broker, and a broker message triggers a device action, with the router added to a live session.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Loopback)]
[TestClass]
public sealed class MessageRouterTests
{
    private const string _rulesJson = """
        {
          "rules": [
            { "direction": "DeviceToBroker", "match": "^A=(?<a>[0-9.]+) B=(?<b>[0-9.]+)", "topic": "devterm/loopback/sensor", "payload": "a=${a};b=${b}" },
            { "direction": "BrokerToDevice", "topic": "devterm/loopback/cmd", "match": "^(?<cmd>[A-Za-z]+)$", "send": "${cmd}?" }
          ]
        }
        """;

    private sealed class FakeBroker : IMessageSink
    {
        public List<(string Topic, string Payload)> Published { get; } = [];

        public Task PublishAsync(string topic, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
        {
            lock (Published)
            {
                Published.Add((topic, Encoding.UTF8.GetString(payload.Span)));
            }

            return Task.CompletedTask;
        }
    }

    public required TestContext TestContext { get; set; }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
    }

    [TestMethod]
    public void RuleSet_RoundTripsThroughJson()
    {
        var rules = RoutingRuleSet.FromJson(_rulesJson);

        Assert.AreEqual(2, rules.Rules.Count);
        Assert.AreEqual(RoutingDirection.BrokerToDevice, rules.Rules[1].Direction);
        Assert.AreEqual(2, RoutingRuleSet.FromJson(rules.ToJson()).Rules.Count);
    }

    [TestMethod]
    public async Task BrokerMessage_TriggersADeviceAction_AndTheDeviceReplyIsPublished()
    {
        var broker = new FakeBroker();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
        var router = new MessageRouter(RoutingRuleSet.FromJson(_rulesJson), broker, clock);
        await using var session = new Session(new LoopbackTransport(Options.Create(new LoopbackTransportOptions())), new Pipeline([]));
        await session.OpenAsync(TestContext.CancellationToken);

        // Live: added to the open connection, no reconnect.
        session.AddPresenter(router);

        // Broker "MEAS" -> device "MEAS?" -> the loopback sample -> published back to the broker.
        router.OnBrokerMessage("devterm/loopback/cmd", "MEAS");
        await WaitUntilAsync(() => broker.Published.Count >= 1);

        Assert.AreEqual(1, broker.Published.Count);
        Assert.AreEqual("devterm/loopback/sensor", broker.Published[0].Topic);
        StringAssert.StartsWith(broker.Published[0].Payload, "a=");
        Assert.AreEqual(2, router.History.Count);
        Assert.AreEqual(RoutingDirection.BrokerToDevice, router.History[0].Direction);
        Assert.AreEqual(RoutingDirection.DeviceToBroker, router.History[1].Direction);
        Assert.AreEqual(clock.GetUtcNow(), router.History[0].Timestamp);
    }

    [TestMethod]
    public async Task BrokerMessage_MatchingNoRule_IsIgnored()
    {
        var router = new MessageRouter(RoutingRuleSet.FromJson(_rulesJson), new FakeBroker());
        var sent = false;
        router.Originated += (_, _) => sent = true;

        router.OnBrokerMessage("devterm/other", "MEAS");
        router.OnBrokerMessage("devterm/loopback/cmd", "not a word!");
        await Task.Delay(20, TestContext.CancellationToken);

        Assert.IsFalse(sent);
        Assert.AreEqual(0, router.History.Count);
    }
}
