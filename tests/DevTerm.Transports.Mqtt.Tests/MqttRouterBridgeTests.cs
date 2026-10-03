using System.Buffers;
using System.Net.Sockets;
using System.Text;
using DevTerm.Core.Routing;
using DevTerm.Test.Utilities;
using Microsoft.Extensions.Options;

namespace DevTerm.Transports.Mqtt.Tests;

/// <summary>A <see cref="MessageRouter"/> wired to MQTT through <see cref="MqttRouterBridge"/>: a fake connection, then the real Mosquitto broker in containers/.</summary>
[TestCategory(TestCategories.Mqtt)]
[TestClass]
public sealed class MqttRouterBridgeTests
{
    private const string _rulesJson = """
        {
          "rules": [
            { "direction": "DeviceToBroker", "match": "^A=(?<a>[0-9.]+)", "topic": "devterm-it/router/sensor", "payload": "a=${a}" },
            { "direction": "BrokerToDevice", "topic": "devterm-it/router/cmd", "match": "^(?<cmd>[A-Za-z]+)$", "send": "${cmd}?" }
          ]
        }
        """;

    public TestContext TestContext { get; set; } = null!;

    private sealed class FakeConnection : IMqttConnection
    {
        public List<string> Subscribed { get; } = [];

        public List<(string Topic, string Payload)> Published { get; } = [];

        public event Action<string, byte[]>? MessageReceived;

        public event Action<Exception?>? Disconnected
        {
            add { }
            remove { }
        }

        public void Receive(string topic, string payload) => MessageReceived?.Invoke(topic, Encoding.UTF8.GetBytes(payload));

        public Task ConnectAsync(MqttTransportOptions options, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SubscribeAsync(string topicFilter, int qualityOfService, CancellationToken cancellationToken)
        {
            Subscribed.Add(topicFilter);
            return Task.CompletedTask;
        }

        public Task PublishAsync(string topic, ReadOnlyMemory<byte> payload, int qualityOfService, CancellationToken cancellationToken)
        {
            lock (Published)
            {
                Published.Add((topic, Encoding.UTF8.GetString(payload.Span)));
            }

            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeFactory(FakeConnection connection) : IMqttConnectionFactory
    {
        public IMqttConnection Create() => connection;
    }

    [TestMethod]
    [TestCategory(TestCategories.Unit)]
    public async Task Start_SubscribesToTheBrokerToDeviceTopics_AndRoutesBothWays()
    {
        var fake = new FakeConnection();
        var rules = RoutingRuleSet.FromJson(_rulesJson);
        await using var bridge = new MqttRouterBridge(new FakeFactory(fake), Options.Create(new MqttTransportOptions { Host = "x" }), rules);
        var router = new MessageRouter(rules, bridge.Sink, terminator: "\n");
        var sent = new List<string>();
        router.Originated += (_, bytes) => sent.Add(Encoding.ASCII.GetString(bytes.Span));
        await bridge.StartAsync(router, TestContext.CancellationToken);

        CollectionAssert.AreEqual(new[] { "devterm-it/router/cmd" }, fake.Subscribed);

        fake.Receive("devterm-it/router/cmd", "STATUS");
        CollectionAssert.AreEqual(new[] { "STATUS?\n" }, sent);

        router.Render(new ReadOnlySequence<byte>("A=21.5\r\n"u8.ToArray()));
        await WaitAsync(() => { lock (fake.Published) { return fake.Published.Count == 1; } });
        Assert.AreEqual(("devterm-it/router/sensor", "a=21.5"), fake.Published[0]);
    }

    [TestMethod]
    [TestCategory(TestCategories.Unit)]
    public async Task Publish_BeforeStart_Throws()
    {
        var rules = RoutingRuleSet.FromJson(_rulesJson);
        await using var bridge = new MqttRouterBridge(new FakeFactory(new FakeConnection()), Options.Create(new MqttTransportOptions { Host = "x" }), rules);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => bridge.PublishAsync("t", "p"u8.ToArray(), TestContext.CancellationToken));
    }

    [TestMethod]
    [TestCategory(TestCategories.Integration)]
    public async Task RealMosquitto_DeviceLineIsPublished_AndBrokerCommandReachesTheDevice()
    {
        using (var probe = new TcpClient())
        {
            try
            {
                await probe.ConnectAsync("127.0.0.1", 1883, TestContext.CancellationToken);
            }
            catch (SocketException)
            {
                Assert.Inconclusive("No broker on 127.0.0.1:1883; start containers/docker-compose.yml.");
            }
        }

        var rules = RoutingRuleSet.FromJson(_rulesJson);
        var factory = new MqttNetConnectionFactory();
        await using var bridge = new MqttRouterBridge(factory, Options.Create(new MqttTransportOptions { Host = "127.0.0.1" }), rules);
        var router = new MessageRouter(rules, bridge.Sink, terminator: "\n");
        var sent = new List<string>();
        router.Originated += (_, bytes) =>
        {
            lock (sent)
            {
                sent.Add(Encoding.ASCII.GetString(bytes.Span));
            }
        };
        await bridge.StartAsync(router, TestContext.CancellationToken);

        // A second client plays the rest of the system: it listens for the sensor topic and issues the command.
        await using var peer = factory.Create();
        var heard = new List<string>();
        peer.MessageReceived += (topic, payload) =>
        {
            lock (heard)
            {
                heard.Add(topic + "=" + Encoding.UTF8.GetString(payload));
            }
        };
        await peer.ConnectAsync(new MqttTransportOptions { Host = "127.0.0.1" }, TestContext.CancellationToken);
        await peer.SubscribeAsync("devterm-it/router/sensor", 0, TestContext.CancellationToken);

        router.Render(new ReadOnlySequence<byte>("A=7.25\r\n"u8.ToArray()));
        await peer.PublishAsync("devterm-it/router/cmd", "PING"u8.ToArray(), 0, TestContext.CancellationToken);

        await WaitAsync(() => { lock (heard) { return heard.Count == 1; } });
        await WaitAsync(() => { lock (sent) { return sent.Count == 1; } });
        Assert.AreEqual("devterm-it/router/sensor=a=7.25", heard[0]);
        Assert.AreEqual("PING?\n", sent[0]);
    }

    private async Task WaitAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, TestContext.CancellationToken);
        }

        Assert.IsTrue(condition(), "Timed out waiting.");
    }
}
