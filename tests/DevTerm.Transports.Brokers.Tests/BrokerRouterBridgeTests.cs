using System.Buffers;
using System.Net.Sockets;
using System.Text;
using DevTerm.Core.Routing;
using DevTerm.Test.Utilities;
using Microsoft.Extensions.Options;

namespace DevTerm.Transports.Brokers.Tests;

/// <summary>A <see cref="MessageRouter"/> wired to AMQP and STOMP through <see cref="BrokerRouterBridge"/>: a fake connection, then the RabbitMQ container in containers/.</summary>
[TestCategory(TestCategories.Brokers)]
[TestClass]
public sealed class BrokerRouterBridgeTests
{
    public TestContext TestContext { get; set; } = null!;

    private static RoutingRuleSet Rules(string sensor, string command) => RoutingRuleSet.FromJson($$"""
        {
          "rules": [
            { "direction": "DeviceToBroker", "match": "^A=(?<a>[0-9.]+)", "topic": "{{sensor}}", "payload": "a=${a}" },
            { "direction": "BrokerToDevice", "topic": "{{command}}", "match": "^(?<cmd>[A-Za-z]+)$", "send": "${cmd}?" }
          ]
        }
        """);

    private sealed class FakeConnection : IBrokerConnection
    {
        public List<string> Subscribed { get; } = [];

        public List<(string Address, string Payload)> Published { get; } = [];

        public event Action<string, byte[]>? MessageReceived;

        public event Action<Exception?>? Disconnected
        {
            add { }
            remove { }
        }

        public void Receive(string address, string payload) => MessageReceived?.Invoke(address, Encoding.UTF8.GetBytes(payload));

        public Task ConnectAsync(BrokerTransportOptions options, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SubscribeAsync(string address, CancellationToken cancellationToken)
        {
            Subscribed.Add(address);
            return Task.CompletedTask;
        }

        public Task PublishAsync(string address, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
        {
            lock (Published)
            {
                Published.Add((address, Encoding.UTF8.GetString(payload.Span)));
            }

            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeFactory(FakeConnection connection) : IBrokerConnectionFactory
    {
        public IBrokerConnection Create() => connection;
    }

    [TestMethod]
    [TestCategory(TestCategories.Unit)]
    public async Task Start_SubscribesToTheBrokerToDeviceAddresses_AndRoutesBothWays()
    {
        var fake = new FakeConnection();
        var rules = Rules("it.sensor", "it.cmd");
        await using var bridge = new BrokerRouterBridge(new FakeFactory(fake), Options.Create(new BrokerTransportOptions { Host = "x" }), rules);
        var router = new MessageRouter(rules, bridge.Sink, terminator: "\n");
        var sent = new List<string>();
        router.Originated += (_, bytes) => sent.Add(Encoding.ASCII.GetString(bytes.Span));
        await bridge.StartAsync(router, TestContext.CancellationToken);

        CollectionAssert.AreEqual(new[] { "it.cmd" }, fake.Subscribed);

        fake.Receive("it.cmd", "STATUS");
        CollectionAssert.AreEqual(new[] { "STATUS?\n" }, sent);

        router.Render(new ReadOnlySequence<byte>("A=21.5\r\n"u8.ToArray()));
        await WaitAsync(() => { lock (fake.Published) { return fake.Published.Count == 1; } });
        Assert.AreEqual(("it.sensor", "a=21.5"), fake.Published[0]);
    }

    [TestMethod]
    [TestCategory(TestCategories.Unit)]
    public async Task Publish_BeforeStart_Throws()
    {
        await using var bridge = new BrokerRouterBridge(new FakeFactory(new FakeConnection()), Options.Create(new BrokerTransportOptions { Host = "x" }), Rules("a", "b"));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => bridge.PublishAsync("t", "p"u8.ToArray(), TestContext.CancellationToken));
    }

    [TestMethod]
    [TestCategory(TestCategories.Integration)]
    public Task RealRabbitMq_Amqp_RoutesBothWays() =>
        RoundTripAsync(new AmqpConnectionFactory(), 5672, "devterm-it.router.sensor", "devterm-it.router.cmd");

    [TestMethod]
    [TestCategory(TestCategories.Integration)]
    public Task RealRabbitMq_Stomp_RoutesBothWays() =>
        RoundTripAsync(new StompConnectionFactory(), 21613, "/topic/devterm-it-router-sensor", "/topic/devterm-it-router-cmd");

    private async Task RoundTripAsync(IBrokerConnectionFactory factory, int port, string sensor, string command)
    {
        using (var probe = new TcpClient())
        {
            try
            {
                await probe.ConnectAsync("127.0.0.1", port, TestContext.CancellationToken);
            }
            catch (SocketException)
            {
                Assert.Inconclusive($"No broker on 127.0.0.1:{port}; start containers/docker-compose.yml.");
            }
        }

        var options = new BrokerTransportOptions { Host = "127.0.0.1", Port = port, Username = "devterm", Password = "devterm" };
        var rules = Rules(sensor, command);
        await using var bridge = new BrokerRouterBridge(factory, Options.Create(options), rules);
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

        // A second client plays the rest of the system: it listens for the sensor address and issues the command.
        await using var peer = factory.Create();
        var heard = new List<string>();
        peer.MessageReceived += (address, payload) =>
        {
            lock (heard)
            {
                heard.Add(address + "=" + Encoding.UTF8.GetString(payload));
            }
        };
        await peer.ConnectAsync(options, TestContext.CancellationToken);
        await peer.SubscribeAsync(sensor, TestContext.CancellationToken);

        // STOMP SUBSCRIBE carries no receipt, so give both subscriptions a moment to register before publishing across connections.
        await Task.Delay(500, TestContext.CancellationToken);

        router.Render(new ReadOnlySequence<byte>("A=7.25\r\n"u8.ToArray()));
        await peer.PublishAsync(command, "PING"u8.ToArray(), TestContext.CancellationToken);

        await WaitAsync(() => { lock (heard) { return heard.Count == 1; } });
        await WaitAsync(() => { lock (sent) { return sent.Count == 1; } });
        Assert.AreEqual(sensor + "=a=7.25", heard[0]);
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
