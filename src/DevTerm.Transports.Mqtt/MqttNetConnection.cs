using System.Buffers;
using MQTTnet;
using MQTTnet.Protocol;

namespace DevTerm.Transports.Mqtt;

/// <summary>The production <see cref="IMqttConnection"/>, backed by MQTTnet.</summary>
internal sealed class MqttNetConnection : IMqttConnection
{
    private readonly MqttClientFactory _factory = new();
    private readonly IMqttClient _client;

    public MqttNetConnection()
    {
        _client = _factory.CreateMqttClient();
        _client.ApplicationMessageReceivedAsync += e =>
        {
            MessageReceived?.Invoke(e.ApplicationMessage.Topic, e.ApplicationMessage.Payload.ToArray());
            return Task.CompletedTask;
        };
        _client.DisconnectedAsync += e =>
        {
            Disconnected?.Invoke(e.Exception);
            return Task.CompletedTask;
        };
    }

    public event Action<string, byte[]>? MessageReceived;

    public event Action<Exception?>? Disconnected;

    public async Task ConnectAsync(MqttTransportOptions options, CancellationToken cancellationToken)
    {
        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(options.Host, options.Port)
            .WithTimeout(TimeSpan.FromMilliseconds(options.TimeoutMs));
        if (!string.IsNullOrEmpty(options.ClientId))
        {
            builder.WithClientId(options.ClientId);
        }

        if (!string.IsNullOrEmpty(options.Username))
        {
            builder.WithCredentials(options.Username, options.Password);
        }

        await _client.ConnectAsync(builder.Build(), cancellationToken);
    }

    public async Task SubscribeAsync(string topicFilter, int qualityOfService, CancellationToken cancellationToken)
    {
        var subscribe = _factory.CreateSubscribeOptionsBuilder()
            .WithTopicFilter(topicFilter, (MqttQualityOfServiceLevel)qualityOfService)
            .Build();
        await _client.SubscribeAsync(subscribe, cancellationToken);
    }

    public async Task PublishAsync(string topic, ReadOnlyMemory<byte> payload, int qualityOfService, CancellationToken cancellationToken)
    {
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload.ToArray())
            .WithQualityOfServiceLevel((MqttQualityOfServiceLevel)qualityOfService)
            .Build();
        await _client.PublishAsync(message, cancellationToken);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        if (_client.IsConnected)
        {
            await _client.DisconnectAsync(cancellationToken: cancellationToken);
        }
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }
}

internal sealed class MqttNetConnectionFactory : IMqttConnectionFactory
{
    public IMqttConnection Create() => new MqttNetConnection();
}
