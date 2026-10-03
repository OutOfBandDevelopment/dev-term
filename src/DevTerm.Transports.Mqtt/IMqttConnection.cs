namespace DevTerm.Transports.Mqtt;

/// <summary>The slice of an MQTT client the transport needs, so tests can fake the broker side.</summary>
public interface IMqttConnection : IAsyncDisposable
{
    /// <summary>A message arrived on a subscribed topic.</summary>
    event Action<string, byte[]>? MessageReceived;

    /// <summary>The broker connection ended; the argument is the failure, or null for a clean close.</summary>
    event Action<Exception?>? Disconnected;

    Task ConnectAsync(MqttTransportOptions options, CancellationToken cancellationToken);

    Task SubscribeAsync(string topicFilter, int qualityOfService, CancellationToken cancellationToken);

    Task PublishAsync(string topic, ReadOnlyMemory<byte> payload, int qualityOfService, CancellationToken cancellationToken);

    Task DisconnectAsync(CancellationToken cancellationToken);
}

public interface IMqttConnectionFactory
{
    IMqttConnection Create();
}
