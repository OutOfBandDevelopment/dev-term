namespace DevTerm.Transports.Brokers;

/// <summary>The slice of a message-broker client the transport needs, so tests can fake the broker side.</summary>
public interface IBrokerConnection : IAsyncDisposable
{
    /// <summary>A message arrived: its address (routing key or destination) and body.</summary>
    event Action<string, byte[]>? MessageReceived;

    /// <summary>The connection ended; the argument is the failure, or null for a clean close.</summary>
    event Action<Exception?>? Disconnected;

    Task ConnectAsync(BrokerTransportOptions options, CancellationToken cancellationToken);

    Task SubscribeAsync(string address, CancellationToken cancellationToken);

    Task PublishAsync(string address, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);

    Task DisconnectAsync(CancellationToken cancellationToken);
}

public interface IBrokerConnectionFactory
{
    IBrokerConnection Create();
}
