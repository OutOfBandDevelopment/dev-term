using System.ComponentModel.DataAnnotations;

namespace DevTerm.Transports.Brokers;

/// <summary>
/// Configuration shared by the AMQP 0-9-1 and STOMP transports: one broker connection, any number of subscriptions and one
/// default publish address. A subscription or publish "address" is an AMQP routing key (on <see cref="Exchange"/>) or a
/// STOMP destination (for RabbitMQ, <c>/topic/x</c>, <c>/queue/x</c> or <c>/exchange/amq.topic/key</c>).
/// See docs/design/proposals/message-broker-protocols.md.
/// </summary>
public sealed class BrokerTransportOptions
{
    [Required(AllowEmptyStrings = false)]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; } = 5672;

    public string? Username { get; set; }

    public string? Password { get; set; }

    /// <summary>AMQP virtual host / STOMP <c>host</c> header; blank means <c>/</c> (AMQP) or <see cref="Host"/> (STOMP).</summary>
    public string? VirtualHost { get; set; }

    /// <summary>AMQP only: the exchange subscriptions bind to and publishes go to. Blank means <c>amq.topic</c>.</summary>
    public string? Exchange { get; set; }

    /// <summary>Routing keys (AMQP, wildcards <c>*</c>/<c>#</c>) or destinations (STOMP) to subscribe to on open. Empty means publish-only.</summary>
    public List<string> SubscribeTopics { get; set; } = [];

    /// <summary>Where a sent line goes unless it names its own address (<c>address&lt;TAB&gt;payload</c>).</summary>
    public string? PublishTopic { get; set; }

    /// <summary>Encrypt the connection (AMQPS / STOMP over TLS). The server certificate must validate against the system trust store or <see cref="TlsCaCertificatePath"/>.</summary>
    public bool UseTls { get; set; }

    /// <summary>Optional PEM/CER file of an extra CA to trust for the server certificate (a private or test CA).</summary>
    public string? TlsCaCertificatePath { get; set; }

    /// <summary>Milliseconds a connect, subscribe or publish may take before it fails.</summary>
    [Range(1, int.MaxValue)]
    public int TimeoutMs { get; set; } = 5000;
}
