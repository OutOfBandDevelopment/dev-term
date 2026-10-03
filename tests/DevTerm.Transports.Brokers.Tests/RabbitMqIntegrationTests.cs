using System.Net.Sockets;
using System.Text;
using DevTerm.Core.Transports;
using DevTerm.Test.Utilities;
using Microsoft.Extensions.Options;

namespace DevTerm.Transports.Brokers.Tests;

/// <summary>Round trips through the real RabbitMQ in containers/ (docker compose -f containers/docker-compose.yml up -d rabbitmq): AMQP 0-9-1 on 5672, STOMP on 21613.</summary>
[TestCategory(TestCategories.Integration)]
[TestCategory(TestCategories.Brokers)]
[TestClass]
public sealed class RabbitMqIntegrationTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task Amqp_PublishedLine_ComesBackAsRoutingKeyTabPayload()
    {
        await RequireAsync(5672);
        var options = new BrokerTransportOptions { Host = "127.0.0.1", Port = 5672, Username = "devterm", Password = "devterm", SubscribeTopics = ["devterm-it.#"], PublishTopic = "devterm-it.out" };
        await using var transport = new BrokerTransport(new AmqpConnectionFactory(), Options.Create(options));
        await transport.OpenAsync(TestContext.CancellationToken);

        await transport.WriteAsync("hello amqp\n"u8.ToArray(), TestContext.CancellationToken);

        Assert.AreEqual("devterm-it.out\thello amqp\n", await ReadAsync(transport));
        Assert.AreEqual(ConnectionState.Open, transport.State);
    }

    [TestMethod]
    public async Task Stomp_PublishedLine_ComesBackAsDestinationTabPayload()
    {
        await RequireAsync(21613);
        var options = new BrokerTransportOptions { Host = "127.0.0.1", Port = 21613, Username = "devterm", Password = "devterm", SubscribeTopics = ["/topic/devterm-it"], PublishTopic = "/topic/devterm-it" };
        await using var transport = new BrokerTransport(new StompConnectionFactory(), Options.Create(options));
        await transport.OpenAsync(TestContext.CancellationToken);

        await transport.WriteAsync("hello stomp\n"u8.ToArray(), TestContext.CancellationToken);

        Assert.AreEqual("/topic/devterm-it\thello stomp\n", await ReadAsync(transport));
    }

    [TestMethod]
    public async Task Stomp_WrongPassword_FailsTheOpen()
    {
        await RequireAsync(21613);
        var options = new BrokerTransportOptions { Host = "127.0.0.1", Port = 21613, Username = "devterm", Password = "wrong" };
        await using var transport = new BrokerTransport(new StompConnectionFactory(), Options.Create(options));

        await Assert.ThrowsAsync<IOException>(() => transport.OpenAsync(TestContext.CancellationToken));

        Assert.AreEqual(ConnectionState.Faulted, transport.State);
    }

    private async Task<string> ReadAsync(BrokerTransport transport)
    {
        var result = await transport.Input.ReadAsync(TestContext.CancellationToken);
        return Encoding.UTF8.GetString(result.Buffer);
    }

    private async Task RequireAsync(int port)
    {
        using var probe = new TcpClient();
        try
        {
            await probe.ConnectAsync("127.0.0.1", port, TestContext.CancellationToken);
        }
        catch (SocketException)
        {
            Assert.Inconclusive($"No broker on 127.0.0.1:{port}; start containers/docker-compose.yml (rabbitmq).");
        }
    }
}
