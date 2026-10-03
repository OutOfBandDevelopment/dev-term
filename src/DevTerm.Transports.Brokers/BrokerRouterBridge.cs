using System.Text;
using DevTerm.Core.Routing;
using Microsoft.Extensions.Options;

namespace DevTerm.Transports.Brokers;

/// <summary>
/// Connects a <see cref="MessageRouter"/> to an AMQP or STOMP broker (whichever <see cref="IBrokerConnectionFactory"/> it is given): it is
/// the router's <see cref="IMessageSink"/>, subscribes to the address of every broker-to-device rule, and feeds each arriving message to
/// <see cref="MessageRouter.OnBrokerMessage"/>. Same shape as the MQTT bridge. Create the router with <see cref="Sink"/>, then call <see cref="StartAsync"/>.
/// </summary>
public sealed class BrokerRouterBridge : IMessageSink, IAsyncDisposable
{
    private readonly IBrokerConnectionFactory _factory;
    private readonly BrokerTransportOptions _options;
    private readonly RoutingRuleSet _rules;
    private IBrokerConnection? _connection;
    private MessageRouter? _router;

    public BrokerRouterBridge(IBrokerConnectionFactory factory, IOptions<BrokerTransportOptions> options, RoutingRuleSet rules)
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

    /// <summary>The broker connection ended after a successful start; the argument is the failure, or null for a clean close.</summary>
    public event Action<Exception?>? Lost;

    /// <summary>Connects, subscribes to every broker-to-device rule's address, and starts handing broker messages to <paramref name="router"/>.</summary>
    public async Task StartAsync(MessageRouter router, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(router);
        _router = router;
        var connection = _factory.Create();
        connection.MessageReceived += OnMessage;
        connection.Disconnected += OnLost;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.TimeoutMs);
            await connection.ConnectAsync(_options, timeout.Token).ConfigureAwait(false);
            var addresses = _rules.Rules
                .Where(r => r.Direction == RoutingDirection.BrokerToDevice && !string.IsNullOrWhiteSpace(r.Topic))
                .Select(r => r.Topic)
                .Distinct(StringComparer.Ordinal);
            foreach (var address in addresses)
            {
                await connection.SubscribeAsync(address, timeout.Token).ConfigureAwait(false);
            }
        }
        catch
        {
            connection.MessageReceived -= OnMessage;
            connection.Disconnected -= OnLost;
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        _connection = connection;
    }

    public async Task PublishAsync(string topic, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        var connection = _connection ?? throw new InvalidOperationException("The broker bridge is not started.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.TimeoutMs);
        await connection.PublishAsync(topic, payload, timeout.Token).ConfigureAwait(false);
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
        connection.Disconnected -= OnLost;
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

    private void OnLost(Exception? error) => Lost?.Invoke(error);

    private void OnMessage(string address, byte[] payload) => _router?.OnBrokerMessage(address, Encoding.UTF8.GetString(payload));
}
