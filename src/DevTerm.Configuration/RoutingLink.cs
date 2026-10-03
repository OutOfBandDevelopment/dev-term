using DevTerm.Core.Routing;
using DevTerm.Transports.Brokers;
using DevTerm.Transports.Mqtt;
using Microsoft.Extensions.Options;

namespace DevTerm.Configuration;

/// <summary>One live connection between a <see cref="MessageRouter"/> and a broker, whichever protocol; <see cref="RoutingService"/> rebuilds it after a loss.</summary>
public interface IRoutingLink : IAsyncDisposable
{
    /// <summary>The connection ended after a successful start; the argument is the failure, or null for a clean close.</summary>
    event Action<Exception?>? Lost;

    Task StartAsync(MessageRouter router, CancellationToken cancellationToken);

    Task PublishAsync(string topic, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);
}

public interface IRoutingLinkFactory
{
    IRoutingLink Create(RoutingOptions options);
}

/// <summary>Builds the MQTT, AMQP or STOMP link a profile's <see cref="RoutingOptions.Protocol"/> names.</summary>
public sealed class RoutingLinkFactory : IRoutingLinkFactory
{
    public IRoutingLink Create(RoutingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var rules = new RoutingRuleSet { Rules = options.Rules };
        var topics = options.Rules.Where(r => r.Direction == RoutingDirection.BrokerToDevice).Select(r => r.Topic).ToList();
        switch (options.Protocol.ToLowerInvariant())
        {
            case "amqp":
            case "stomp":
                var broker = new BrokerTransportOptions
                {
                    Host = options.Host,
                    Port = options.EffectivePort,
                    Username = options.Username,
                    Password = options.Password,
                    UseTls = options.Tls,
                    SubscribeTopics = topics,
                };
                IBrokerConnectionFactory factory = options.Protocol.Equals("amqp", StringComparison.OrdinalIgnoreCase)
                    ? new AmqpConnectionFactory()
                    : new StompConnectionFactory();
                return new BrokerLink(new BrokerRouterBridge(factory, Options.Create(broker), rules));
            default:
                var mqtt = new MqttTransportOptions
                {
                    Host = options.Host,
                    Port = options.EffectivePort,
                    Username = options.Username,
                    Password = options.Password,
                    SubscribeTopics = topics,
                };
                return new MqttLink(new MqttRouterBridge(new MqttNetConnectionFactory(), Options.Create(mqtt), rules));
        }
    }

    private sealed class MqttLink(MqttRouterBridge bridge) : IRoutingLink
    {
        public event Action<Exception?>? Lost
        {
            add => bridge.Lost += value;
            remove => bridge.Lost -= value;
        }

        public Task StartAsync(MessageRouter router, CancellationToken cancellationToken) => bridge.StartAsync(router, cancellationToken);

        public Task PublishAsync(string topic, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) => bridge.PublishAsync(topic, payload, cancellationToken);

        public ValueTask DisposeAsync() => bridge.DisposeAsync();
    }

    private sealed class BrokerLink(BrokerRouterBridge bridge) : IRoutingLink
    {
        public event Action<Exception?>? Lost
        {
            add => bridge.Lost += value;
            remove => bridge.Lost -= value;
        }

        public Task StartAsync(MessageRouter router, CancellationToken cancellationToken) => bridge.StartAsync(router, cancellationToken);

        public Task PublishAsync(string topic, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) => bridge.PublishAsync(topic, payload, cancellationToken);

        public ValueTask DisposeAsync() => bridge.DisposeAsync();
    }
}
