using System.Net.Sockets;
using System.Text;
using DevTerm.Core.Transports;
using DevTerm.Test.Utilities;
using Microsoft.Extensions.Options;

namespace DevTerm.Transports.Mqtt.Tests;

/// <summary>Round trip through the real Mosquitto broker in containers/ (docker compose -f containers/docker-compose.yml up -d mosquitto).</summary>
[TestCategory(TestCategories.Integration)]
[TestCategory(TestCategories.Mqtt)]
[TestClass]
public sealed class MosquittoIntegrationTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task PublishedLine_ComesBackAsTopicTabPayload()
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

        var options = new MqttTransportOptions { Host = "127.0.0.1", Port = 1883, SubscribeTopics = ["devterm-it/#"], PublishTopic = "devterm-it/out" };
        await using var transport = new MqttTransport(new MqttNetConnectionFactory(), Options.Create(options));
        await transport.OpenAsync(TestContext.CancellationToken);

        await transport.WriteAsync("hello broker\n"u8.ToArray(), TestContext.CancellationToken);

        var result = await transport.Input.ReadAsync(TestContext.CancellationToken);
        Assert.AreEqual("devterm-it/out\thello broker\n", Encoding.UTF8.GetString(result.Buffer));
        Assert.AreEqual(ConnectionState.Open, transport.State);
    }
}
