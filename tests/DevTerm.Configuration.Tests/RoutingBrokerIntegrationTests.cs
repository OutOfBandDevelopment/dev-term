using System.Net.Sockets;
using System.Text;
using DevTerm.Core.Routing;
using DevTerm.Test.Utilities;
using DevTerm.Transports.Mqtt;
using Microsoft.Extensions.Options;

namespace DevTerm.Configuration.Tests;

/// <summary>RoutingService through the real Mosquitto broker in containers/ (docker compose -f containers/docker-compose.yml up -d mosquitto).</summary>
[TestCategory(TestCategories.Integration)]
[TestCategory(TestCategories.Mqtt)]
[TestClass]
public sealed class RoutingBrokerIntegrationTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task ADeviceLine_IsPublishedToTheBroker_AndABrokerMessageReachesTheRouter()
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

        var options = new RoutingOptions
        {
            Host = "127.0.0.1",
            Rules =
            [
                new RoutingRule { Direction = RoutingDirection.DeviceToBroker, Match = @"^T=(?<t>\d+)$", Topic = "devterm-it/route/temp", Payload = "${t}" },
                new RoutingRule { Direction = RoutingDirection.BrokerToDevice, Match = "(?<v>.*)", Topic = "devterm-it/route/cmd", Send = "SET ${v}" },
            ],
        };

        // An independent subscriber on the same broker, to see what the service published.
        var watcher = new MqttTransportOptions { Host = "127.0.0.1", Port = 1883, SubscribeTopics = ["devterm-it/route/temp"], PublishTopic = "devterm-it/route/cmd" };
        await using var observer = new MqttTransport(new MqttNetConnectionFactory(), Options.Create(watcher));
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
        Assert.AreEqual("devterm-it/route/temp\t21\n", Encoding.UTF8.GetString(published.Buffer).Replace("\r\n", "\n"));

        await observer.WriteAsync("42\n"u8.ToArray(), TestContext.CancellationToken);
        for (var i = 0; i < 300 && service.Router.History.Count < 2; i++)
        {
            await Task.Delay(20, TestContext.CancellationToken);
        }

        Assert.AreEqual(1, service.Router.HitCount(options.Rules[1]));
        Assert.IsTrue(service.Router.History.Any(m => m.Direction == RoutingDirection.BrokerToDevice && m.Payload == "42"), "History: " + string.Join(" | ", service.Router.History.Select(m => $"{m.Direction} {m.Topic} {m.Payload}")));
    }
}
