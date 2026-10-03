using System.IO.Pipelines;
using System.Text;
using DevTerm.Core.Transports;
using DevTerm.Test.Utilities;
using Microsoft.Extensions.Options;

namespace DevTerm.Transports.Mqtt.Tests;

[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Mqtt)]
[TestClass]
public sealed class MqttTransportTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task OpenAsync_ConnectsAndSubscribesToEveryTopic()
    {
        var fake = new FakeConnection();
        await using var transport = Create(fake, new MqttTransportOptions { Host = "b", SubscribeTopics = ["a/#", " ", "b/+"] });

        await transport.OpenAsync(TestContext.CancellationToken);

        Assert.AreEqual(ConnectionState.Open, transport.State);
        CollectionAssert.AreEqual(new[] { "a/#", "b/+" }, fake.Subscribed);
    }

    [TestMethod]
    public async Task OpenAsync_ConnectFailure_FaultsAndRethrows()
    {
        var fake = new FakeConnection { ConnectError = new IOException("refused") };
        await using var transport = Create(fake, new MqttTransportOptions { Host = "b" });

        await Assert.ThrowsExactlyAsync<IOException>(() => transport.OpenAsync(TestContext.CancellationToken));

        Assert.AreEqual(ConnectionState.Faulted, transport.State);
    }

    [TestMethod]
    public async Task InboundMessage_BecomesOneTopicTabPayloadLine()
    {
        var fake = new FakeConnection();
        await using var transport = Create(fake, new MqttTransportOptions { Host = "b" });
        await transport.OpenAsync(TestContext.CancellationToken);

        fake.Receive("sensors/temp", "21.5"u8.ToArray());

        var result = await transport.Input.ReadAsync(TestContext.CancellationToken);
        Assert.AreEqual("sensors/temp\t21.5\n", Encoding.UTF8.GetString(result.Buffer));
    }

    [TestMethod]
    public async Task WriteAsync_PlainLine_GoesToTheConfiguredPublishTopic()
    {
        var fake = new FakeConnection();
        await using var transport = Create(fake, new MqttTransportOptions { Host = "b", PublishTopic = "cmd", QualityOfService = 1 });
        await transport.OpenAsync(TestContext.CancellationToken);

        await transport.WriteAsync("on\r\n"u8.ToArray(), TestContext.CancellationToken);

        Assert.HasCount(1, fake.Published);
        Assert.AreEqual(("cmd", "on", 1), fake.Published[0]);
    }

    [TestMethod]
    public async Task WriteAsync_TopicTabPayload_OverridesThePublishTopic()
    {
        var fake = new FakeConnection();
        await using var transport = Create(fake, new MqttTransportOptions { Host = "b", PublishTopic = "cmd" });
        await transport.OpenAsync(TestContext.CancellationToken);

        await transport.WriteAsync("lamp/1\tdim 40\n"u8.ToArray(), TestContext.CancellationToken);

        Assert.AreEqual(("lamp/1", "dim 40", 0), fake.Published[0]);
    }

    [TestMethod]
    public async Task WriteAsync_NoTopicAnywhere_IsAnErrorNotADisconnect()
    {
        var fake = new FakeConnection();
        await using var transport = Create(fake, new MqttTransportOptions { Host = "b" });
        await transport.OpenAsync(TestContext.CancellationToken);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => transport.WriteAsync("hello\n"u8.ToArray(), TestContext.CancellationToken));

        Assert.AreEqual(ConnectionState.Open, transport.State);
    }

    [TestMethod]
    public async Task WriteAsync_EmptyData_IsANoOp()
    {
        var fake = new FakeConnection();
        await using var transport = Create(fake, new MqttTransportOptions { Host = "b", PublishTopic = "cmd" });
        await transport.OpenAsync(TestContext.CancellationToken);

        await transport.WriteAsync(ReadOnlyMemory<byte>.Empty, TestContext.CancellationToken);
        await transport.WriteAsync("\n"u8.ToArray(), TestContext.CancellationToken);

        Assert.IsEmpty(fake.Published);
    }

    [TestMethod]
    public async Task BrokerDisconnect_FaultsAndCompletesTheInputPipe()
    {
        var fake = new FakeConnection();
        await using var transport = Create(fake, new MqttTransportOptions { Host = "b" });
        await transport.OpenAsync(TestContext.CancellationToken);

        fake.Drop(new IOException("gone"));

        Assert.AreEqual(ConnectionState.Faulted, transport.State);
        await Assert.ThrowsExactlyAsync<IOException>(async () => await transport.Input.ReadAsync(TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task CloseAsync_DisconnectsAndIsIdempotent()
    {
        var fake = new FakeConnection();
        await using var transport = Create(fake, new MqttTransportOptions { Host = "b" });
        await transport.OpenAsync(TestContext.CancellationToken);

        await transport.CloseAsync(TestContext.CancellationToken);
        await transport.CloseAsync(TestContext.CancellationToken);

        Assert.AreEqual(ConnectionState.Closed, transport.State);
        Assert.AreEqual(1, fake.DisconnectCount);
    }

    [TestMethod]
    public async Task OpenAsync_AfterClose_ReadsFreshMessages()
    {
        var fake = new FakeConnection();
        await using var transport = Create(fake, new MqttTransportOptions { Host = "b" });
        await transport.OpenAsync(TestContext.CancellationToken);
        await transport.CloseAsync(TestContext.CancellationToken);

        var second = new FakeConnection();
        var reopened = Create(second, new MqttTransportOptions { Host = "b" });
        await reopened.OpenAsync(TestContext.CancellationToken);
        second.Receive("t", "x"u8.ToArray());

        var result = await reopened.Input.ReadAsync(TestContext.CancellationToken);
        Assert.AreEqual("t\tx\n", Encoding.UTF8.GetString(result.Buffer));
    }

    private static MqttTransport Create(FakeConnection fake, MqttTransportOptions options)
        => new(new FakeFactory(fake), Options.Create(options));

    private sealed class FakeFactory(FakeConnection connection) : IMqttConnectionFactory
    {
        public IMqttConnection Create() => connection;
    }

    private sealed class FakeConnection : IMqttConnection
    {
        public Exception? ConnectError { get; init; }

        public List<string> Subscribed { get; } = [];

        public List<(string Topic, string Payload, int Qos)> Published { get; } = [];

        public int DisconnectCount { get; private set; }

        public event Action<string, byte[]>? MessageReceived;

        public event Action<Exception?>? Disconnected;

        public void Receive(string topic, byte[] payload) => MessageReceived?.Invoke(topic, payload);

        public void Drop(Exception? error) => Disconnected?.Invoke(error);

        public Task ConnectAsync(MqttTransportOptions options, CancellationToken cancellationToken)
            => ConnectError is null ? Task.CompletedTask : Task.FromException(ConnectError);

        public Task SubscribeAsync(string topicFilter, int qualityOfService, CancellationToken cancellationToken)
        {
            Subscribed.Add(topicFilter);
            return Task.CompletedTask;
        }

        public Task PublishAsync(string topic, ReadOnlyMemory<byte> payload, int qualityOfService, CancellationToken cancellationToken)
        {
            Published.Add((topic, Encoding.UTF8.GetString(payload.Span), qualityOfService));
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            DisconnectCount++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
