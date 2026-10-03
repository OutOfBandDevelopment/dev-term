using System.Text;
using DevTerm.Core.Routing;
using Microsoft.Extensions.Options;

namespace DevTerm.Transports.Mqtt;

/// <summary>
/// Connects a <see cref="MessageRouter"/> to a real MQTT broker: it is the router's <see cref="IMessageSink"/> (device messages are
/// published), and it subscribes to the topic of every broker-to-device rule and feeds each arriving message to
/// <see cref="MessageRouter.OnBrokerMessage"/>. Create the router with <see cref="Sink"/>, then call <see cref="StartAsync"/>.
/// </summary>
public sealed class MqttRouterBridge : IMessageSink, IAsyncDisposable
{
    private readonly IMqttConnectionFactory _factory;
    private readonly MqttTransportOptions _options;
    private readonly RoutingRuleSet _rules;
    private IMqttConnection? _connection;
    private MessageRouter? _router;

    public MqttRouterBridge(IMqttConnectionFactory factory, IOptions<MqttTransportOptions> options, RoutingRuleSet rules)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(rules);
        _factory = factory;
        _options = options.Value;
        _rules = rules;
    }

    /// <summary>The sink to give the <see cref="MessageRouter"/>.</summary>
    public IMessageSink Sink => this;

    /// <summary>Connects, subscribes to every broker-to-device rule's topic, and starts handing broker messages to <paramref name="router"/>.</summary>
    public async Task StartAsync(MessageRouter router, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(router);
        _router = router;
        var connection = _factory.Create();
        connection.MessageReceived += OnMessage;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.TimeoutMs);
            await connection.ConnectAsync(_options, timeout.Token).ConfigureAwait(false);
            var topics = _rules.Rules
                .Where(r => r.Direction == RoutingDirection.BrokerToDevice && !string.IsNullOrWhiteSpace(r.Topic))
                .Select(r => r.Topic)
                .Distinct(StringComparer.Ordinal);
            foreach (var topic in topics)
            {
                await connection.SubscribeAsync(topic, _options.QualityOfService, timeout.Token).ConfigureAwait(false);
            }
        }
        catch
        {
            connection.MessageReceived -= OnMessage;
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        _connection = connection;
    }

    public async Task PublishAsync(string topic, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        var connection = _connection ?? throw new InvalidOperationException("The MQTT bridge is not started.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.TimeoutMs);
        await connection.PublishAsync(topic, payload, _options.QualityOfService, timeout.Token).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        var connection = _connection;
        _connection = null;
        if (connection is null)
        {
            return;
        }

        connection.MessageReceived -= OnMessage;
        try
        {
            await connection.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Already gone: closing is idempotent.
        }

        await connection.DisposeAsync().ConfigureAwait(false);
    }

    private void OnMessage(string topic, byte[] payload) => _router?.OnBrokerMessage(topic, Encoding.UTF8.GetString(payload));
}
