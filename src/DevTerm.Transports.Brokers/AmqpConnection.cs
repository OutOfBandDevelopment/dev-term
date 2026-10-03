using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace DevTerm.Transports.Brokers;

/// <summary>
/// AMQP 0-9-1 over RabbitMQ.Client. A subscription binds a private, auto-deleted queue to the exchange with the given
/// routing key; a publish goes to the exchange with the given routing key.
/// </summary>
internal sealed class AmqpConnection : IBrokerConnection
{
    private const string _defaultExchange = "amq.topic";

    private IConnection? _connection;
    private IChannel? _channel;
    private string _exchange = _defaultExchange;
    private bool _closing;

    public event Action<string, byte[]>? MessageReceived;

    public event Action<Exception?>? Disconnected;

    public async Task ConnectAsync(BrokerTransportOptions options, CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = options.Host,
            Port = options.Port,
            VirtualHost = string.IsNullOrWhiteSpace(options.VirtualHost) ? "/" : options.VirtualHost,
            UserName = string.IsNullOrEmpty(options.Username) ? "guest" : options.Username,
            Password = string.IsNullOrEmpty(options.Password) ? "guest" : options.Password,
            RequestedConnectionTimeout = TimeSpan.FromMilliseconds(options.TimeoutMs),
        };
        _exchange = string.IsNullOrWhiteSpace(options.Exchange) ? _defaultExchange : options.Exchange;
        _connection = await factory.CreateConnectionAsync(cancellationToken);
        _connection.ConnectionShutdownAsync += (_, e) =>
        {
            if (!_closing)
            {
                Disconnected?.Invoke(e.Initiator == ShutdownInitiator.Application ? null : new IOException(e.ReplyText));
            }

            return Task.CompletedTask;
        };
        _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
    }

    public async Task SubscribeAsync(string address, CancellationToken cancellationToken)
    {
        var channel = _channel ?? throw new InvalidOperationException("The AMQP connection is not open.");
        var queue = (await channel.QueueDeclareAsync(string.Empty, durable: false, exclusive: true, autoDelete: true, cancellationToken: cancellationToken)).QueueName;
        await channel.QueueBindAsync(queue, _exchange, address, cancellationToken: cancellationToken);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, e) =>
        {
            MessageReceived?.Invoke(e.RoutingKey, e.Body.ToArray());
            return Task.CompletedTask;
        };
        await channel.BasicConsumeAsync(queue, autoAck: true, consumer, cancellationToken);
    }

    public async Task PublishAsync(string address, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var channel = _channel ?? throw new InvalidOperationException("The AMQP connection is not open.");
        await channel.BasicPublishAsync(_exchange, address, payload, cancellationToken);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        _closing = true;
        if (_connection is { IsOpen: true })
        {
            await _connection.CloseAsync(cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _closing = true;
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }
}

internal sealed class AmqpConnectionFactory : IBrokerConnectionFactory
{
    public IBrokerConnection Create() => new AmqpConnection();
}
