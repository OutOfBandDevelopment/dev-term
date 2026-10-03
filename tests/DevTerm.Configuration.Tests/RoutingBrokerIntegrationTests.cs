using System.Net.Sockets;
using System.Text;
using DevTerm.Core.Routing;
using DevTerm.Core.Transports;
using DevTerm.Test.Utilities;
using DevTerm.Transports.Brokers;
using DevTerm.Transports.Mqtt;
using Microsoft.Extensions.Options;

namespace DevTerm.Configuration.Tests;

/// <summary>RoutingService through the real Mosquitto broker in containers/ (docker compose -f containers/docker-compose.yml up -d mosquitto).</summary>
[TestCategory(TestCategories.Integration)]
[TestCategory(TestCategories.Mqtt)]
[TestCategory(TestCategories.Brokers)]
[TestClass]
public sealed class RoutingBrokerIntegrationTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow("mqtt", 1883, "devterm-it/route/temp", "devterm-it/route/cmd")]
    [DataRow("amqp", 5672, "devterm-it.route.temp", "devterm-it.route.cmd")]
    [DataRow("stomp", 21613, "/topic/devterm-it-route-temp", "/topic/devterm-it-route-cmd")]
    public async Task ADeviceLine_IsPublishedToTheBroker_AndABrokerMessageReachesTheRouter(string protocol, int port, string tempTopic, string cmdTopic)
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

        var options = new RoutingOptions
        {
            Protocol = protocol,
            Host = "127.0.0.1",
            Port = port,
            Username = protocol == "mqtt" ? null : "devterm",
            Password = protocol == "mqtt" ? null : "devterm",
            Rules =
            [
                new RoutingRule { Direction = RoutingDirection.DeviceToBroker, Match = @"^T=(?<t>\d+)$", Topic = tempTopic, Payload = "${t}" },
                new RoutingRule { Direction = RoutingDirection.BrokerToDevice, Match = "(?<v>.*)", Topic = cmdTopic, Send = "SET ${v}" },
            ],
        };

        // An independent subscriber on the same broker, to see what the service published.
        await using var observer = NewObserver(protocol, port, tempTopic, cmdTopic);
        await observer.OpenAsync(TestContext.CancellationToken);

        var session = DevTermSessionBuilder.Build(new CliOptions { Transport = "loopback" }).Session;
        await using var service = new RoutingService(options, new RoutingLinkFactory());
        service.Start(session);
        for (var i = 0; i < 300 && service.State != RoutingState.Connected; i++)
        {
            await Task.Delay(20, TestContext.CancellationToken);
        }

        Assert.AreEqual(RoutingState.Connected, service.State, service.Reason);

        service.Router!.Render(new System.Buffers.ReadOnlySequence<byte>(Encoding.UTF8.GetBytes("T=21\r\n")));
        var published = await observer.Input.ReadAsync(TestContext.CancellationToken);
        Assert.AreEqual($"{tempTopic}\t21\n",Encoding.UTF8.GetString(published.Buffer).Replace("\r\n", "\n"));

        await observer.WriteAsync("42\n"u8.ToArray(), TestContext.CancellationToken);
        for (var i = 0; i < 300 && service.Router.History.Count < 2; i++)
        {
            await Task.Delay(20, TestContext.CancellationToken);
        }

        Assert.AreEqual(1, service.Router.HitCount(options.Rules[1]));
        Assert.IsTrue(service.Router.History.Any(m => m.Direction == RoutingDirection.BrokerToDevice && m.Payload == "42"), "History: " + string.Join(" | ", service.Router.History.Select(m => $"{m.Direction} {m.Topic} {m.Payload}")));
    }

    private static ITransport NewObserver(string protocol, int port, string subscribe, string publish)
    {
        if (protocol == "mqtt")
        {
            var mqtt = new MqttTransportOptions { Host = "127.0.0.1", Port = port, SubscribeTopics = [subscribe], PublishTopic = publish };
            return new MqttTransport(new MqttNetConnectionFactory(), Options.Create(mqtt));
        }

        var broker = new BrokerTransportOptions { Host = "127.0.0.1", Port = port, Username = "devterm", Password = "devterm", SubscribeTopics = [subscribe], PublishTopic = publish };
        IBrokerConnectionFactory factory = protocol == "amqp" ? new AmqpConnectionFactory() : new StompConnectionFactory();
        return new BrokerTransport(factory, Options.Create(broker));
    }
}
